using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Managed NuGet/local-folder repository client for PowerShell module packages.
/// </summary>
public sealed partial class ManagedModuleRepositoryClient
{
    private const int PackageCopyBufferSize = 1024 * 1024;

    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private readonly ManagedModulePackageReader _packageReader;
    private readonly ManagedModuleRepositoryClientOptions _options;
    private readonly ConcurrentDictionary<string, string> _packageBaseAddressCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _searchQueryServiceCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _packagePublishAddressCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _registrationBaseAddressCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _packageDownloadLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<ManagedModuleVersionInfo>>>> _versionQueryTasks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Task<ManagedModuleVersionInfo?>>> _latestVersionQueryTasks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<ManagedModuleVersionInfo>>>> _searchQueryTasks = new(StringComparer.Ordinal);
    private long _requestCount;

    /// <summary>
    /// Creates a repository client.
    /// </summary>
    /// <param name="logger">Logger used for diagnostic output.</param>
    /// <param name="httpClient">Optional HTTP client for tests or custom hosting.</param>
    /// <param name="packageReader">Optional package reader.</param>
    /// <param name="options">Optional repository HTTP policy options.</param>
    public ManagedModuleRepositoryClient(
        ILogger logger,
        HttpClient? httpClient = null,
        ManagedModulePackageReader? packageReader = null,
        ManagedModuleRepositoryClientOptions? options = null)
    {
        _logger = logger ?? new NullLogger();
        _options = options ?? new ManagedModuleRepositoryClientOptions();
        _httpClient = httpClient ?? CreateDefaultHttpClient(_options);
        _packageReader = packageReader ?? new ManagedModulePackageReader();
    }

    /// <summary>
    /// Number of HTTP request attempts sent by this repository client instance.
    /// </summary>
    public long RequestCount => System.Threading.Interlocked.Read(ref _requestCount);

    /// <summary>
    /// Lists package versions from the repository.
    /// </summary>
    /// <param name="repository">Repository to query.</param>
    /// <param name="packageId">Package id.</param>
    /// <param name="includePrerelease">Include prerelease versions.</param>
    /// <param name="credential">Optional repository credential.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Versions discovered in the repository.</returns>
    public async Task<IReadOnlyList<ManagedModuleVersionInfo>> GetVersionsAsync(
        ManagedModuleRepository repository,
        string packageId,
        bool includePrerelease = false,
        RepositoryCredential? credential = null,
        CancellationToken cancellationToken = default)
    {
        if (repository is null)
            throw new ArgumentNullException(nameof(repository));
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("Package id is required.", nameof(packageId));

        return repository.Kind switch
        {
            ManagedModuleRepositoryKind.LocalFolder => GetLocalVersions(repository, packageId, includePrerelease),
            ManagedModuleRepositoryKind.NuGetV3 => await ExecuteCoalescedVersionQueryAsync(
                repository,
                packageId,
                includePrerelease,
                credential,
                cancellationToken,
                token => GetNuGetVersionsWithPowerShellGalleryReadApiAsync(repository, packageId, includePrerelease, credential, token)).ConfigureAwait(false),
            ManagedModuleRepositoryKind.NuGetV2 => await ExecuteCoalescedVersionQueryAsync(
                repository,
                packageId,
                includePrerelease,
                credential,
                cancellationToken,
                token => GetNuGetV2VersionsAsync(repository, packageId, includePrerelease, credential, token)).ConfigureAwait(false),
            _ => throw new NotSupportedException($"Repository kind '{repository.Kind}' is not supported.")
        };
    }

    /// <summary>
    /// Gets the latest selected package version from the repository without requiring a full version enumeration when the repository supports it.
    /// </summary>
    /// <param name="repository">Repository to query.</param>
    /// <param name="packageId">Package id.</param>
    /// <param name="includePrerelease">Include prerelease versions.</param>
    /// <param name="credential">Optional repository credential.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The latest selected version, or <c>null</c> when no version was found.</returns>
    public async Task<ManagedModuleVersionInfo?> GetLatestVersionAsync(
        ManagedModuleRepository repository,
        string packageId,
        bool includePrerelease = false,
        RepositoryCredential? credential = null,
        CancellationToken cancellationToken = default)
    {
        if (repository is null)
            throw new ArgumentNullException(nameof(repository));
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("Package id is required.", nameof(packageId));

        return repository.Kind switch
        {
            ManagedModuleRepositoryKind.LocalFolder => GetLocalVersions(repository, packageId, includePrerelease).LastOrDefault(),
            ManagedModuleRepositoryKind.NuGetV3 => await ExecuteCoalescedLatestVersionQueryAsync(
                repository,
                packageId,
                includePrerelease,
                credential,
                cancellationToken,
                token => GetLatestNuGetVersionWithPowerShellGalleryReadApiAsync(repository, packageId, includePrerelease, credential, token)).ConfigureAwait(false),
            ManagedModuleRepositoryKind.NuGetV2 => await ExecuteCoalescedLatestVersionQueryAsync(
                repository,
                packageId,
                includePrerelease,
                credential,
                cancellationToken,
                token => GetLatestNuGetV2VersionAsync(repository, packageId, includePrerelease, credential, token)).ConfigureAwait(false),
            _ => throw new NotSupportedException($"Repository kind '{repository.Kind}' is not supported.")
        };
    }

