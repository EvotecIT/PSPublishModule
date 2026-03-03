using System.Text.Json.Serialization;

namespace PowerForge.Web;

/// <summary>Options for ecosystem stats generation.</summary>
public sealed class WebEcosystemStatsOptions
{
    /// <summary>Output JSON path.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>Optional base directory for resolving relative paths.</summary>
    public string? BaseDirectory { get; set; }
    /// <summary>Optional title override.</summary>
    public string? Title { get; set; }
    /// <summary>GitHub organization to aggregate.</summary>
    public string? GitHubOrganization { get; set; }
    /// <summary>Optional GitHub token for authenticated requests.</summary>
    public string? GitHubToken { get; set; }
    /// <summary>NuGet owner profile to aggregate.</summary>
    public string? NuGetOwner { get; set; }
    /// <summary>PowerShell Gallery profile owner/handle.</summary>
    public string? PowerShellGalleryOwner { get; set; }
    /// <summary>PowerShell Gallery author name for direct filtering.</summary>
    public string? PowerShellGalleryAuthor { get; set; }
    /// <summary>Maximum items to pull from each source.</summary>
    public int MaxItems { get; set; } = 500;
    /// <summary>HTTP request timeout in seconds.</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;
}

/// <summary>Result payload for ecosystem stats generation.</summary>
public sealed class WebEcosystemStatsResult
{
    /// <summary>Output JSON path.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>Number of GitHub repositories included.</summary>
    public int RepositoryCount { get; set; }
    /// <summary>Number of NuGet packages included.</summary>
    public int NuGetPackageCount { get; set; }
    /// <summary>Number of PowerShell Gallery modules included.</summary>
    public int PowerShellGalleryModuleCount { get; set; }
    /// <summary>Warnings emitted during generation.</summary>
    public string[] Warnings { get; set; } = Array.Empty<string>();
}

