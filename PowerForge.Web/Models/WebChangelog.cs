namespace PowerForge.Web;

/// <summary>Options for changelog generation.</summary>
public sealed class WebChangelogOptions
{
    /// <summary>Changelog source selection.</summary>
    public WebChangelogSource Source { get; set; } = WebChangelogSource.Auto;
    /// <summary>Local changelog path (CHANGELOG.md).</summary>
    public string? ChangelogPath { get; set; }
    /// <summary>Repository owner/name (owner/repo).</summary>
    public string? Repo { get; set; }
    /// <summary>Repository URL (https://github.com/owner/repo).</summary>
    public string? RepoUrl { get; set; }
    /// <summary>Optional repository token.</summary>
    public string? Token { get; set; }
    /// <summary>Maximum number of releases to include.</summary>
    public int? MaxReleases { get; set; }
    /// <summary>Output JSON path.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>Optional base directory for resolving relative paths.</summary>
    public string? BaseDirectory { get; set; }
    /// <summary>Optional title override.</summary>
    public string? Title { get; set; }
    /// <summary>Include release assets.</summary>
    public bool IncludeAssets { get; set; } = true;
}

/// <summary>Result payload for changelog generation.</summary>
public sealed class WebChangelogResult
{
    /// <summary>Output JSON path.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>Number of releases included.</summary>
    public int ReleaseCount { get; set; }
    /// <summary>Resolved source for data.</summary>
    public WebChangelogSource Source { get; set; } = WebChangelogSource.Auto;
    /// <summary>Warnings emitted during generation.</summary>
    public string[] Warnings { get; set; } = Array.Empty<string>();
}
