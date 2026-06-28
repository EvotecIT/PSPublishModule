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
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private readonly ManagedModulePackageReader _packageReader;
    private readonly ManagedModuleRepositoryClientOptions _options;
    private readonly Dictionary<string, string> _packageBaseAddressCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _searchQueryServiceCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _packagePublishAddressCache = new(StringComparer.OrdinalIgnoreCase);
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
        _httpClient = httpClient ?? new HttpClient(CreateDefaultHttpMessageHandler(_options));
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
            ManagedModuleRepositoryKind.NuGetV3 => await GetNuGetVersionsWithPowerShellGalleryReadApiAsync(repository, packageId, includePrerelease, credential, cancellationToken).ConfigureAwait(false),
            ManagedModuleRepositoryKind.NuGetV2 => await GetNuGetV2VersionsAsync(repository, packageId, includePrerelease, credential, cancellationToken).ConfigureAwait(false),
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
            ManagedModuleRepositoryKind.NuGetV3 => await SearchNuGetPackagesWithPowerShellGalleryReadApiAsync(repository, query, includePrerelease, credential, take, cancellationToken).ConfigureAwait(false),
            ManagedModuleRepositoryKind.NuGetV2 => await SearchNuGetV2PackagesAsync(repository, query, includePrerelease, credential, take, cancellationToken).ConfigureAwait(false),
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

        Directory.CreateDirectory(destinationDirectory);
        var cached = TryUseCachedPackage(repository, packageId, version, destinationDirectory);
        if (cached is not null)
            return cached;

        return repository.Kind switch
        {
            ManagedModuleRepositoryKind.LocalFolder => await CopyLocalPackageAsync(repository, packageId, version, destinationDirectory, cancellationToken).ConfigureAwait(false),
            ManagedModuleRepositoryKind.NuGetV3 => await DownloadNuGetPackageWithPowerShellGalleryReadApiAsync(repository, packageId, version, destinationDirectory, credential, cancellationToken).ConfigureAwait(false),
            ManagedModuleRepositoryKind.NuGetV2 => await DownloadNuGetV2PackageAsync(repository, packageId, version, destinationDirectory, credential, cancellationToken).ConfigureAwait(false),
            _ => throw new NotSupportedException($"Repository kind '{repository.Kind}' is not supported.")
        };
    }

    /// <summary>
    /// Reads package metadata from a NuGet package.
    /// </summary>
    /// <param name="packagePath">Path to the package.</param>
    /// <returns>Package metadata.</returns>
    public ManagedModulePackageMetadata ReadPackageMetadata(string packagePath)
        => _packageReader.ReadMetadata(packagePath);

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

    private async Task<ManagedModuleDownloadResult> DownloadNuGetPackageWithPowerShellGalleryReadApiAsync(
        ManagedModuleRepository repository,
        string packageId,
        string version,
        string destinationDirectory,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        if (ShouldUsePowerShellGalleryV2ReadApi(repository))
        {
            var fallback = CreatePowerShellGalleryV2Fallback(repository);
            return await DownloadNuGetV2PackageAsync(fallback, packageId, version, destinationDirectory, credential, cancellationToken).ConfigureAwait(false);
        }

        return await DownloadNuGetPackageAsync(repository, packageId, version, destinationDirectory, credential, cancellationToken).ConfigureAwait(false);
    }

    private ManagedModuleDownloadResult? TryUseCachedPackage(
        ManagedModuleRepository repository,
        string packageId,
        string version,
        string destinationDirectory)
    {
        var destinationPath = BuildDestinationPath(destinationDirectory, packageId, version);
        if (!File.Exists(destinationPath))
            return null;

        try
        {
            var metadata = _packageReader.ReadMetadata(destinationPath);
            if (!metadata.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase) ||
                ManagedModuleVersionComparer.Instance.Compare(metadata.Version, version) != 0)
            {
                return null;
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
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
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

        return versions.EnumerateArray()
            .Select(static version => version.GetString())
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .Select(version => version!.Trim())
            .Where(version => includePrerelease || !ManagedModuleVersionComparer.IsPrerelease(version))
            .OrderBy(version => version, ManagedModuleVersionComparer.Instance)
            .Select(version => new ManagedModuleVersionInfo
            {
                Name = packageId,
                Version = version,
                RepositoryName = repository.Name,
                RepositorySource = repository.Source,
                PackageSource = BuildPackageUri(packageBase, packageId, version).ToString(),
                IsPrerelease = ManagedModuleVersionComparer.IsPrerelease(version)
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
                IsPrerelease = metadata.IsPrerelease
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
        var uri = new Uri(
            new Uri(EnsureTrailingSlash(searchService)),
            $"?q={Uri.EscapeDataString(searchText)}&prerelease={includePrerelease.ToString().ToLowerInvariant()}&take={Math.Max(1, take)}");
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
        var destinationPath = BuildDestinationPath(destinationDirectory, packageId, version);
        using var response = await SendWithPolicyAsync(
            () => CreateRequest(HttpMethod.Get, packageUri, credential, "application/octet-stream"),
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateRepositoryHttpException(repository, "Download", response.StatusCode, $"Unable to download package '{packageId}' version '{version}'.");

        long bytesWritten;
        using (var source = await ReadContentStreamAsync(response.Content, cancellationToken).ConfigureAwait(false))
        using (var destination = File.Create(destinationPath))
        {
            await source.CopyToAsync(destination, 81920, cancellationToken).ConfigureAwait(false);
            bytesWritten = destination.Length;
        }

        return new ManagedModuleDownloadResult
        {
            Name = packageId,
            Version = version,
            RepositoryName = repository.Name,
            Source = packageUri.ToString(),
            PackagePath = destinationPath,
            BytesWritten = bytesWritten,
            PackageSha256 = ComputeSha256(destinationPath),
            Metadata = _packageReader.ReadMetadata(destinationPath)
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

        var destinationPath = BuildDestinationPath(destinationDirectory, packageId, version);
        using (var source = File.OpenRead(match.PackageSource))
        using (var destination = File.Create(destinationPath))
        {
            await source.CopyToAsync(destination, 81920, cancellationToken).ConfigureAwait(false);
        }

        return new ManagedModuleDownloadResult
        {
            Name = packageId,
            Version = version,
            RepositoryName = repository.Name,
            Source = match.PackageSource,
            PackagePath = destinationPath,
            BytesWritten = new FileInfo(destinationPath).Length,
            PackageSha256 = ComputeSha256(destinationPath),
            Metadata = _packageReader.ReadMetadata(destinationPath)
        };
    }

    private async Task<string> ResolvePackageBaseAddressAsync(
        ManagedModuleRepository repository,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        if (_packageBaseAddressCache.TryGetValue(repository.Source, out var cached))
            return cached;

        if (!repository.Source.EndsWith("index.json", StringComparison.OrdinalIgnoreCase))
        {
            var flat = EnsureTrailingSlash(repository.Source);
            _packageBaseAddressCache[repository.Source] = flat;
            return flat;
        }

        using var response = await SendWithPolicyAsync(
            () => CreateRequest(HttpMethod.Get, new Uri(repository.Source), credential, "application/json"),
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

        foreach (var resource in resources.EnumerateArray())
        {
            if (!IsPackageBaseAddressResource(resource))
                continue;

            var id = resource.TryGetProperty("@id", out var idElement) ? idElement.GetString() : null;
            if (!string.IsNullOrWhiteSpace(id))
            {
                var resolved = EnsureTrailingSlash(id!);
                _packageBaseAddressCache[repository.Source] = resolved;
                return resolved;
            }
        }

        throw CreateRepositoryContractException(repository, "ServiceIndex", "NuGet service index did not expose PackageBaseAddress.");
    }

    private async Task<string> ResolveSearchQueryServiceAsync(
        ManagedModuleRepository repository,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        if (_searchQueryServiceCache.TryGetValue(repository.Source, out var cached))
            return cached;

        if (!repository.Source.EndsWith("index.json", StringComparison.OrdinalIgnoreCase))
        {
            var flat = EnsureTrailingSlash(repository.Source);
            _searchQueryServiceCache[repository.Source] = flat;
            return flat;
        }

        using var response = await SendWithPolicyAsync(
            () => CreateRequest(HttpMethod.Get, new Uri(repository.Source), credential, "application/json"),
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

        foreach (var resource in resources.EnumerateArray())
        {
            if (!IsSearchQueryServiceResource(resource))
                continue;

            var id = resource.TryGetProperty("@id", out var idElement) ? idElement.GetString() : null;
            if (!string.IsNullOrWhiteSpace(id))
            {
                var resolved = EnsureTrailingSlash(id!);
                _searchQueryServiceCache[repository.Source] = resolved;
                return resolved;
            }
        }

        throw CreateRepositoryContractException(repository, "SearchServiceDiscovery", "NuGet service index did not expose SearchQueryService.");
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, RepositoryCredential? credential, string accept)
    {
        var request = new HttpRequestMessage(method, uri);
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
            Listed = !item.TryGetProperty("listed", out var listedElement) || listedElement.ValueKind != JsonValueKind.False
        };
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
                IsPrerelease = metadata.IsPrerelease
            };
        }
    }

    private static Uri BuildPackageUri(string packageBase, string packageId, string version)
    {
        var lowerId = packageId.Trim().ToLowerInvariant();
        var lowerVersion = version.Trim().ToLowerInvariant();
        return new Uri(
            new Uri(EnsureTrailingSlash(packageBase)),
            $"{Uri.EscapeDataString(lowerId)}/{Uri.EscapeDataString(lowerVersion)}/{Uri.EscapeDataString(lowerId)}.{Uri.EscapeDataString(lowerVersion)}.nupkg");
    }

    private static string BuildDestinationPath(string destinationDirectory, string packageId, string version)
        => Path.Combine(Path.GetFullPath(destinationDirectory), $"{packageId.Trim().ToLowerInvariant()}.{version.Trim().ToLowerInvariant()}.nupkg");

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