/// <summary>Unified ecosystem stats document.</summary>
public sealed class WebEcosystemStatsDocument
{
    /// <summary>Document title.</summary>
    public string Title { get; set; } = "Ecosystem Stats";
    /// <summary>Generation timestamp in UTC.</summary>
    public string GeneratedAtUtc { get; set; } = string.Empty;
    /// <summary>Aggregated totals.</summary>
    public WebEcosystemStatsSummary Summary { get; set; } = new();
    /// <summary>GitHub repository statistics.</summary>
    public WebEcosystemGitHubStats? GitHub { get; set; }
    /// <summary>NuGet package statistics.</summary>
    [JsonPropertyName("nuget")]
    public WebEcosystemNuGetStats? NuGet { get; set; }
    /// <summary>PowerShell Gallery module statistics.</summary>
    public WebEcosystemPowerShellGalleryStats? PowerShellGallery { get; set; }
    /// <summary>Warnings emitted during generation.</summary>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>Aggregated ecosystem totals.</summary>
public sealed class WebEcosystemStatsSummary
{
    /// <summary>Total repositories counted from GitHub.</summary>
    public int RepositoryCount { get; set; }
    /// <summary>Total NuGet packages counted.</summary>
    public int NuGetPackageCount { get; set; }
    /// <summary>Total PowerShell Gallery modules counted.</summary>
    public int PowerShellGalleryModuleCount { get; set; }
    /// <summary>Total GitHub stars across repositories.</summary>
    public long GitHubStars { get; set; }
    /// <summary>Total GitHub forks across repositories.</summary>
    public long GitHubForks { get; set; }
    /// <summary>Total NuGet downloads across packages.</summary>
    public long NuGetDownloads { get; set; }
    /// <summary>Total PowerShell Gallery downloads across modules.</summary>
    public long PowerShellGalleryDownloads { get; set; }
    /// <summary>Total downloads across package ecosystems.</summary>
    public long TotalDownloads { get; set; }
}

/// <summary>GitHub statistics section.</summary>
public sealed class WebEcosystemGitHubStats
{
    /// <summary>Organization name.</summary>
    public string Organization { get; set; } = string.Empty;
    /// <summary>Repository count.</summary>
    public int RepositoryCount { get; set; }
    /// <summary>Total stars.</summary>
    public long TotalStars { get; set; }
    /// <summary>Total forks.</summary>
    public long TotalForks { get; set; }
    /// <summary>Total watchers.</summary>
    public long TotalWatchers { get; set; }
    /// <summary>Total open issues.</summary>
    public long TotalOpenIssues { get; set; }
    /// <summary>Repository list.</summary>
    public List<WebEcosystemGitHubRepository> Repositories { get; set; } = new();
}

/// <summary>GitHub repository statistics entry.</summary>
public sealed class WebEcosystemGitHubRepository
{
    /// <summary>Repository name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Full repository name (owner/repo).</summary>
    public string FullName { get; set; } = string.Empty;
    /// <summary>Repository URL.</summary>
    public string Url { get; set; } = string.Empty;
    /// <summary>Primary language.</summary>
    public string? Language { get; set; }
    /// <summary>Whether the repository is archived.</summary>
    public bool Archived { get; set; }
    /// <summary>Star count.</summary>
    public int Stars { get; set; }
    /// <summary>Fork count.</summary>
    public int Forks { get; set; }
    /// <summary>Watcher count.</summary>
    public int Watchers { get; set; }
    /// <summary>Open issues count.</summary>
    public int OpenIssues { get; set; }
    /// <summary>Last push timestamp in UTC when available.</summary>
    public DateTimeOffset? PushedAt { get; set; }
}

/// <summary>NuGet statistics section.</summary>
public sealed class WebEcosystemNuGetStats
{
    /// <summary>Owner profile name.</summary>
    public string Owner { get; set; } = string.Empty;
    /// <summary>Package count.</summary>
    public int PackageCount { get; set; }
    /// <summary>Total downloads across packages.</summary>
    public long TotalDownloads { get; set; }
    /// <summary>Package list.</summary>
    [JsonPropertyName("packages")]
    public List<WebEcosystemNuGetPackage> Items { get; set; } = new();
}

/// <summary>NuGet package statistics entry.</summary>
public sealed class WebEcosystemNuGetPackage
{
    /// <summary>Package identifier.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Latest version from search results.</summary>
    public string? Version { get; set; }
    /// <summary>Total package downloads.</summary>
    public long TotalDownloads { get; set; }
    /// <summary>Package URL.</summary>
    public string? PackageUrl { get; set; }
    /// <summary>Project URL.</summary>
    public string? ProjectUrl { get; set; }
    /// <summary>Short description.</summary>
    public string? Description { get; set; }
    /// <summary>Whether the package is verified.</summary>
    public bool Verified { get; set; }
}

/// <summary>PowerShell Gallery statistics section.</summary>
public sealed class WebEcosystemPowerShellGalleryStats
{
    /// <summary>Owner/profile hint used for lookup.</summary>
    public string Owner { get; set; } = string.Empty;
    /// <summary>Author filter used for direct lookup when provided.</summary>
    public string? AuthorFilter { get; set; }
    /// <summary>Module count.</summary>
    public int ModuleCount { get; set; }
    /// <summary>Total downloads across modules.</summary>
    public long TotalDownloads { get; set; }
    /// <summary>Module list.</summary>
    public List<WebEcosystemPowerShellGalleryModule> Modules { get; set; } = new();
}

/// <summary>PowerShell Gallery module statistics entry.</summary>
public sealed class WebEcosystemPowerShellGalleryModule
{
    /// <summary>Module ID.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Latest version.</summary>
    public string? Version { get; set; }
    /// <summary>Total module downloads.</summary>
    public long DownloadCount { get; set; }
    /// <summary>Authors value.</summary>
    public string? Authors { get; set; }
    /// <summary>Owners value.</summary>
    public string? Owners { get; set; }
    /// <summary>Gallery package URL.</summary>
    public string? GalleryUrl { get; set; }
    /// <summary>Project URL.</summary>
    public string? ProjectUrl { get; set; }
    /// <summary>Short description.</summary>
    public string? Description { get; set; }
}
