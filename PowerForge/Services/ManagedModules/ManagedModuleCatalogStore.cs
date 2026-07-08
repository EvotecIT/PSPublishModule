using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace PowerForge;

/// <summary>
/// Stores and refreshes local managed module catalog metadata.
/// </summary>
public sealed class ManagedModuleCatalogStore
{
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

        catalog.Name = name;
        catalog.Source = NormalizeSource(request.Source);
        catalog.RepositoryKind = request.RepositoryKind;
        catalog.Mode = request.Mode;
        catalog.MaxStaleness = request.MaxStaleness <= TimeSpan.Zero ? TimeSpan.FromDays(14) : request.MaxStaleness;
        catalog.IncludePrerelease = request.IncludePrerelease;
        catalog.UpdatedAtUtc = now;
        catalog.Packages ??= new List<ManagedModuleCatalogPackage>();

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
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            var source = ResolveMetadataSource(catalog);
            var documents = await ReadNuGetV2XmlPagesAsync(source, packageName, cancellationToken).ConfigureAwait(false);
            var package = ReadPackage(source, packageName, documents, includePrerelease);
            if (package is null)
                warnings.Add($"Package '{packageName}' was not found in catalog source '{source}'.");
            return package;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or System.Xml.XmlException)
        {
            warnings.Add($"Package '{packageName}' refresh failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private async Task<IReadOnlyList<XDocument>> ReadNuGetV2XmlPagesAsync(
        string source,
        string packageName,
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

        var version = versionText.Trim();
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

    private static string ResolveMetadataSource(ManagedModuleCatalog catalog)
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
        var escapedId = Uri.EscapeDataString(packageId.Trim().Replace("'", "''", StringComparison.Ordinal));
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

    private static bool IsPowerShellGalleryV3(string source)
        => source.Trim().TrimEnd('/').Equals(ManagedModuleCatalogDefaults.PowerShellGalleryV3, StringComparison.OrdinalIgnoreCase);

    private static bool IsPowerShellGalleryV2(string source)
        => source.Trim().TrimEnd('/').Equals(ManagedModuleCatalogDefaults.PowerShellGalleryV2, StringComparison.OrdinalIgnoreCase);

    private static string EnsureTrailingSlash(string value)
        => value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";

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

        foreach (var tag in tags.Split(new[] { ' ', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
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
}
