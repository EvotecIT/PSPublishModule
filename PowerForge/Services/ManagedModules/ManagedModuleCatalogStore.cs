using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Collections.Concurrent;

namespace PowerForge;

/// <summary>
/// Stores and refreshes local managed module catalog metadata.
/// </summary>
public sealed class ManagedModuleCatalogStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CatalogLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly HttpClient _httpClient;
    private readonly string _path;

    /// <summary>
    /// Creates a catalog store at the default user path.
    /// </summary>
    public ManagedModuleCatalogStore()
        : this(null, null)
    {
    }

    /// <summary>
    /// Creates a catalog store at an explicit path.
    /// </summary>
    /// <param name="path">Catalog document path.</param>
    public ManagedModuleCatalogStore(string? path)
        : this(path, null)
    {
    }

    /// <summary>
    /// Creates a catalog store with an optional test HTTP client.
    /// </summary>
    /// <param name="path">Catalog document path.</param>
    /// <param name="httpClient">Optional HTTP client used for metadata refresh.</param>
    public ManagedModuleCatalogStore(string? path, HttpClient? httpClient)
    {
        _path = string.IsNullOrWhiteSpace(path) ? GetDefaultPath(machine: false) : System.IO.Path.GetFullPath(path!);
        _httpClient = httpClient ?? ManagedModuleRepositoryClient.CreateDefaultHttpClient(new ManagedModuleRepositoryClientOptions());
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PowerForge-ManagedModuleCatalog/1.0");
    }

    /// <summary>Catalog document path.</summary>
    public string Path => _path;

    /// <summary>
    /// Returns the default managed module catalog path.
    /// </summary>
    /// <param name="machine">True to return the machine-wide path.</param>
    /// <returns>Default catalog path.</returns>
    public static string GetDefaultPath(bool machine)
    {
        var overrideVariable = machine
            ? "POWERFORGE_MODULE_CATALOG_MACHINE_PATH"
            : "POWERFORGE_MODULE_CATALOG_PATH";
        var overridePath = Environment.GetEnvironmentVariable(overrideVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
            return System.IO.Path.GetFullPath(overridePath!);

        var root = Environment.GetFolderPath(machine
            ? Environment.SpecialFolder.CommonApplicationData
            : Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(root))
                root = Environment.CurrentDirectory;
        }

        return System.IO.Path.Combine(root, "PowerForge", "ManagedModuleCatalog", "catalog.json");
    }

    /// <summary>
    /// Gets configured catalogs.
    /// </summary>
    /// <returns>Catalogs ordered by name.</returns>
    public ManagedModuleCatalog[] GetCatalogs()
        => ReadDocument().Catalogs
            .Where(static catalog => catalog is not null && !string.IsNullOrWhiteSpace(catalog.Name))
            .Select(NormalizeCatalog)
            .OrderBy(static catalog => catalog.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>
    /// Gets one configured catalog by name.
    /// </summary>
    /// <param name="name">Catalog name.</param>
    /// <returns>Catalog when present.</returns>
    public ManagedModuleCatalog? GetCatalog(string name)
    {
        var normalizedName = NormalizeName(name);
        return GetCatalogs().FirstOrDefault(catalog => string.Equals(catalog.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Creates or updates catalog settings.
    /// </summary>
    /// <param name="request">Settings request.</param>
    /// <returns>Saved catalog.</returns>
    public ManagedModuleCatalog SetCatalog(ManagedModuleCatalogSetRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var document = ReadDocument();
        var now = DateTimeOffset.UtcNow;
        var name = NormalizeName(request.Name);
        var existing = document.Catalogs.FirstOrDefault(catalog => string.Equals(catalog.Name, name, StringComparison.OrdinalIgnoreCase));
        var catalog = existing ?? new ManagedModuleCatalog
        {
            Name = name,
            CreatedAtUtc = now
        };

        var source = NormalizeSource(request.Source);
        var repositoryKind = request.RepositoryKind;
        var sourceIdentityChanged = existing is not null &&
                                    (!string.Equals(NormalizeSource(existing.Source), source, StringComparison.OrdinalIgnoreCase) ||
                                     existing.RepositoryKind != repositoryKind);

        catalog.Name = name;
        catalog.Source = source;
        catalog.RepositoryKind = repositoryKind;
        catalog.Mode = request.Mode;
        catalog.MaxStaleness = request.MaxStaleness <= TimeSpan.Zero ? TimeSpan.FromDays(14) : request.MaxStaleness;
        catalog.IncludePrerelease = request.IncludePrerelease;
        catalog.UpdatedAtUtc = now;
        catalog.Packages = sourceIdentityChanged
            ? new List<ManagedModuleCatalogPackage>()
            : catalog.Packages ?? new List<ManagedModuleCatalogPackage>();
        if (sourceIdentityChanged)
        {
            catalog.LastRefreshAtUtc = null;
            catalog.LastWarning = null;
        }

        var catalogs = document.Catalogs
            .Where(item => !string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
            .Append(catalog)
            .Select(NormalizeCatalog)
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        document.Catalogs = catalogs;
        WriteDocument(document);
        return catalog;
    }

    /// <summary>
    /// Refreshes package metadata in a configured catalog.
    /// </summary>
    /// <param name="request">Refresh request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Refresh result.</returns>
    public async Task<ManagedModuleCatalogUpdateResult> UpdateCatalogAsync(
        ManagedModuleCatalogUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var catalogLock = CatalogLocks.GetOrAdd(_path, static _ => new SemaphoreSlim(1, 1));
        await catalogLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await UpdateCatalogCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            catalogLock.Release();
        }
    }

    private async Task<ManagedModuleCatalogUpdateResult> UpdateCatalogCoreAsync(
        ManagedModuleCatalogUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var document = ReadDocument();
        var name = NormalizeName(request.Name);
        var catalog = document.Catalogs.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (catalog is null)
            throw new InvalidOperationException($"Managed module catalog '{name}' was not found. Create it with Set-ManagedModuleCatalog first.");

        catalog = NormalizeCatalog(catalog);
        var packageNames = request.PackageNames
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (packageNames.Length == 0)
        {
            packageNames = catalog.Packages
                .Select(static package => package.Id)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (packageNames.Length == 0)
            throw new InvalidOperationException("Update-ManagedModuleCatalog needs at least one package name for the first refresh.");

        var warnings = new List<string>();
        var refreshed = 0;
        foreach (var packageName in packageNames)
        {
            var package = await TryReadPackageAsync(
                    catalog,
                    packageName,
                    request.IncludePrerelease ?? catalog.IncludePrerelease,
                    request.Credential,
                    warnings,
                    cancellationToken)
                .ConfigureAwait(false);
            if (package is null)
                continue;

            UpsertPackage(catalog, package);
            refreshed++;
        }

        var now = DateTimeOffset.UtcNow;
        catalog.LastRefreshAtUtc = now;
        catalog.LastWarning = warnings.Count == 0 ? null : string.Join(Environment.NewLine, warnings);
        catalog.UpdatedAtUtc = now;
        catalog.Packages = catalog.Packages
            .OrderBy(static package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        document.Catalogs = document.Catalogs
            .Where(item => !string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
            .Append(catalog)
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        WriteDocument(document);

        return new ManagedModuleCatalogUpdateResult
        {
            Name = catalog.Name,
            Source = catalog.Source,
            CatalogPath = _path,
            RefreshedPackageCount = refreshed,
            PackageCount = catalog.Packages.Count,
            VersionCount = catalog.Packages.Sum(static package => package.Versions.Count),
            RefreshedAtUtc = now,
            Warnings = warnings
        };
    }

    private async Task<ManagedModuleCatalogPackage?> TryReadPackageAsync(
        ManagedModuleCatalog catalog,
        string packageName,
        bool includePrerelease,
        RepositoryCredential? credential,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var package = catalog.RepositoryKind == ManagedModuleRepositoryKind.NuGetV3 && !IsPowerShellGalleryV3(catalog.Source)
                ? await ReadNuGetV3PackageAsync(catalog.Source, packageName, includePrerelease, credential, cancellationToken).ConfigureAwait(false)
                : await ReadNuGetV2PackageAsync(catalog, packageName, includePrerelease, credential, cancellationToken).ConfigureAwait(false);
            if (package is null)
                warnings.Add($"Package '{packageName}' was not found in catalog source '{catalog.Source}'.");
            return package;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or System.Xml.XmlException or JsonException)
        {
            warnings.Add($"Package '{packageName}' refresh failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private async Task<ManagedModuleCatalogPackage?> ReadNuGetV2PackageAsync(
        ManagedModuleCatalog catalog,
        string packageName,
        bool includePrerelease,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        var source = ResolveNuGetV2MetadataSource(catalog);
        var documents = await ReadNuGetV2XmlPagesAsync(source, packageName, credential, cancellationToken).ConfigureAwait(false);
        return ReadPackage(source, packageName, documents, includePrerelease);
    }

    private async Task<ManagedModuleCatalogPackage?> ReadNuGetV3PackageAsync(
        string source,
        string packageName,
        bool includePrerelease,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        var resources = await ResolveNuGetV3ResourcesAsync(source, credential, cancellationToken).ConfigureAwait(false);
        var versions = await ReadNuGetV3VersionsAsync(resources.PackageBaseAddress, packageName, cancellationToken).ConfigureAwait(false);
        if (versions.Count == 0)
            return null;

        var registration = string.IsNullOrWhiteSpace(resources.RegistrationBaseAddress)
            ? null
            : await ReadNuGetV3RegistrationAsync(resources.RegistrationBaseAddress!, packageName, credential, cancellationToken).ConfigureAwait(false);
        var registrationVersions = registration?.Versions.ToDictionary(static version => version.Version, StringComparer.OrdinalIgnoreCase) ??
                                   new Dictionary<string, ManagedModuleCatalogVersion>(StringComparer.OrdinalIgnoreCase);
        var catalogVersions = versions
            .Where(version => includePrerelease || !ManagedModuleVersionComparer.IsPrerelease(version))
            .Select(version => registrationVersions.TryGetValue(version, out var registered)
                ? EnsureNuGetV3VersionPackageSource(registered, resources.PackageBaseAddress, packageName)
                : CreateNuGetV3Version(resources.PackageBaseAddress, packageName, version, listed: true))
            .GroupBy(static version => version.Version, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static version => version.Version, ManagedModuleVersionComparer.Instance)
            .ToList();
        if (catalogVersions.Count == 0)
            return null;

        var package = new ManagedModuleCatalogPackage
        {
            Id = registration?.Id ?? packageName.Trim(),
            Authors = registration?.Authors,
            Owners = registration?.Owners,
            Description = registration?.Description,
            ProjectUrl = registration?.ProjectUrl,
            GalleryUrl = registration?.GalleryUrl,
            Tags = registration?.Tags.ToList() ?? new List<string>(),
            Versions = catalogVersions,
            LastRefreshAtUtc = DateTimeOffset.UtcNow,
            SourceReceipt = source.Trim().TrimEnd('/')
        };
        package.LatestStableVersion = catalogVersions
            .Where(static version => !version.IsPrerelease && version.Listed)
            .OrderBy(static version => version.Version, ManagedModuleVersionComparer.Instance)
            .LastOrDefault()
            ?.Version;
        package.LatestPrereleaseVersion = catalogVersions
            .Where(static version => version.IsPrerelease && version.Listed)
            .OrderBy(static version => version.Version, ManagedModuleVersionComparer.Instance)
            .LastOrDefault()
            ?.Version;
        return package;
    }

    private async Task<IReadOnlyList<XDocument>> ReadNuGetV2XmlPagesAsync(
        string source,
        string packageName,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        const int MaxPages = 100;
        XNamespace atom = "http://www.w3.org/2005/Atom";
        var documents = new List<XDocument>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = BuildFindPackagesByIdUri(source, packageName);

        for (var page = 0; page < MaxPages; page++)
        {
            if (!visited.Add(current.AbsoluteUri))
                throw new InvalidOperationException($"Catalog query for '{packageName}' returned a repeated next page link.");

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.Accept.ParseAdd("application/xml");
            ApplyCredential(request, credential);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {current}");

            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var document = XDocument.Load(stream);
            documents.Add(document);

            var next = document
                .Descendants(atom + "link")
                .FirstOrDefault(link => string.Equals((string?)link.Attribute("rel"), "next", StringComparison.OrdinalIgnoreCase))
                ?.Attribute("href")
                ?.Value;
            if (string.IsNullOrWhiteSpace(next))
                return documents;

            current = Uri.TryCreate(next, UriKind.Absolute, out var absolute)
                ? absolute
                : new Uri(current, next);
        }

        throw new InvalidOperationException($"Catalog query for '{packageName}' exceeded the page limit.");
    }

    private static ManagedModuleCatalogPackage? ReadPackage(
        string source,
        string fallbackId,
        IReadOnlyList<XDocument> documents,
        bool includePrerelease)
    {
        XNamespace atom = "http://www.w3.org/2005/Atom";
        XNamespace data = "http://schemas.microsoft.com/ado/2007/08/dataservices";
        var versions = documents
            .SelectMany(document => document.Descendants(atom + "entry"))
            .Select(entry => ReadVersion(source, fallbackId, entry, data))
            .Where(version => version is not null && (includePrerelease || !version!.IsPrerelease))
            .Select(static version => version!)
            .GroupBy(static version => version.Version, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static version => version.Version, ManagedModuleVersionComparer.Instance)
            .ToList();

        if (versions.Count == 0)
            return null;

        var firstEntry = documents
            .SelectMany(document => document.Descendants(atom + "entry"))
            .FirstOrDefault();
        var id = ReadString(firstEntry, data, "Id") ?? fallbackId;
        var package = new ManagedModuleCatalogPackage
        {
            Id = id.Trim(),
            Authors = ReadString(firstEntry, data, "Authors"),
            Owners = ReadString(firstEntry, data, "Owners"),
            Description = ReadString(firstEntry, data, "Description"),
            ProjectUrl = ReadString(firstEntry, data, "ProjectUrl"),
            GalleryUrl = ReadString(firstEntry, data, "GalleryDetailsUrl"),
            Tags = SplitTags(ReadString(firstEntry, data, "Tags")).ToList(),
            Versions = versions,
            LastRefreshAtUtc = DateTimeOffset.UtcNow,
            SourceReceipt = source
        };
        package.LatestStableVersion = versions
            .Where(static version => !version.IsPrerelease && version.Listed)
            .OrderBy(static version => version.Version, ManagedModuleVersionComparer.Instance)
            .LastOrDefault()
            ?.Version;
        package.LatestPrereleaseVersion = versions
            .Where(static version => version.IsPrerelease && version.Listed)
            .OrderBy(static version => version.Version, ManagedModuleVersionComparer.Instance)
            .LastOrDefault()
            ?.Version;
        return package;
    }

    private static ManagedModuleCatalogVersion? ReadVersion(
        string source,
        string fallbackId,
        XElement entry,
        XNamespace data)
    {
        var id = ReadString(entry, data, "Id") ?? fallbackId;
        var versionText = ReadString(entry, data, "NormalizedVersion") ?? ReadString(entry, data, "Version");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(versionText))
            return null;

        var version = versionText!.Trim();
        return new ManagedModuleCatalogVersion
        {
            Version = version,
            NormalizedVersion = ReadString(entry, data, "NormalizedVersion"),
            IsPrerelease = ReadBoolean(entry, data, "IsPrerelease") || ManagedModuleVersionComparer.IsPrerelease(version),
            Listed = ReadListed(entry, data),
            IsLatestVersion = ReadBoolean(entry, data, "IsLatestVersion"),
            IsAbsoluteLatestVersion = ReadBoolean(entry, data, "IsAbsoluteLatestVersion"),
            CreatedAtUtc = ReadDateTimeOffset(entry, data, "Created"),
            PublishedAtUtc = ReadDateTimeOffset(entry, data, "Published"),
            DownloadCount = ReadInt64(entry, data, "DownloadCount"),
            VersionDownloadCount = ReadInt64(entry, data, "VersionDownloadCount"),
            PackageSize = ReadInt64(entry, data, "PackageSize"),
            PackageHash = ReadString(entry, data, "PackageHash"),
            PackageHashAlgorithm = ReadString(entry, data, "PackageHashAlgorithm"),
            License = ReadString(entry, data, "LicenseExpression") ??
                      ReadString(entry, data, "LicenseNames") ??
                      ReadString(entry, data, "LicenseUrl"),
            RequireLicenseAcceptance = ReadBoolean(entry, data, "RequireLicenseAcceptance"),
            PackageSource = entry.Element(XName.Get("content", "http://www.w3.org/2005/Atom"))?.Attribute("src")?.Value ??
                            BuildNuGetV2PackageUri(source, id, version).ToString(),
            CdnPackageSource = IsPowerShellGalleryV2(source) ? BuildPowerShellGalleryCdnPackageUri(id, version).ToString() : null,
            Dependencies = ReadDependencies(entry, data).ToList()
        };
    }

    private static void UpsertPackage(ManagedModuleCatalog catalog, ManagedModuleCatalogPackage package)
    {
        catalog.Packages = catalog.Packages
            .Where(existing => !string.Equals(existing.Id, package.Id, StringComparison.OrdinalIgnoreCase))
            .Append(package)
            .OrderBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ManagedModuleCatalog NormalizeCatalog(ManagedModuleCatalog catalog)
    {
        catalog.Name = NormalizeName(catalog.Name);
        catalog.Source = NormalizeSource(catalog.Source);
        catalog.MaxStaleness = catalog.MaxStaleness <= TimeSpan.Zero ? TimeSpan.FromDays(14) : catalog.MaxStaleness;
        catalog.Packages ??= new List<ManagedModuleCatalogPackage>();
        foreach (var package in catalog.Packages)
        {
            package.Tags ??= new List<string>();
            package.Versions ??= new List<ManagedModuleCatalogVersion>();
            foreach (var version in package.Versions)
                version.Dependencies ??= new List<ManagedModuleDependencyInfo>();
        }

        return catalog;
    }

    private async Task<NuGetV3CatalogResources> ResolveNuGetV3ResourcesAsync(
        string source,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        var normalizedSource = source.Trim().TrimEnd('/');
        if (!TryNormalizeNuGetV3ServiceIndexSource(normalizedSource, out var serviceIndexSource))
        {
            return new NuGetV3CatalogResources
            {
                PackageBaseAddress = EnsureTrailingSlash(normalizedSource)
            };
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, serviceIndexSource);
        request.Headers.Accept.ParseAdd("application/json");
        ApplyCredential(request, credential);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {serviceIndexSource}");

        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("resources", out var resources) || resources.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("NuGet v3 service index did not include a resources array.");

        string? packageBase = null;
        string? registrationBase = null;
        foreach (var resource in resources.EnumerateArray())
        {
            var id = ReadJsonString(resource, "@id");
            if (string.IsNullOrWhiteSpace(id))
                continue;

            if (IsNuGetV3PackageBaseResource(resource))
                packageBase = EnsureTrailingSlash(id!);
            else if (IsNuGetV3RegistrationBaseResource(resource))
                registrationBase = EnsureTrailingSlash(id!);
        }

        if (string.IsNullOrWhiteSpace(packageBase))
            throw new InvalidOperationException("NuGet v3 service index did not expose PackageBaseAddress.");

        return new NuGetV3CatalogResources
        {
            PackageBaseAddress = packageBase!,
            RegistrationBaseAddress = registrationBase
        };
    }

    private async Task<IReadOnlyList<string>> ReadNuGetV3VersionsAsync(
        string packageBaseAddress,
        string packageName,
        CancellationToken cancellationToken)
    {
        var lowerId = packageName.Trim().ToLowerInvariant();
        var indexUri = new Uri(new Uri(EnsureTrailingSlash(packageBaseAddress)), $"{Uri.EscapeDataString(lowerId)}/index.json");
        using var request = new HttpRequestMessage(HttpMethod.Get, indexUri);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return Array.Empty<string>();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {indexUri}");

        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("versions", out var versions) || versions.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"NuGet v3 flat-container response did not include a versions array for '{packageName}'.");

        return versions.EnumerateArray()
            .Select(static version => version.GetString())
            .Where(static version => !string.IsNullOrWhiteSpace(version))
            .Select(static version => version!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<NuGetV3RegistrationPackage?> ReadNuGetV3RegistrationAsync(
        string registrationBaseAddress,
        string packageName,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        var lowerId = packageName.Trim().ToLowerInvariant();
        var indexUri = new Uri(new Uri(EnsureTrailingSlash(registrationBaseAddress)), $"{Uri.EscapeDataString(lowerId)}/index.json");
        var package = new NuGetV3RegistrationPackage { Id = packageName.Trim() };
        await ReadNuGetV3RegistrationPageAsync(indexUri, credential, package, depth: 0, cancellationToken).ConfigureAwait(false);
        return package.Versions.Count == 0 ? null : package;
    }

    private async Task ReadNuGetV3RegistrationPageAsync(
        Uri uri,
        RepositoryCredential? credential,
        NuGetV3RegistrationPackage package,
        int depth,
        CancellationToken cancellationToken)
    {
        if (depth > 32)
            throw new InvalidOperationException($"NuGet v3 registration query for '{uri}' exceeded the page limit.");

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("application/json");
        ApplyCredential(request, credential);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return;
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {uri}");

        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        await ReadNuGetV3RegistrationItemsAsync(document.RootElement, credential, package, depth, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReadNuGetV3RegistrationItemsAsync(
        JsonElement element,
        RepositoryCredential? credential,
        NuGetV3RegistrationPackage package,
        int depth,
        CancellationToken cancellationToken)
    {
        if (!element.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("catalogEntry", out var catalogEntry) && catalogEntry.ValueKind == JsonValueKind.Object)
            {
                ReadNuGetV3RegistrationCatalogEntry(catalogEntry, package);
                continue;
            }

            if (item.TryGetProperty("items", out var nestedItems) && nestedItems.ValueKind == JsonValueKind.Array)
            {
                await ReadNuGetV3RegistrationItemsAsync(item, credential, package, depth + 1, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var page = ReadJsonString(item, "@id");
            if (Uri.TryCreate(page, UriKind.Absolute, out var pageUri))
                await ReadNuGetV3RegistrationPageAsync(pageUri, credential, package, depth + 1, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ReadNuGetV3RegistrationCatalogEntry(JsonElement catalogEntry, NuGetV3RegistrationPackage package)
    {
        var id = ReadJsonString(catalogEntry, "id") ?? ReadJsonString(catalogEntry, "Id") ?? package.Id;
        var version = ReadJsonString(catalogEntry, "version") ?? ReadJsonString(catalogEntry, "Version");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version))
            return;

        package.Id = id!.Trim();
        package.Authors ??= ReadJsonStringOrArray(catalogEntry, "authors");
        package.Owners ??= ReadJsonStringOrArray(catalogEntry, "owners");
        package.Description ??= ReadJsonString(catalogEntry, "description");
        package.ProjectUrl ??= ReadJsonString(catalogEntry, "projectUrl");
        package.GalleryUrl ??= ReadJsonString(catalogEntry, "galleryDetailsUrl") ?? ReadJsonString(catalogEntry, "@id");
        foreach (var tag in ReadJsonTags(catalogEntry, "tags"))
        {
            if (!package.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                package.Tags.Add(tag);
        }

        package.Versions.Add(new ManagedModuleCatalogVersion
        {
            Version = version!.Trim(),
            NormalizedVersion = ReadJsonString(catalogEntry, "normalizedVersion"),
            IsPrerelease = ReadJsonBoolean(catalogEntry, "isPrerelease") || ManagedModuleVersionComparer.IsPrerelease(version),
            Listed = ReadJsonListed(catalogEntry),
            CreatedAtUtc = ReadJsonDateTimeOffset(catalogEntry, "created"),
            PublishedAtUtc = ReadJsonDateTimeOffset(catalogEntry, "published"),
            DownloadCount = ReadJsonInt64(catalogEntry, "downloadCount"),
            VersionDownloadCount = ReadJsonInt64(catalogEntry, "versionDownloadCount"),
            PackageSize = ReadJsonInt64(catalogEntry, "packageSize"),
            PackageHash = ReadJsonString(catalogEntry, "packageHash"),
            PackageHashAlgorithm = ReadJsonString(catalogEntry, "packageHashAlgorithm"),
            License = ReadJsonString(catalogEntry, "licenseExpression") ??
                      ReadJsonString(catalogEntry, "licenseUrl"),
            RequireLicenseAcceptance = ReadJsonBoolean(catalogEntry, "requireLicenseAcceptance"),
            PackageSource = ReadJsonString(catalogEntry, "packageContent"),
            Dependencies = ReadJsonDependencies(catalogEntry).ToList()
        });
    }

    private static ManagedModuleCatalogVersion CreateNuGetV3Version(
        string packageBaseAddress,
        string packageId,
        string version,
        bool listed)
        => new()
        {
            Version = version.Trim(),
            IsPrerelease = ManagedModuleVersionComparer.IsPrerelease(version),
            Listed = listed,
            PackageSource = BuildNuGetV3PackageUri(packageBaseAddress, packageId, version).ToString()
        };

    private static ManagedModuleCatalogVersion EnsureNuGetV3VersionPackageSource(
        ManagedModuleCatalogVersion version,
        string packageBaseAddress,
        string packageId)
    {
        if (string.IsNullOrWhiteSpace(version.PackageSource))
            version.PackageSource = BuildNuGetV3PackageUri(packageBaseAddress, packageId, version.Version).ToString();

        return version;
    }

    private static string ResolveNuGetV2MetadataSource(ManagedModuleCatalog catalog)
    {
        if (IsPowerShellGalleryV3(catalog.Source))
            return ManagedModuleCatalogDefaults.PowerShellGalleryV2;
        if (IsPowerShellGalleryV2(catalog.Source))
            return ManagedModuleCatalogDefaults.PowerShellGalleryV2;
        if (catalog.RepositoryKind == ManagedModuleRepositoryKind.NuGetV2)
            return catalog.Source.Trim().TrimEnd('/');

        throw new NotSupportedException($"Managed module catalog refresh currently supports PowerShell Gallery and NuGet v2 metadata sources. Source '{catalog.Source}' is '{catalog.RepositoryKind}'.");
    }

    private static string NormalizeName(string name)
        => string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Catalog name is required.", nameof(name))
            : name.Trim();

    private static string NormalizeSource(string source)
        => string.IsNullOrWhiteSpace(source)
            ? ManagedModuleCatalogDefaults.PowerShellGalleryV3
            : source.Trim().TrimEnd('/');

    private ManagedModuleCatalogDocument ReadDocument()
    {
        if (!File.Exists(_path))
            return new ManagedModuleCatalogDocument();

        var json = File.ReadAllText(_path);
        if (string.IsNullOrWhiteSpace(json))
            return new ManagedModuleCatalogDocument();

        try
        {
            var document = JsonSerializer.Deserialize<ManagedModuleCatalogDocument>(json, JsonOptions) ?? new ManagedModuleCatalogDocument();
            document.Catalogs ??= new List<ManagedModuleCatalog>();
            return document;
        }
        catch (JsonException)
        {
            return new ManagedModuleCatalogDocument();
        }
    }

    private void WriteDocument(ManagedModuleCatalogDocument document)
    {
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory!);

        File.WriteAllText(_path, JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine);
    }

    private static Uri BuildFindPackagesByIdUri(string source, string packageId)
    {
        var escapedId = Uri.EscapeDataString(packageId.Trim().Replace("'", "''"));
        return new Uri(new Uri(EnsureTrailingSlash(source)), $"FindPackagesById()?id='{escapedId}'&semVerLevel=2.0.0");
    }

    private static Uri BuildNuGetV2PackageUri(string source, string packageId, string version)
        => new(new Uri(EnsureTrailingSlash(source)), $"package/{Uri.EscapeDataString(packageId.Trim())}/{Uri.EscapeDataString(version.Trim())}");

    private static Uri BuildPowerShellGalleryCdnPackageUri(string packageId, string version)
    {
        var lowerId = packageId.Trim().ToLowerInvariant();
        var lowerVersion = version.Trim().ToLowerInvariant();
        return new Uri("https://cdn.powershellgallery.com/packages/" +
                       $"{Uri.EscapeDataString(lowerId)}.{Uri.EscapeDataString(lowerVersion)}.nupkg");
    }

    private static Uri BuildNuGetV3PackageUri(string packageBaseAddress, string packageId, string version)
    {
        var lowerId = packageId.Trim().ToLowerInvariant();
        var lowerVersion = version.Trim().ToLowerInvariant();
        return new Uri(new Uri(EnsureTrailingSlash(packageBaseAddress)),
            $"{Uri.EscapeDataString(lowerId)}/{Uri.EscapeDataString(lowerVersion)}/{Uri.EscapeDataString(lowerId)}.{Uri.EscapeDataString(lowerVersion)}.nupkg");
    }

    private static bool IsPowerShellGalleryV3(string source)
        => source.Trim().TrimEnd('/').Equals(ManagedModuleCatalogDefaults.PowerShellGalleryV3, StringComparison.OrdinalIgnoreCase);

    private static bool IsPowerShellGalleryV2(string source)
        => source.Trim().TrimEnd('/').Equals(ManagedModuleCatalogDefaults.PowerShellGalleryV2, StringComparison.OrdinalIgnoreCase);

    private static string EnsureTrailingSlash(string value)
        => value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";

    private static bool TryNormalizeNuGetV3ServiceIndexSource(string source, out Uri serviceIndexSource)
    {
        serviceIndexSource = null!;
        if (!Uri.TryCreate(source.Trim(), UriKind.Absolute, out var uri))
            return false;
        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        var path = uri.AbsolutePath.TrimEnd('/');
        if (!path.EndsWith("index.json", StringComparison.OrdinalIgnoreCase))
            return false;

        serviceIndexSource = uri;
        return true;
    }

    private static bool IsNuGetV3PackageBaseResource(JsonElement resource)
        => ResourceTypeContains(resource, "PackageBaseAddress");

    private static bool IsNuGetV3RegistrationBaseResource(JsonElement resource)
        => ResourceTypeContains(resource, "RegistrationsBaseUrl");

    private static bool ResourceTypeContains(JsonElement resource, string value)
    {
        if (!resource.TryGetProperty("@type", out var type))
            return false;

        if (type.ValueKind == JsonValueKind.String)
            return type.GetString()?.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;

        return type.ValueKind == JsonValueKind.Array &&
               type.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String &&
                                                 item.GetString()?.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0);
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

    private static string? ReadJsonString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString().Trim(),
            _ => null
        };
    }

    private static string? ReadJsonStringOrArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.String)
            return value.GetString()?.Trim();
        if (value.ValueKind != JsonValueKind.Array)
            return null;

        var values = value.EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => item.GetString()?.Trim())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
        return values.Length == 0 ? null : string.Join(", ", values);
    }

    private static bool ReadJsonBoolean(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return false;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => false
        };
    }

    private static bool ReadJsonListed(JsonElement element)
    {
        if (element.TryGetProperty("listed", out var listed))
        {
            return listed.ValueKind switch
            {
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(listed.GetString(), out var parsed) => parsed,
                _ => true
            };
        }

        var published = ReadJsonString(element, "published");
        return !string.Equals(published, "1900-01-01T00:00:00", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset? ReadJsonDateTimeOffset(JsonElement element, string name)
        => DateTimeOffset.TryParse(ReadJsonString(element, name), out var parsed) ? parsed.ToUniversalTime() : null;

    private static long? ReadJsonInt64(JsonElement element, string name)
        => long.TryParse(ReadJsonString(element, name), out var parsed) ? parsed : null;

    private static IEnumerable<string> ReadJsonTags(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            yield break;

        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                    continue;

                var tag = item.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(tag))
                    yield return tag!;
            }

            yield break;
        }

        foreach (var tag in SplitTags(value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()))
            yield return tag;
    }

    private static IEnumerable<ManagedModuleDependencyInfo> ReadJsonDependencies(JsonElement catalogEntry)
    {
        if (!catalogEntry.TryGetProperty("dependencyGroups", out var groups) || groups.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var group in groups.EnumerateArray())
        {
            var targetFramework = ReadJsonString(group, "targetFramework");
            if (!group.TryGetProperty("dependencies", out var dependencies) || dependencies.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var dependency in dependencies.EnumerateArray())
            {
                var id = ReadJsonString(dependency, "id");
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                yield return new ManagedModuleDependencyInfo
                {
                    Id = id!,
                    VersionRange = ReadJsonString(dependency, "range"),
                    TargetFramework = targetFramework
                };
            }
        }
    }

    private static string? ReadString(XElement? entry, XNamespace data, string name)
        => entry?.Descendants(data + name).FirstOrDefault()?.Value.Trim();

    private static bool ReadBoolean(XElement entry, XNamespace data, string name)
        => bool.TryParse(ReadString(entry, data, name), out var parsed) && parsed;

    private static bool ReadListed(XElement entry, XNamespace data)
    {
        var listedText = ReadString(entry, data, "Listed");
        if (bool.TryParse(listedText, out var listed))
            return listed;

        var published = ReadString(entry, data, "Published");
        return !string.Equals(published, "1900-01-01T00:00:00", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset? ReadDateTimeOffset(XElement entry, XNamespace data, string name)
        => DateTimeOffset.TryParse(ReadString(entry, data, name), out var parsed) ? parsed.ToUniversalTime() : null;

    private static long? ReadInt64(XElement entry, XNamespace data, string name)
        => long.TryParse(ReadString(entry, data, name), out var parsed) ? parsed : null;

    private static IEnumerable<string> SplitTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
            yield break;

        foreach (var tag in tags!.Split(new[] { ' ', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = tag.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
                yield return trimmed;
        }
    }

    private static IEnumerable<ManagedModuleDependencyInfo> ReadDependencies(XElement entry, XNamespace data)
    {
        var value = ReadString(entry, data, "Dependencies");
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        foreach (var dependencyText in value!.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = dependencyText.Split(new[] { ':' }, 3);
            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
                continue;

            yield return new ManagedModuleDependencyInfo
            {
                Id = parts[0].Trim(),
                VersionRange = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1].Trim() : null,
                TargetFramework = parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2].Trim() : null
            };
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class ManagedModuleCatalogDocument
    {
        public int Version { get; set; } = 1;

        public List<ManagedModuleCatalog> Catalogs { get; set; } = new();
    }

    private sealed class NuGetV3CatalogResources
    {
        public string PackageBaseAddress { get; set; } = string.Empty;

        public string? RegistrationBaseAddress { get; set; }
    }

    private sealed class NuGetV3RegistrationPackage
    {
        public string Id { get; set; } = string.Empty;

        public string? Authors { get; set; }

        public string? Owners { get; set; }

        public string? Description { get; set; }

        public string? ProjectUrl { get; set; }

        public string? GalleryUrl { get; set; }

        public List<string> Tags { get; } = new();

        public List<ManagedModuleCatalogVersion> Versions { get; } = new();
    }
}
