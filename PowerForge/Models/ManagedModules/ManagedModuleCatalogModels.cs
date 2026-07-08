namespace PowerForge;

/// <summary>
/// Local managed module catalog usage mode.
/// </summary>
public enum ManagedModuleCatalogCacheMode
{
    /// <summary>Catalog caching is disabled.</summary>
    Off,

    /// <summary>Live repository metadata is preferred, while successful lookups can populate the local catalog.</summary>
    ReadThrough,

    /// <summary>Live repository metadata is preferred, and stale local catalog data can be used when live metadata fails.</summary>
    Fallback,

    /// <summary>Local catalog data is preferred when it is fresh enough, with live refresh used when needed.</summary>
    PreferCache,

    /// <summary>Only local catalog data should be used.</summary>
    Offline
}

/// <summary>
/// Persisted managed module catalog configuration and package metadata.
/// </summary>
public sealed class ManagedModuleCatalog
{
    /// <summary>Catalog name, usually a repository name such as PSGallery.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Repository source URL used for live metadata refresh.</summary>
    public string Source { get; set; } = ManagedModuleCatalogDefaults.PowerShellGalleryV3;

    /// <summary>Repository kind used by the managed module engine.</summary>
    public ManagedModuleRepositoryKind RepositoryKind { get; set; } = ManagedModuleRepositoryKind.NuGetV3;

    /// <summary>Local catalog cache mode.</summary>
    public ManagedModuleCatalogCacheMode Mode { get; set; } = ManagedModuleCatalogCacheMode.Fallback;

    /// <summary>Maximum catalog age accepted for fallback/prefer-cache decisions.</summary>
    public TimeSpan MaxStaleness { get; set; } = TimeSpan.FromDays(14);

    /// <summary>Whether prerelease versions are included during metadata refresh.</summary>
    public bool IncludePrerelease { get; set; } = true;

    /// <summary>Catalog creation timestamp in UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Catalog configuration update timestamp in UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>Last successful metadata refresh timestamp in UTC.</summary>
    public DateTimeOffset? LastRefreshAtUtc { get; set; }

    /// <summary>Last refresh warning, when the previous refresh completed with degraded metadata.</summary>
    public string? LastWarning { get; set; }

    /// <summary>Known packages in this catalog.</summary>
    public List<ManagedModuleCatalogPackage> Packages { get; set; } = new();
}

/// <summary>
/// Package metadata stored in a managed module catalog.
/// </summary>
public sealed class ManagedModuleCatalogPackage
{
    /// <summary>Package id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Latest listed stable version known to the catalog.</summary>
    public string? LatestStableVersion { get; set; }

    /// <summary>Latest listed prerelease version known to the catalog.</summary>
    public string? LatestPrereleaseVersion { get; set; }

    /// <summary>Package authors from repository metadata.</summary>
    public string? Authors { get; set; }

    /// <summary>Package owners from repository metadata.</summary>
    public string? Owners { get; set; }

    /// <summary>Package description from repository metadata.</summary>
    public string? Description { get; set; }

    /// <summary>Project URL from repository metadata.</summary>
    public string? ProjectUrl { get; set; }

    /// <summary>Gallery details URL from repository metadata.</summary>
    public string? GalleryUrl { get; set; }

    /// <summary>Tags from repository metadata.</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>Package versions known to the catalog.</summary>
    public List<ManagedModuleCatalogVersion> Versions { get; set; } = new();

    /// <summary>Last successful package metadata refresh timestamp in UTC.</summary>
    public DateTimeOffset? LastRefreshAtUtc { get; set; }

    /// <summary>Metadata source receipt for this package.</summary>
    public string SourceReceipt { get; set; } = string.Empty;
}

/// <summary>
/// Package version metadata stored in a managed module catalog.
/// </summary>
public sealed class ManagedModuleCatalogVersion
{
    /// <summary>Version text.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Normalized version text when the source provides one.</summary>
    public string? NormalizedVersion { get; set; }

    /// <summary>True when the version is prerelease.</summary>
    public bool IsPrerelease { get; set; }

    /// <summary>True when repository metadata indicates the version is listed.</summary>
    public bool Listed { get; set; } = true;

    /// <summary>True when this version is the repository latest stable version.</summary>
    public bool IsLatestVersion { get; set; }

