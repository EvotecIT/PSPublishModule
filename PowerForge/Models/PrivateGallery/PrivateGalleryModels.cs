using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>
/// Supported private gallery feed providers.
/// </summary>
public enum PrivateGalleryIndexProvider
{
    /// <summary>Azure DevOps Azure Artifacts feed.</summary>
    AzureArtifacts
}

/// <summary>
/// Authentication mode used for private gallery HTTP requests.
/// </summary>
public enum PrivateGalleryAuthenticationKind
{
    /// <summary>No authentication header is added.</summary>
    None,

    /// <summary>Use a bearer token.</summary>
    Bearer,

    /// <summary>Use Azure DevOps PAT-style basic authentication with an empty username.</summary>
    BasicToken
}

/// <summary>
/// Options for indexing a private gallery feed.
/// </summary>
public sealed class PrivateGalleryIndexOptions
{
    /// <summary>Provider backing the private gallery.</summary>
    public PrivateGalleryIndexProvider Provider { get; set; } = PrivateGalleryIndexProvider.AzureArtifacts;

    /// <summary>Azure DevOps organization name.</summary>
    public string? Organization { get; set; }

    /// <summary>Optional Azure DevOps project name for project-scoped feeds.</summary>
    public string? Project { get; set; }

    /// <summary>Azure Artifacts feed name or id.</summary>
    public string? Feed { get; set; }

    /// <summary>Repository name users register locally.</summary>
    public string? RepositoryName { get; set; }

    /// <summary>Optional title for the generated gallery document.</summary>
    public string? Title { get; set; }

    /// <summary>Whether all known versions should be included.</summary>
    public bool IncludeAllVersions { get; set; } = true;

    /// <summary>Whether package content should be downloaded and inspected.</summary>
    public bool IncludePackageContent { get; set; }

    /// <summary>Whether metrics should be queried when the provider supports them.</summary>
    public bool IncludeMetrics { get; set; }

    /// <summary>Maximum number of packages to index.</summary>
    public int MaxPackages { get; set; } = 500;

    /// <summary>Maximum number of versions to inspect per package when content inspection is enabled.</summary>
    public int MaxVersionsPerPackage { get; set; } = 1;

    /// <summary>HTTP request timeout in seconds.</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>Optional token for Azure DevOps or NuGet feed requests.</summary>
    public string? Token { get; set; }

    /// <summary>Authentication mode for token-based requests.</summary>
    public PrivateGalleryAuthenticationKind AuthenticationKind { get; set; } = PrivateGalleryAuthenticationKind.Bearer;

    /// <summary>Optional temp directory used for downloaded packages.</summary>
    public string? TempDirectory { get; set; }
}

/// <summary>
/// Result returned by the private gallery indexer.
/// </summary>
public sealed class PrivateGalleryIndexResult
{
    /// <summary>Indexed gallery document.</summary>
    public PrivateGalleryDocument Document { get; set; } = new();

    /// <summary>Warnings emitted during indexing.</summary>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Normalized private gallery document.
/// </summary>
public sealed class PrivateGalleryDocument
{
    /// <summary>Schema version for the private gallery data contract.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Document format identifier.</summary>
    public string Format { get; set; } = "powerforge.private-gallery";

    /// <summary>Display title.</summary>
    public string Title { get; set; } = "Private Gallery";

    /// <summary>Generation timestamp in UTC.</summary>
    public string GeneratedAtUtc { get; set; } = string.Empty;

    /// <summary>Provider backing the gallery.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PrivateGalleryIndexProvider Provider { get; set; } = PrivateGalleryIndexProvider.AzureArtifacts;

    /// <summary>Feed identity.</summary>
    public PrivateGalleryFeed Feed { get; set; } = new();

    /// <summary>Aggregated summary.</summary>
    public PrivateGallerySummary Summary { get; set; } = new();

    /// <summary>Indexed packages.</summary>
    public List<PrivateGalleryPackage> Packages { get; set; } = new();

    /// <summary>Warnings emitted during indexing.</summary>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Private gallery feed identity.
/// </summary>
public sealed class PrivateGalleryFeed
{
    /// <summary>Azure DevOps organization name.</summary>
    public string? Organization { get; set; }

    /// <summary>Azure DevOps project name.</summary>
    public string? Project { get; set; }

    /// <summary>Feed name or id.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Repository name users register locally.</summary>
    public string RepositoryName { get; set; } = string.Empty;

    /// <summary>NuGet v3 service index URL for the feed.</summary>
    public string? NuGetServiceIndexUrl { get; set; }
}

/// <summary>
/// Aggregated private gallery counts.
/// </summary>
public sealed class PrivateGallerySummary
{
    /// <summary>Total package count.</summary>
    public int PackageCount { get; set; }

    /// <summary>Total version count.</summary>
    public int VersionCount { get; set; }

    /// <summary>Total command count discovered from inspected packages.</summary>
    public int CommandCount { get; set; }

    /// <summary>Total document asset count discovered from inspected packages.</summary>
    public int DocumentCount { get; set; }