    /// <summary>
    /// Searches package ids from the repository and returns the latest selected version per match.
    /// </summary>
    /// <param name="repository">Repository to query.</param>
    /// <param name="query">Package id text or wildcard pattern.</param>
    /// <param name="includePrerelease">Include prerelease versions.</param>
    /// <param name="credential">Optional repository credential.</param>
    /// <param name="take">Maximum number of results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Latest selected package versions.</returns>
    public async Task<IReadOnlyList<ManagedModuleVersionInfo>> SearchPackagesAsync(
        ManagedModuleRepository repository,
        string query,
        bool includePrerelease = false,
        RepositoryCredential? credential = null,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        if (repository is null)
            throw new ArgumentNullException(nameof(repository));
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Search query is required.", nameof(query));

        return repository.Kind switch
        {
            ManagedModuleRepositoryKind.LocalFolder => SearchLocalPackages(repository, query, includePrerelease, take),
            ManagedModuleRepositoryKind.NuGetV3 => await ExecuteCoalescedSearchQueryAsync(
                repository,
                query,
                includePrerelease,
                take,
                credential,
                cancellationToken,
                token => SearchNuGetPackagesWithPowerShellGalleryReadApiAsync(repository, query, includePrerelease, credential, take, token)).ConfigureAwait(false),
            ManagedModuleRepositoryKind.NuGetV2 => await ExecuteCoalescedSearchQueryAsync(
                repository,
                query,
                includePrerelease,
                take,
                credential,
                cancellationToken,
                token => SearchNuGetV2PackagesAsync(repository, query, includePrerelease, credential, take, token)).ConfigureAwait(false),
            _ => throw new NotSupportedException($"Repository kind '{repository.Kind}' is not supported.")
        };
    }

