namespace PowerForge.Web;

/// <summary>
/// Options for private gallery data generation.
/// </summary>
public sealed class WebPrivateGalleryOptions
{
    /// <summary>Output directory for private gallery JSON files.</summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Optional base directory for resolving relative paths.</summary>
    public string? BaseDirectory { get; set; }

    /// <summary>Optional title for the generated gallery document.</summary>
    public string? Title { get; set; }

    /// <summary>Azure DevOps organization name.</summary>
    public string? Organization { get; set; }

    /// <summary>Optional Azure DevOps project name for project-scoped feeds.</summary>
    public string? Project { get; set; }

    /// <summary>Azure Artifacts feed name.</summary>
    public string? Feed { get; set; }

    /// <summary>Repository name users register locally.</summary>
    public string? RepositoryName { get; set; }

    /// <summary>Whether all versions should be included.</summary>
    public bool IncludeAllVersions { get; set; } = true;

    /// <summary>Whether package content should be downloaded and inspected.</summary>
    public bool IncludePackageContent { get; set; }

    /// <summary>Whether package metrics should be queried.</summary>
    public bool IncludeMetrics { get; set; }

    /// <summary>Maximum packages to index.</summary>
    public int MaxPackages { get; set; } = 500;

    /// <summary>Maximum versions per package to inspect when package content inspection is enabled.</summary>
    public int MaxVersionsPerPackage { get; set; } = 1;

    /// <summary>HTTP request timeout in seconds.</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>Optional token value.</summary>
    public string? Token { get; set; }

    /// <summary>Optional token environment variable name.</summary>
    public string? TokenEnvironmentVariable { get; set; }

    /// <summary>Authentication kind for token-based requests.</summary>
    public PrivateGalleryAuthenticationKind AuthenticationKind { get; set; } = PrivateGalleryAuthenticationKind.Bearer;

    /// <summary>Optional temp directory for downloaded packages.</summary>
    public string? TempDirectory { get; set; }
}

/// <summary>
/// Result returned by private gallery data generation.
/// </summary>
public sealed class WebPrivateGalleryResult
{
    /// <summary>Path to the generated feed JSON file.</summary>
    public string FeedPath { get; set; } = string.Empty;

    /// <summary>Path to the generated search JSON file.</summary>
    public string SearchPath { get; set; } = string.Empty;

    /// <summary>Number of packages indexed.</summary>
    public int PackageCount { get; set; }

    /// <summary>Number of package versions indexed.</summary>
    public int VersionCount { get; set; }

    /// <summary>Number of commands discovered.</summary>
    public int CommandCount { get; set; }

    /// <summary>Warnings emitted during generation.</summary>
    public string[] Warnings { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Search document generated from private gallery data.
/// </summary>
public sealed class WebPrivateGallerySearchDocument
{
    /// <summary>Schema version for the search data contract.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Document format identifier.</summary>
    public string Format { get; set; } = "powerforge.private-gallery.search";

    /// <summary>Generation timestamp in UTC.</summary>
    public string GeneratedAtUtc { get; set; } = string.Empty;

    /// <summary>Search entries.</summary>
    public List<WebPrivateGallerySearchEntry> Entries { get; set; } = new();
}

/// <summary>
/// Search entry generated from private gallery module, version, command, or document metadata.
/// </summary>
public sealed class WebPrivateGallerySearchEntry
{
    /// <summary>Stable search entry id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Entry kind, such as module, version, command, or document.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Entry title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Module/package name.</summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>Version associated with the entry.</summary>
    public string? Version { get; set; }

    /// <summary>Entry summary.</summary>
    public string? Summary { get; set; }

    /// <summary>Entry tags/facets.</summary>
    public List<string> Tags { get; set; } = new();
}