    /// <summary>Total download count when metrics are available.</summary>
    public long? TotalDownloads { get; set; }
}

/// <summary>
/// Indexed package entry.
/// </summary>
public sealed class PrivateGalleryPackage
{
    /// <summary>Provider package id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display package name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Package protocol type, such as NuGet.</summary>
    public string ProtocolType { get; set; } = "NuGet";

    /// <summary>Latest version text.</summary>
    public string? LatestVersion { get; set; }

    /// <summary>Package description.</summary>
    public string? Description { get; set; }

    /// <summary>Package URL in the provider.</summary>
    public string? WebUrl { get; set; }

    /// <summary>Package metrics when available.</summary>
    public PrivateGalleryPackageMetrics? Metrics { get; set; }

    /// <summary>Known package versions.</summary>
    public List<PrivateGalleryPackageVersion> Versions { get; set; } = new();

    /// <summary>Module metadata discovered from inspected package content.</summary>
    public PrivateGalleryModuleMetadata? Module { get; set; }

    /// <summary>Warnings related to this package.</summary>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Indexed package version entry.
/// </summary>
public sealed class PrivateGalleryPackageVersion
{
    /// <summary>Provider version id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display version.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Normalized version when available.</summary>
    public string? NormalizedVersion { get; set; }

    /// <summary>Whether this version is the provider latest version.</summary>
    public bool IsLatest { get; set; }

    /// <summary>Whether this version is listed.</summary>
    public bool? IsListed { get; set; }

    /// <summary>Whether this version is deleted.</summary>
    public bool? IsDeleted { get; set; }

    /// <summary>Publish date in UTC when available.</summary>
    public DateTimeOffset? PublishedAtUtc { get; set; }

    /// <summary>Version description.</summary>
    public string? Description { get; set; }

    /// <summary>Version author.</summary>
    public string? Author { get; set; }

    /// <summary>Dependencies reported by the feed.</summary>
    public List<PrivateGalleryDependency> Dependencies { get; set; } = new();

    /// <summary>Feed views containing this version.</summary>
    public List<string> Views { get; set; } = new();

    /// <summary>Package content inspection result.</summary>
    public PrivateGalleryModuleMetadata? Module { get; set; }

    /// <summary>Version metrics when available.</summary>
    public PrivateGalleryPackageMetrics? Metrics { get; set; }
}

/// <summary>
/// Dependency entry.
/// </summary>
public sealed class PrivateGalleryDependency
{
    /// <summary>Dependency name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Dependency version range.</summary>
    public string? VersionRange { get; set; }

    /// <summary>Optional dependency group.</summary>
    public string? Group { get; set; }
}

/// <summary>
/// Module metadata discovered from package content.
/// </summary>
public sealed class PrivateGalleryModuleMetadata
{
    /// <summary>Module name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Module version.</summary>
    public string? Version { get; set; }

    /// <summary>Module description.</summary>
    public string? Description { get; set; }

    /// <summary>Module author.</summary>
    public string? Author { get; set; }

    /// <summary>Company name.</summary>
    public string? CompanyName { get; set; }

    /// <summary>PowerShell version requirement.</summary>
    public string? PowerShellVersion { get; set; }

    /// <summary>Compatible PowerShell editions.</summary>
    public List<string> CompatiblePSEditions { get; set; } = new();

    /// <summary>Tags discovered from private data.</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>Commands exported or documented by the module.</summary>
    public List<PrivateGalleryCommandMetadata> Commands { get; set; } = new();

    /// <summary>Document assets shipped in the package.</summary>
    public List<PrivateGalleryDocumentAsset> Documents { get; set; } = new();

    /// <summary>Required module dependencies discovered from the manifest.</summary>
    public List<PrivateGalleryDependency> RequiredModules { get; set; } = new();

    /// <summary>Inspection warnings.</summary>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Command metadata discovered from module manifest or help XML.
/// </summary>
public sealed class PrivateGalleryCommandMetadata
{
    /// <summary>Command name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Command type, such as Function or Cmdlet.</summary>
    public string? Kind { get; set; }

    /// <summary>Command synopsis when available.</summary>
    public string? Synopsis { get; set; }
}

/// <summary>
/// Document asset discovered inside a module package.
/// </summary>
public sealed class PrivateGalleryDocumentAsset
{
    /// <summary>Asset path inside the package.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Asset kind, such as readme, docs, example, help, changelog, or license.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Asset display title.</summary>
    public string? Title { get; set; }

    /// <summary>Asset size in bytes.</summary>
    public long Size { get; set; }
}

/// <summary>
/// Package metrics collected from the provider.
/// </summary>
public sealed class PrivateGalleryPackageMetrics
{
    /// <summary>Total downloads when available.</summary>
    public long? DownloadCount { get; set; }

    /// <summary>Unique users when available.</summary>
    public long? UniqueUsers { get; set; }

    /// <summary>Last download timestamp in UTC when available.</summary>
    public DateTimeOffset? LastDownloadedAtUtc { get; set; }
}