    /// <summary>
    /// Downloads or copies a package to a destination directory.
    /// </summary>
    /// <param name="repository">Repository to use.</param>
    /// <param name="packageId">Package id.</param>
    /// <param name="version">Package version.</param>
    /// <param name="destinationDirectory">Directory that receives the package.</param>
    /// <param name="credential">Optional repository credential.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Download result.</returns>
    public async Task<ManagedModuleDownloadResult> DownloadPackageAsync(
        ManagedModuleRepository repository,
        string packageId,
        string version,
        string destinationDirectory,
        RepositoryCredential? credential = null,
        CancellationToken cancellationToken = default)
    {
        if (repository is null)
            throw new ArgumentNullException(nameof(repository));
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("Package id is required.", nameof(packageId));
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Version is required.", nameof(version));
        if (string.IsNullOrWhiteSpace(destinationDirectory))
            throw new ArgumentException("Destination directory is required.", nameof(destinationDirectory));

        packageId = ManagedModulePackageIdentity.RequireSafeId(packageId, nameof(packageId));
        version = ManagedModulePackageIdentity.RequireSafeVersion(version, nameof(version));
        Directory.CreateDirectory(destinationDirectory);
        var destinationPath = BuildDestinationPath(destinationDirectory, repository, packageId, version);
        var packageLock = _packageDownloadLocks.GetOrAdd(destinationPath, static _ => new SemaphoreSlim(1, 1));
        await packageLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cached = TryUseCachedPackage(repository, packageId, version, destinationDirectory);
            if (cached is not null)
                return cached;

            using var downloadRequestScope = BeginRequestScope();
            var result = repository.Kind switch
            {
                ManagedModuleRepositoryKind.LocalFolder => await CopyLocalPackageAsync(repository, packageId, version, destinationDirectory, cancellationToken).ConfigureAwait(false),
                ManagedModuleRepositoryKind.NuGetV3 => await DownloadNuGetPackageWithPowerShellGalleryCdnAsync(repository, packageId, version, destinationDirectory, credential, cancellationToken).ConfigureAwait(false),
                ManagedModuleRepositoryKind.NuGetV2 => await DownloadNuGetV2PackageAsync(repository, packageId, version, destinationDirectory, credential, cancellationToken).ConfigureAwait(false),
                _ => throw new NotSupportedException($"Repository kind '{repository.Kind}' is not supported.")
            };
            result.RedirectCount = downloadRequestScope.RedirectCount;
            return result;
        }
        finally
        {
            packageLock.Release();
        }
    }

    /// <summary>
    /// Reads package metadata from a NuGet package.
    /// </summary>
    /// <param name="packagePath">Path to the package.</param>
    /// <returns>Package metadata.</returns>
    public ManagedModulePackageMetadata ReadPackageMetadata(string packagePath)
        => _packageReader.ReadMetadata(packagePath);

    /// <summary>
    /// Reads package metadata for one exact package version, downloading the package to a temporary location when repository indexes omit metadata.
    /// </summary>
    /// <param name="repository">Repository to query.</param>
    /// <param name="packageId">Package id.</param>
    /// <param name="version">Package version.</param>
    /// <param name="credential">Optional repository credential.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Package metadata, or <c>null</c> when the package could not be read.</returns>
    public async Task<ManagedModulePackageMetadata?> GetPackageMetadataAsync(
        ManagedModuleRepository repository,
        string packageId,
        string version,
        RepositoryCredential? credential = null,
        CancellationToken cancellationToken = default)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PowerForge", "managed-module-metadata", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempRoot);
            var download = await DownloadPackageAsync(
                repository,
                packageId,
                version,
                tempRoot,
                credential,
                cancellationToken).ConfigureAwait(false);

            return download.Metadata;
        }
        catch (ManagedModuleRepositoryException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _logger.Verbose($"Managed module metadata read skipped '{packageId}' {version}: {ex.Message}");
            return null;
        }
        finally
        {
            ManagedModuleExtractedPackageCache.DeleteDirectoryQuietly(tempRoot);
        }
    }

    private async Task<IReadOnlyList<ManagedModuleVersionInfo>> GetNuGetVersionsWithPowerShellGalleryReadApiAsync(
        ManagedModuleRepository repository,
        string packageId,
        bool includePrerelease,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        if (ShouldUsePowerShellGalleryV2ReadApi(repository))
        {
            var fallback = CreatePowerShellGalleryV2Fallback(repository);
            return await GetNuGetV2VersionsAsync(fallback, packageId, includePrerelease, credential, cancellationToken).ConfigureAwait(false);
        }

        return await GetNuGetVersionsAsync(repository, packageId, includePrerelease, credential, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ManagedModuleVersionInfo?> GetLatestNuGetVersionWithPowerShellGalleryReadApiAsync(
        ManagedModuleRepository repository,
        string packageId,
        bool includePrerelease,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        if (ShouldUsePowerShellGalleryV2ReadApi(repository))
        {
            var fallback = CreatePowerShellGalleryV2Fallback(repository);
            return await GetLatestNuGetV2VersionAsync(fallback, packageId, includePrerelease, credential, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return (await GetNuGetVersionsAsync(repository, packageId, includePrerelease, credential, cancellationToken).ConfigureAwait(false))
                .LastOrDefault(static version => version.Listed);
        }
        catch (ManagedModuleRepositoryException ex) when (IsRepositoryPackageNotFound(ex))
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<ManagedModuleVersionInfo>> SearchNuGetPackagesWithPowerShellGalleryReadApiAsync(
        ManagedModuleRepository repository,
        string query,
        bool includePrerelease,
        RepositoryCredential? credential,
        int take,
        CancellationToken cancellationToken)
    {
        if (ShouldUsePowerShellGalleryV2ReadApi(repository))
        {
            var fallback = CreatePowerShellGalleryV2Fallback(repository);
            return await SearchNuGetV2PackagesAsync(fallback, query, includePrerelease, credential, take, cancellationToken).ConfigureAwait(false);
        }

        return await SearchNuGetPackagesAsync(repository, query, includePrerelease, credential, take, cancellationToken).ConfigureAwait(false);
    }

    private ManagedModuleDownloadResult? TryUseCachedPackage(
        ManagedModuleRepository repository,
        string packageId,
        string version,
        string destinationDirectory)
    {
        foreach (var destinationPath in EnumerateCachedPackageCandidates(destinationDirectory, repository, packageId, version))
        {
            if (!File.Exists(destinationPath))
                continue;

            try
            {
                var metadata = _packageReader.ReadMetadata(destinationPath);
                if (!metadata.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase) ||
                    ManagedModuleVersionComparer.Instance.Compare(metadata.Version, version) != 0)
                {
                    continue;
                }

                return new ManagedModuleDownloadResult
                {
                    Name = packageId,
                    Version = version,
                    RepositoryName = repository.Name,
                    Source = destinationPath,
                    PackagePath = destinationPath,
                    BytesWritten = 0,
                    FromCache = true,
                    PackageSha256 = ComputeSha256(destinationPath),
                    Metadata = metadata
                };
            }
            catch (InvalidDataException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        return null;
    }

    private async Task<IReadOnlyList<ManagedModuleVersionInfo>> GetNuGetVersionsAsync(
        ManagedModuleRepository repository,
        string packageId,
        bool includePrerelease,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        var packageBase = await ResolvePackageBaseAddressAsync(repository, credential, cancellationToken).ConfigureAwait(false);
        var lowerId = packageId.Trim().ToLowerInvariant();
        var indexUri = new Uri(new Uri(EnsureTrailingSlash(packageBase)), $"{Uri.EscapeDataString(lowerId)}/index.json");
        using var response = await SendWithPolicyAsync(
            () => CreateRequest(HttpMethod.Get, indexUri, credential, "application/json"),
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return Array.Empty<ManagedModuleVersionInfo>();
        if (!response.IsSuccessStatusCode)
            throw CreateRepositoryHttpException(repository, "VersionQuery", response.StatusCode, $"Unable to query versions for package '{packageId}'.");

        using var document = await ReadJsonDocumentAsync(
            response.Content,
            repository,
            "VersionQuery",
            $"Managed module version query for package '{packageId}' returned malformed JSON.",
            cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("versions", out var versions) || versions.ValueKind != JsonValueKind.Array)
            throw CreateRepositoryContractException(repository, "VersionQuery", $"Repository response did not include a versions array for package '{packageId}'.");

        var listedByVersion = await TryResolveVersionListingMapAsync(repository, packageId, credential, cancellationToken).ConfigureAwait(false);
        return versions.EnumerateArray()
            .Select(static version => version.GetString())
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .Select(version => version!.Trim())
            .Where(version => includePrerelease || !ManagedModuleVersionComparer.IsPrerelease(version))
            .OrderBy(version => version, ManagedModuleVersionComparer.Instance)
            .Select(version => new
            {
                Version = version,
                Listed = !listedByVersion.TryGetValue(version, out var listed) || listed
            })
            .Select(version => new ManagedModuleVersionInfo
            {
                Name = packageId,
                Version = version.Version,
                RepositoryName = repository.Name,
                RepositorySource = repository.Source,
                PackageSource = BuildPackageUri(packageBase, packageId, version.Version).ToString(),
                IsPrerelease = ManagedModuleVersionComparer.IsPrerelease(version.Version),
                Listed = version.Listed
            })
            .ToArray();
    }

    private IReadOnlyList<ManagedModuleVersionInfo> GetLocalVersions(
        ManagedModuleRepository repository,
        string packageId,
        bool includePrerelease)
    {
        var root = ResolveLocalFolder(repository.Source);
        if (!Directory.Exists(root))
            throw CreateLocalRepositoryException(repository, "VersionQuery", $"Local repository folder was not found: {root}");

        var versions = new List<ManagedModuleVersionInfo>();
        foreach (var file in Directory.EnumerateFiles(root, "*.nupkg", SearchOption.AllDirectories))
        {
            ManagedModulePackageMetadata metadata;
            try
            {
                metadata = _packageReader.ReadMetadata(file);
            }
            catch (Exception ex)
            {
                _logger.Verbose($"Managed module local feed skipped '{file}': {ex.Message}");
                continue;
            }

            if (!metadata.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!includePrerelease && metadata.IsPrerelease)
                continue;

            versions.Add(new ManagedModuleVersionInfo
            {
                Name = metadata.Id,
                Version = metadata.Version,
                RepositoryName = repository.Name,
                RepositorySource = repository.Source,
                PackageSource = file,
                IsPrerelease = metadata.IsPrerelease,
                License = metadata.License,
                RequireLicenseAcceptance = metadata.RequireLicenseAcceptance,
                Dependencies = metadata.Dependencies
            });
        }

        return versions
            .OrderBy(version => version.Version, ManagedModuleVersionComparer.Instance)
            .ToArray();
    }

    private IReadOnlyList<ManagedModuleVersionInfo> SearchLocalPackages(
        ManagedModuleRepository repository,
        string query,
        bool includePrerelease,
        int take)
    {
        var root = ResolveLocalFolder(repository.Source);
        if (!Directory.Exists(root))
            throw CreateLocalRepositoryException(repository, "Search", $"Local repository folder was not found: {root}");

        return ReadLocalPackageVersions(repository, root, includePrerelease)
            .Where(version => ManagedModuleSearchMatcher.IsMatch(query, version.Name))
            .GroupBy(version => version.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(version => version.Version, ManagedModuleVersionComparer.Instance).Last())
            .OrderBy(version => version.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, take))
            .ToArray();
    }

    private async Task<IReadOnlyList<ManagedModuleVersionInfo>> SearchNuGetPackagesAsync(
        ManagedModuleRepository repository,
        string query,
        bool includePrerelease,
        RepositoryCredential? credential,
        int take,
        CancellationToken cancellationToken)
    {
        var searchService = await ResolveSearchQueryServiceAsync(repository, credential, cancellationToken).ConfigureAwait(false);
        var searchText = ManagedModuleSearchMatcher.ToSearchText(query);
        var uri = BuildSearchQueryUri(
            searchService,
            $"q={Uri.EscapeDataString(searchText)}&prerelease={includePrerelease.ToString().ToLowerInvariant()}&take={Math.Max(1, take)}&semVerLevel=2.0.0");
        using var response = await SendWithPolicyAsync(
            () => CreateRequest(HttpMethod.Get, uri, credential, "application/json"),
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateRepositoryHttpException(repository, "Search", response.StatusCode, $"Unable to search for '{query}'.");

        using var document = await ReadJsonDocumentAsync(
            response.Content,
            repository,
            "Search",
            $"Managed module search for '{query}' returned malformed JSON.",
            cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw CreateRepositoryContractException(repository, "Search", "Repository response did not include a search data array.");

        return data.EnumerateArray()
            .Select(item => ReadSearchResult(repository, item))
            .Where(version => version is not null && ManagedModuleSearchMatcher.IsMatch(query, version.Name))
            .Select(static version => version!)
            .OrderBy(static version => version.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, take))
            .ToArray();
    }

    private async Task<IReadOnlyDictionary<string, bool>> TryResolveVersionListingMapAsync(
        ManagedModuleRepository repository,
        string packageId,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        try
        {
            var registrationBase = await TryResolveRegistrationBaseAddressAsync(repository, credential, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(registrationBase))
                return EmptyVersionListingMap;

            var lowerId = packageId.Trim().ToLowerInvariant();
            var indexUri = new Uri(new Uri(EnsureTrailingSlash(registrationBase!)), $"{Uri.EscapeDataString(lowerId)}/index.json");
            var versions = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            await ReadRegistrationListingPageAsync(repository, indexUri, credential, versions, depth: 0, cancellationToken).ConfigureAwait(false);
            return versions;
        }
        catch (Exception ex) when (ex is ManagedModuleRepositoryException or InvalidOperationException or JsonException)
        {
            _logger.Verbose($"Managed module listed-version metadata lookup skipped for '{packageId}': {ex.Message}");
            return EmptyVersionListingMap;
        }
    }

    private static readonly IReadOnlyDictionary<string, bool> EmptyVersionListingMap =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

    private async Task ReadRegistrationListingPageAsync(
        ManagedModuleRepository repository,
        Uri uri,
        RepositoryCredential? credential,
        IDictionary<string, bool> versions,
        int depth,
        CancellationToken cancellationToken)
    {
        if (depth > 32)
            throw CreateRepositoryContractException(repository, "RegistrationMetadata", $"Managed module registration query for '{uri}' exceeded the page limit.");

        using var response = await SendWithPolicyAsync(
            () => CreateRequest(HttpMethod.Get, uri, credential, "application/json"),
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return;
        if (!response.IsSuccessStatusCode)
            throw CreateRepositoryHttpException(repository, "RegistrationMetadata", response.StatusCode, $"Unable to query registration metadata '{uri}'.");

        using var document = await ReadJsonDocumentAsync(
            response.Content,
            repository,
            "RegistrationMetadata",
            $"Managed module registration metadata for '{uri}' returned malformed JSON.",
            cancellationToken).ConfigureAwait(false);
        await ReadRegistrationListingItemsAsync(repository, document.RootElement, credential, versions, depth, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReadRegistrationListingItemsAsync(
        ManagedModuleRepository repository,
        JsonElement element,
        RepositoryCredential? credential,
        IDictionary<string, bool> versions,
        int depth,
        CancellationToken cancellationToken)
    {
        if (!element.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in items.EnumerateArray())
        {
            if (TryReadRegistrationCatalogEntry(item, versions))
                continue;

            if (item.TryGetProperty("items", out var nestedItems) && nestedItems.ValueKind == JsonValueKind.Array)
            {
                await ReadRegistrationListingItemsAsync(repository, item, credential, versions, depth + 1, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var page = ReadOptionalString(item, "@id");
            if (Uri.TryCreate(page, UriKind.Absolute, out var pageUri))
                await ReadRegistrationListingPageAsync(repository, pageUri, credential, versions, depth + 1, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool TryReadRegistrationCatalogEntry(JsonElement item, IDictionary<string, bool> versions)
    {
        if (!item.TryGetProperty("catalogEntry", out var catalogEntry) || catalogEntry.ValueKind != JsonValueKind.Object)
            return false;

        var version = ReadOptionalString(catalogEntry, "version");
        if (string.IsNullOrWhiteSpace(version))
            return false;

        versions[version!] = ReadListedFlag(catalogEntry);
        return true;
    }

    private static bool ReadListedFlag(JsonElement item)
    {
        if (!item.TryGetProperty("listed", out var listed))
            return true;

        return listed.ValueKind switch
        {
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(listed.GetString(), out var value) => value,
            _ => true
        };
    }

    private async Task<ManagedModuleDownloadResult> DownloadNuGetPackageAsync(
        ManagedModuleRepository repository,
        string packageId,
        string version,
        string destinationDirectory,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        var packageBase = await ResolvePackageBaseAddressAsync(repository, credential, cancellationToken).ConfigureAwait(false);
        var packageUri = BuildPackageUri(packageBase, packageId, version);
        var destinationPath = BuildDestinationPath(destinationDirectory, repository, packageId, version);
        using var response = await SendWithPolicyAsync(
            () => CreateRequest(HttpMethod.Get, packageUri, credential, "application/octet-stream"),
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateRepositoryHttpException(repository, "Download", response.StatusCode, $"Unable to download package '{packageId}' version '{version}'.");

        PackageCopyResult packageCopy;
        using (var source = await ReadContentStreamAsync(response.Content, cancellationToken).ConfigureAwait(false))
        {
            packageCopy = await CopyPackageStreamWithHashAsync(source, destinationPath, _options.MaxPackageBytes, cancellationToken).ConfigureAwait(false);
        }

        return new ManagedModuleDownloadResult
        {
            Name = packageId,
            Version = version,
            RepositoryName = repository.Name,
            Source = packageUri.ToString(),
            PackagePath = destinationPath,
            BytesWritten = packageCopy.BytesWritten,
            PackageSha256 = packageCopy.Sha256,
            Metadata = ReadDownloadedPackageMetadata(packageId, version, destinationPath)
        };
    }

    private async Task<ManagedModuleDownloadResult> CopyLocalPackageAsync(
        ManagedModuleRepository repository,
        string packageId,
        string version,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        var match = GetLocalVersions(repository, packageId, includePrerelease: true)
            .FirstOrDefault(item => item.Version.Equals(version, StringComparison.OrdinalIgnoreCase));
        if (match?.PackageSource is null)
            throw CreateLocalRepositoryException(repository, "Download", $"Package '{packageId}' version '{version}' was not found in local repository '{repository.Name}'.");

        var destinationPath = BuildDestinationPath(destinationDirectory, repository, packageId, version);
        PackageCopyResult packageCopy;
        using (var source = CreatePackageFileStream(match.PackageSource, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            packageCopy = await CopyPackageStreamWithHashAsync(source, destinationPath, _options.MaxPackageBytes, cancellationToken).ConfigureAwait(false);
        }

        return new ManagedModuleDownloadResult
        {
            Name = packageId,
            Version = version,
            RepositoryName = repository.Name,
            Source = match.PackageSource,
            PackagePath = destinationPath,
            BytesWritten = packageCopy.BytesWritten,
            PackageSha256 = packageCopy.Sha256,
            Metadata = ReadDownloadedPackageMetadata(packageId, version, destinationPath)
        };
    }

    private ManagedModulePackageMetadata ReadDownloadedPackageMetadata(string packageId, string version, string packagePath)
    {
        var metadata = _packageReader.ReadMetadata(packagePath);
        ValidateDownloadedPackageMetadata(packageId, version, packagePath, metadata);
        return metadata;
    }

#if !NET472
    private ManagedModulePackageMetadata ReadDownloadedPackageMetadata(string packageId, string version, Stream packageStream, string packageIdentity)
    {
        var metadata = _packageReader.ReadMetadata(packageStream, packageIdentity);
        ValidateDownloadedPackageMetadata(packageId, version, packageIdentity, metadata);
        return metadata;
    }
#endif

    private static void ValidateDownloadedPackageMetadata(
        string packageId,
        string version,
        string packagePath,
        ManagedModulePackageMetadata metadata)
    {
        if (!metadata.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase) ||
            ManagedModuleVersionComparer.Instance.Compare(metadata.Version, version) != 0)
        {
            throw new InvalidOperationException(
                $"Downloaded package '{packagePath}' contains '{metadata.Id}' version '{metadata.Version}', not requested package '{packageId}' version '{version}'.");
        }
    }

    private async Task<string> ResolvePackageBaseAddressAsync(
        ManagedModuleRepository repository,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        var cacheKey = NormalizeRepositorySourceCacheKey(repository.Source);
        if (_packageBaseAddressCache.TryGetValue(cacheKey, out var cached))
            return cached;

        if (!TryNormalizeServiceIndexSource(repository.Source, out var serviceIndexSource))
        {
            var flat = EnsureTrailingSlash(repository.Source);
            _packageBaseAddressCache[cacheKey] = flat;
            return flat;
        }

        using var response = await SendWithPolicyAsync(
            () => CreateRequest(HttpMethod.Get, new Uri(serviceIndexSource), credential, "application/json"),
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateRepositoryHttpException(repository, "ServiceIndex", response.StatusCode, $"Unable to query NuGet service index '{repository.Source}'.");

        using var document = await ReadJsonDocumentAsync(
            response.Content,
            repository,
            "ServiceIndex",
            "NuGet service index query returned malformed JSON.",
            cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("resources", out var resources) || resources.ValueKind != JsonValueKind.Array)
            throw CreateRepositoryContractException(repository, "ServiceIndex", "NuGet service index did not include a resources array.");

        string? packageBase = null;
        var registrationBaseDiscovered = false;
        foreach (var resource in resources.EnumerateArray())
        {
            var id = resource.TryGetProperty("@id", out var idElement) ? idElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
                continue;

            if (IsPackageBaseAddressResource(resource))
                packageBase = EnsureTrailingSlash(id!);
            else if (IsSearchQueryServiceResource(resource))
            {
                var resolved = NormalizeServiceResource(id!);
                _searchQueryServiceCache[cacheKey] = resolved;
            }
            else if (IsRegistrationBaseResource(resource))
            {
                var resolved = EnsureTrailingSlash(id!);
                _registrationBaseAddressCache[cacheKey] = resolved;
                registrationBaseDiscovered = true;
            }
        }

        if (!registrationBaseDiscovered)
            _registrationBaseAddressCache.TryAdd(cacheKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(packageBase))
        {
            _packageBaseAddressCache[cacheKey] = packageBase!;
            return packageBase!;
        }

        throw CreateRepositoryContractException(repository, "ServiceIndex", "NuGet service index did not expose PackageBaseAddress.");
    }

    private static FileStream CreatePackageFileStream(string path, FileMode mode, FileAccess access, FileShare share)
    {
        if (access != FileAccess.Read)
        {
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);
        }

        return new FileStream(
            path,
            mode,
            access,
            share,
            PackageCopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    private async Task<string> ResolveSearchQueryServiceAsync(
        ManagedModuleRepository repository,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        var cacheKey = NormalizeRepositorySourceCacheKey(repository.Source);
        if (_searchQueryServiceCache.TryGetValue(cacheKey, out var cached))
            return cached;

        if (!TryNormalizeServiceIndexSource(repository.Source, out var serviceIndexSource))
            throw CreateRepositoryContractException(
                repository,
                "SearchServiceDiscovery",
                "Wildcard package search requires a NuGet v3 service index that exposes SearchQueryService; flat-container and package endpoints do not support search.");

        using var response = await SendWithPolicyAsync(
            () => CreateRequest(HttpMethod.Get, new Uri(serviceIndexSource), credential, "application/json"),
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateRepositoryHttpException(repository, "SearchServiceDiscovery", response.StatusCode, $"Unable to query NuGet service index '{repository.Source}'.");

        using var document = await ReadJsonDocumentAsync(
            response.Content,
            repository,
            "SearchServiceDiscovery",
            "NuGet search service discovery returned malformed JSON.",
            cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("resources", out var resources) || resources.ValueKind != JsonValueKind.Array)
            throw CreateRepositoryContractException(repository, "SearchServiceDiscovery", "NuGet service index did not include a resources array.");

        string? searchService = null;
        var registrationBaseDiscovered = false;
        foreach (var resource in resources.EnumerateArray())
        {
            var id = resource.TryGetProperty("@id", out var idElement) ? idElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
                continue;

            if (IsSearchQueryServiceResource(resource))
                searchService = NormalizeServiceResource(id!);
            else if (IsPackageBaseAddressResource(resource))
            {
                var resolved = EnsureTrailingSlash(id!);
                _packageBaseAddressCache[cacheKey] = resolved;
            }
            else if (IsRegistrationBaseResource(resource))
            {
                var resolved = EnsureTrailingSlash(id!);
                _registrationBaseAddressCache[cacheKey] = resolved;
                registrationBaseDiscovered = true;
            }
        }

        if (!registrationBaseDiscovered)
            _registrationBaseAddressCache.TryAdd(cacheKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(searchService))
        {
            _searchQueryServiceCache[cacheKey] = searchService!;
            return searchService!;
        }

        throw CreateRepositoryContractException(repository, "SearchServiceDiscovery", "NuGet service index did not expose SearchQueryService.");
    }

    private async Task<string?> TryResolveRegistrationBaseAddressAsync(
        ManagedModuleRepository repository,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        var cacheKey = NormalizeRepositorySourceCacheKey(repository.Source);
        if (_registrationBaseAddressCache.TryGetValue(cacheKey, out var cached))
            return string.IsNullOrWhiteSpace(cached) ? null : cached;

        if (!TryNormalizeServiceIndexSource(repository.Source, out var serviceIndexSource))
            return null;

        using var response = await SendWithPolicyAsync(
            () => CreateRequest(HttpMethod.Get, new Uri(serviceIndexSource), credential, "application/json"),
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateRepositoryHttpException(repository, "RegistrationServiceDiscovery", response.StatusCode, $"Unable to query NuGet service index '{repository.Source}'.");

        using var document = await ReadJsonDocumentAsync(
            response.Content,
            repository,
            "RegistrationServiceDiscovery",
            "NuGet registration service discovery returned malformed JSON.",
            cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("resources", out var resources) || resources.ValueKind != JsonValueKind.Array)
            throw CreateRepositoryContractException(repository, "RegistrationServiceDiscovery", "NuGet service index did not include a resources array.");

        string? registrationBase = null;
        foreach (var resource in resources.EnumerateArray())
        {
            var id = resource.TryGetProperty("@id", out var idElement) ? idElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
                continue;

            if (IsRegistrationBaseResource(resource))
                registrationBase = EnsureTrailingSlash(id!);
            else if (IsPackageBaseAddressResource(resource))
            {
                var resolved = EnsureTrailingSlash(id!);
                _packageBaseAddressCache[cacheKey] = resolved;
            }
            else if (IsSearchQueryServiceResource(resource))
            {
                var resolved = NormalizeServiceResource(id!);
                _searchQueryServiceCache[cacheKey] = resolved;
            }
        }

        _registrationBaseAddressCache[cacheKey] = registrationBase ?? string.Empty;
        return registrationBase;
    }

    internal static string NormalizeRepositorySourceCacheKey(string source)
    {
        var trimmed = source.Trim().Trim('"');
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            var builder = new UriBuilder(uri)
            {
                Scheme = uri.Scheme.ToLowerInvariant(),
                Host = uri.Host.ToLowerInvariant()
            };
            return builder.Uri.AbsoluteUri;
        }

        return trimmed;
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, RepositoryCredential? credential, string accept)
    {
        var request = new HttpRequestMessage(method, uri);
#if !NET472
        request.Version = HttpVersion.Version20;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
#endif
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        request.Headers.UserAgent.ParseAdd("PowerForge-ManagedModule/1.0");
        ApplyCredential(request, credential);
        return request;
    }

    private static void ApplyCredential(HttpRequestMessage request, RepositoryCredential? credential)
    {
        if (credential is null)
            return;

        if (!string.IsNullOrWhiteSpace(credential.UserName) && !string.IsNullOrWhiteSpace(credential.Secret))
        {
            var raw = Encoding.ASCII.GetBytes($"{credential.UserName}:{credential.Secret}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
            return;
        }

        if (!string.IsNullOrWhiteSpace(credential.Secret))
            request.Headers.Add("X-NuGet-ApiKey", credential.Secret);
    }

    private static bool IsPackageBaseAddressResource(JsonElement resource)
    {
        if (!resource.TryGetProperty("@type", out var typeElement))
            return false;

        if (typeElement.ValueKind == JsonValueKind.String)
            return IsPackageBaseAddressType(typeElement.GetString());

        return typeElement.ValueKind == JsonValueKind.Array &&
               typeElement.EnumerateArray().Any(type => type.ValueKind == JsonValueKind.String && IsPackageBaseAddressType(type.GetString()));
    }

    private static bool IsSearchQueryServiceResource(JsonElement resource)
    {
        if (!resource.TryGetProperty("@type", out var typeElement))
            return false;

        if (typeElement.ValueKind == JsonValueKind.String)
            return IsSearchQueryServiceType(typeElement.GetString());

        return typeElement.ValueKind == JsonValueKind.Array &&
               typeElement.EnumerateArray().Any(type => type.ValueKind == JsonValueKind.String && IsSearchQueryServiceType(type.GetString()));
    }

    private static bool IsPackageBaseAddressType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return false;

        return type!.IndexOf("PackageBaseAddress", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsSearchQueryServiceType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return false;

        return type!.IndexOf("SearchQueryService", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsRegistrationBaseResource(JsonElement resource)
    {
        if (!resource.TryGetProperty("@type", out var typeElement))
            return false;

        if (typeElement.ValueKind == JsonValueKind.String)
            return IsRegistrationBaseType(typeElement.GetString());

        return typeElement.ValueKind == JsonValueKind.Array &&
               typeElement.EnumerateArray().Any(type => type.ValueKind == JsonValueKind.String && IsRegistrationBaseType(type.GetString()));
    }

    private static bool IsRegistrationBaseType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return false;

        return type!.IndexOf("RegistrationsBaseUrl", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private ManagedModuleVersionInfo? ReadSearchResult(ManagedModuleRepository repository, JsonElement item)
    {
        var id = item.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var version = ReadSearchVersion(item);
        if (string.IsNullOrWhiteSpace(version))
            return null;

        return new ManagedModuleVersionInfo
        {
            Name = id!.Trim(),
            Version = version!,
            RepositoryName = repository.Name,
            RepositorySource = repository.Source,
            IsPrerelease = ManagedModuleVersionComparer.IsPrerelease(version!),
            Listed = !item.TryGetProperty("listed", out var listedElement) || listedElement.ValueKind != JsonValueKind.False,
            License = ReadOptionalString(item, "licenseExpression") ??
                      ReadOptionalString(item, "licenseUrl"),
            RequireLicenseAcceptance = ReadOptionalBoolean(item, "requireLicenseAcceptance"),
            Dependencies = ReadSearchDependencies(item)
        };
    }

    private static IReadOnlyList<ManagedModuleDependencyInfo> ReadSearchDependencies(JsonElement item)
    {
        if (!item.TryGetProperty("dependencyGroups", out var groups) || groups.ValueKind != JsonValueKind.Array)
            return Array.Empty<ManagedModuleDependencyInfo>();

        var dependencies = new List<ManagedModuleDependencyInfo>();
        foreach (var group in groups.EnumerateArray())
        {
            var targetFramework = ReadOptionalString(group, "targetFramework");
            if (!group.TryGetProperty("dependencies", out var dependencyItems) ||
                dependencyItems.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var dependency in dependencyItems.EnumerateArray())
            {
                var id = ReadOptionalString(dependency, "id");
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                dependencies.Add(new ManagedModuleDependencyInfo
                {
                    Id = id!,
                    VersionRange = ReadOptionalString(dependency, "range"),
                    TargetFramework = targetFramework
                });
            }
        }

        return dependencies.Count == 0 ? Array.Empty<ManagedModuleDependencyInfo>() : dependencies;
    }

    private static string? ReadSearchVersion(JsonElement item)
    {
        if (item.TryGetProperty("version", out var versionElement) && versionElement.ValueKind == JsonValueKind.String)
            return versionElement.GetString()?.Trim();

        if (!item.TryGetProperty("versions", out var versionsElement) || versionsElement.ValueKind != JsonValueKind.Array)
            return null;

        return versionsElement.EnumerateArray()
            .Select(static version => version.TryGetProperty("version", out var value) ? value.GetString() : null)
            .Where(static version => !string.IsNullOrWhiteSpace(version))
            .Select(static version => version!.Trim())
            .OrderBy(static version => version, ManagedModuleVersionComparer.Instance)
            .LastOrDefault();
    }

    private IEnumerable<ManagedModuleVersionInfo> ReadLocalPackageVersions(
        ManagedModuleRepository repository,
        string root,
        bool includePrerelease)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*.nupkg", SearchOption.AllDirectories))
        {
            ManagedModulePackageMetadata metadata;
            try
            {
                metadata = _packageReader.ReadMetadata(file);
            }
            catch (Exception ex)
            {
                _logger.Verbose($"Managed module local feed skipped '{file}': {ex.Message}");
                continue;
            }

            if (!includePrerelease && metadata.IsPrerelease)
                continue;

            yield return new ManagedModuleVersionInfo
            {
                Name = metadata.Id,
                Version = metadata.Version,
                RepositoryName = repository.Name,
                RepositorySource = repository.Source,
                PackageSource = file,
                IsPrerelease = metadata.IsPrerelease,
                License = metadata.License,
                RequireLicenseAcceptance = metadata.RequireLicenseAcceptance
            };
        }
    }

    private static string? ReadOptionalString(JsonElement item, string propertyName)
        => item.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()?.Trim()
            : null;

    private static bool ReadOptionalBoolean(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var element))
            return false;

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => bool.TryParse(element.GetString(), out var value) && value,
            _ => false
        };
    }

    private static Uri BuildPackageUri(string packageBase, string packageId, string version)
    {
        var lowerId = packageId.Trim().ToLowerInvariant();
        var lowerVersion = version.Trim().ToLowerInvariant();
        return new Uri(
            new Uri(EnsureTrailingSlash(packageBase)),
            $"{Uri.EscapeDataString(lowerId)}/{Uri.EscapeDataString(lowerVersion)}/{Uri.EscapeDataString(lowerId)}.{Uri.EscapeDataString(lowerVersion)}.nupkg");
    }

    private static string BuildDestinationPath(
        string destinationDirectory,
        ManagedModuleRepository repository,
        string packageId,
        string version)
    {
        var safePackageId = ManagedModulePackageIdentity.RequireSafeId(packageId, nameof(packageId));
        var safeVersion = ManagedModulePackageIdentity.RequireSafeVersion(version, nameof(version));
        return Path.Combine(
            Path.GetFullPath(destinationDirectory),
            GetRepositoryCacheKey(repository),
            $"{safePackageId.ToLowerInvariant()}.{safeVersion.ToLowerInvariant()}.nupkg");
    }

    private static IEnumerable<string> EnumerateCachedPackageCandidates(
        string destinationDirectory,
        ManagedModuleRepository repository,
        string packageId,
        string version)
    {
        yield return BuildDestinationPath(destinationDirectory, repository, packageId, version);
    }

    private static string GetRepositoryCacheKey(ManagedModuleRepository repository)
    {
        using var sha256 = SHA256.Create();
        var source = Encoding.UTF8.GetBytes(repository.Source.Trim());
        var hash = sha256.ComputeHash(source);
        return string.Concat(hash.Take(8).Select(static value => value.ToString("x2")));
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(stream);
        return string.Concat(hash.Select(static value => value.ToString("x2")));
    }

    private static string ResolveLocalFolder(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsFile)
            return uri.LocalPath;

        return Path.GetFullPath(source.Trim().Trim('"'));
    }

    private static string EnsureTrailingSlash(string value)
        => value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";

    private static string NormalizeServiceResource(string value)
        => value.Trim();

    private static bool TryNormalizeServiceIndexSource(string source, out string serviceIndexSource)
    {
        var trimmed = source.Trim();
        var normalized = trimmed.TrimEnd('/');
        if (normalized.EndsWith("index.json", StringComparison.OrdinalIgnoreCase))
        {
            serviceIndexSource = normalized;
            return true;
        }

        serviceIndexSource = trimmed;
        return false;
    }

    private static Uri BuildSearchQueryUri(string searchService, string query)
    {
        var trimmed = searchService.Trim();
        var separator = trimmed.IndexOf("?", StringComparison.Ordinal) >= 0 ? "&" : "?";
        return new Uri(trimmed + separator + query);
    }

    private static ManagedModuleRepository CreatePowerShellGalleryV2Fallback(ManagedModuleRepository repository)
        => new(repository.Name, "https://www.powershellgallery.com/api/v2", ManagedModuleRepositoryKind.NuGetV2, repository.Trusted);

    private static bool ShouldUsePowerShellGalleryV2ReadApi(ManagedModuleRepository repository)
        => IsPowerShellGalleryV3Index(repository.Source);

    private static bool IsPowerShellGalleryV3Index(string source)
    {
        var normalized = source.Trim().TrimEnd('/');
        return normalized.Equals("https://www.powershellgallery.com/api/v3/index.json", StringComparison.OrdinalIgnoreCase);
    }

    private static Task<Stream> ReadContentStreamAsync(HttpContent content, CancellationToken cancellationToken)
#if NET472
        => content.ReadAsStreamAsync();
#else
        => content.ReadAsStreamAsync(cancellationToken);
#endif

    private static async Task<JsonDocument> ReadJsonDocumentAsync(
        HttpContent content,
        ManagedModuleRepository repository,
        string operation,
        string detail,
        CancellationToken cancellationToken)
    {
        try
        {
            using var stream = await ReadContentStreamAsync(content, cancellationToken).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw CreateRepositoryJsonException(repository, operation, detail, ex);
        }
    }
}