    /// <summary>True when this version is the repository latest absolute version.</summary>
    public bool IsAbsoluteLatestVersion { get; set; }

    /// <summary>Package creation timestamp in UTC when known.</summary>
    public DateTimeOffset? CreatedAtUtc { get; set; }

    /// <summary>Package published timestamp in UTC when known.</summary>
    public DateTimeOffset? PublishedAtUtc { get; set; }

    /// <summary>Total package download count when known.</summary>
    public long? DownloadCount { get; set; }

    /// <summary>Version-specific download count when known.</summary>
    public long? VersionDownloadCount { get; set; }

    /// <summary>Package size in bytes when known.</summary>
    public long? PackageSize { get; set; }

    /// <summary>Package hash when known.</summary>
    public string? PackageHash { get; set; }

    /// <summary>Package hash algorithm when known.</summary>
    public string? PackageHashAlgorithm { get; set; }

    /// <summary>License expression, license URL, or license name when known.</summary>
    public string? License { get; set; }

    /// <summary>True when repository metadata indicates license acceptance is required.</summary>
    public bool RequireLicenseAcceptance { get; set; }

    /// <summary>Direct package content URL from repository metadata.</summary>
    public string? PackageSource { get; set; }

    /// <summary>Predictable PowerShell Gallery CDN package URL when applicable.</summary>
    public string? CdnPackageSource { get; set; }

    /// <summary>Dependencies from repository metadata.</summary>
    public List<ManagedModuleDependencyInfo> Dependencies { get; set; } = new();
}

/// <summary>
/// Request used to create or update managed module catalog settings.
/// </summary>
public sealed class ManagedModuleCatalogSetRequest
{
    /// <summary>Catalog name.</summary>
    public string Name { get; set; } = "PSGallery";

    /// <summary>Repository source URL.</summary>
    public string Source { get; set; } = ManagedModuleCatalogDefaults.PowerShellGalleryV3;

    /// <summary>Repository kind.</summary>
    public ManagedModuleRepositoryKind RepositoryKind { get; set; } = ManagedModuleRepositoryKind.NuGetV3;

    /// <summary>Catalog cache mode.</summary>
    public ManagedModuleCatalogCacheMode Mode { get; set; } = ManagedModuleCatalogCacheMode.Fallback;

    /// <summary>Maximum accepted catalog staleness.</summary>
    public TimeSpan MaxStaleness { get; set; } = TimeSpan.FromDays(14);

    /// <summary>Whether refresh should include prerelease versions.</summary>
    public bool IncludePrerelease { get; set; } = true;
}

/// <summary>
/// Request used to refresh managed module catalog metadata.
/// </summary>
public sealed class ManagedModuleCatalogUpdateRequest
{
    /// <summary>Catalog name.</summary>
    public string Name { get; set; } = "PSGallery";

    /// <summary>Package names to refresh. When empty, existing package entries are refreshed.</summary>
    public IReadOnlyList<string> PackageNames { get; set; } = Array.Empty<string>();

    /// <summary>Optional prerelease override for this refresh.</summary>
    public bool? IncludePrerelease { get; set; }
}

/// <summary>
/// Result returned by a managed module catalog refresh.
/// </summary>
public sealed class ManagedModuleCatalogUpdateResult
{
    /// <summary>Catalog name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Catalog source URL.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Catalog storage path.</summary>
    public string CatalogPath { get; set; } = string.Empty;

    /// <summary>Number of packages refreshed.</summary>
    public int RefreshedPackageCount { get; set; }

    /// <summary>Total package count currently stored in the catalog.</summary>
    public int PackageCount { get; set; }

    /// <summary>Total version count currently stored in the catalog.</summary>
    public int VersionCount { get; set; }

    /// <summary>Refresh timestamp in UTC.</summary>
    public DateTimeOffset RefreshedAtUtc { get; set; }

    /// <summary>Warnings emitted during refresh.</summary>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Constants used by the managed module catalog.
/// </summary>
public static class ManagedModuleCatalogDefaults
{
    /// <summary>Canonical PowerShell Gallery NuGet v3 index URL.</summary>
    public const string PowerShellGalleryV3 = "https://www.powershellgallery.com/api/v3/index.json";

    /// <summary>PowerShell Gallery v2 metadata API URL used by reliable read paths.</summary>
    public const string PowerShellGalleryV2 = "https://www.powershellgallery.com/api/v2";
}
