using System.Text.Json.Serialization;

namespace PowerForge.Web;

/// <summary>Options for release hub generation.</summary>
public sealed class WebReleaseHubOptions
{
    /// <summary>Release source selection.</summary>
    public WebChangelogSource Source { get; set; } = WebChangelogSource.Auto;
    /// <summary>Optional local changelog path fallback (for source=file/auto).</summary>
    public string? ChangelogPath { get; set; }
    /// <summary>Optional local releases JSON path (GitHub API shape or release-hub shape).</summary>
    public string? ReleasesPath { get; set; }
    /// <summary>Repository owner/name (owner/repo).</summary>
    public string? Repo { get; set; }
    /// <summary>Repository URL (https://github.com/owner/repo).</summary>
    public string? RepoUrl { get; set; }
    /// <summary>Optional repository API token.</summary>
    public string? Token { get; set; }
    /// <summary>Maximum number of releases to include.</summary>
    public int? MaxReleases { get; set; }
    /// <summary>GitHub page size (max 100).</summary>
    public int PageSize { get; set; } = 100;
    /// <summary>Maximum number of pages to fetch from GitHub.</summary>
    public int MaxPages { get; set; } = 5;
    /// <summary>Include draft releases.</summary>
    public bool IncludeDraft { get; set; }
    /// <summary>Include prerelease items.</summary>
    public bool IncludePrerelease { get; set; } = true;
    /// <summary>Include release assets.</summary>
    public bool IncludeAssets { get; set; } = true;
    /// <summary>Default channel value for non-prerelease items (for example: stable).</summary>
    public string? DefaultChannel { get; set; } = "stable";
    /// <summary>Output JSON path.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>Optional base directory for resolving relative paths.</summary>
    public string? BaseDirectory { get; set; }
    /// <summary>Optional title override.</summary>
    public string? Title { get; set; }
    /// <summary>Optional product catalog.</summary>
    public List<WebReleaseHubProductInput> Products { get; set; } = new();
    /// <summary>Optional asset mapping/classification rules.</summary>
    public List<WebReleaseHubAssetRuleInput> AssetRules { get; set; } = new();
}

/// <summary>Release hub generation result.</summary>
public sealed class WebReleaseHubResult
{
    /// <summary>Output JSON path.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>Number of included releases.</summary>
    public int ReleaseCount { get; set; }
    /// <summary>Total number of included assets.</summary>
    public int AssetCount { get; set; }
    /// <summary>Resolved source used to generate data.</summary>
    public WebChangelogSource Source { get; set; } = WebChangelogSource.Auto;
    /// <summary>Warnings emitted during generation.</summary>
    public string[] Warnings { get; set; } = Array.Empty<string>();
}

/// <summary>Release hub document.</summary>
public sealed class WebReleaseHubDocument
{
    /// <summary>Document title.</summary>
    public string Title { get; set; } = "Release Hub";
    /// <summary>Generation timestamp in UTC.</summary>
    public string GeneratedAtUtc { get; set; } = string.Empty;
    /// <summary>Data source string.</summary>
    public string Source { get; set; } = "auto";
    /// <summary>Resolved repository identifier when known.</summary>
    public string? Repo { get; set; }
    /// <summary>Latest release markers.</summary>
    public WebReleaseHubLatest Latest { get; set; } = new();
    /// <summary>Product catalog.</summary>
    public List<WebReleaseHubProduct> Products { get; set; } = new();
    /// <summary>Release timeline.</summary>
    public List<WebReleaseHubRelease> Releases { get; set; } = new();
    /// <summary>Warnings emitted during generation.</summary>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>Latest release markers.</summary>
public sealed class WebReleaseHubLatest
{
    /// <summary>Latest stable release tag.</summary>
    public string? StableTag { get; set; }
    /// <summary>Latest prerelease tag.</summary>
    public string? PrereleaseTag { get; set; }
}

/// <summary>Release item model.</summary>
public sealed class WebReleaseHubRelease
{
    /// <summary>Deterministic release identifier.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Release tag.</summary>
    public string? Tag { get; set; }
    /// <summary>Release display name.</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Release URL.</summary>
    public string? Url { get; set; }
    /// <summary>Release publication time.</summary>
    public DateTimeOffset? PublishedAt { get; set; }
    /// <summary>Release created time.</summary>
    public DateTimeOffset? CreatedAt { get; set; }
    /// <summary>True when release is prerelease.</summary>
    public bool IsPrerelease { get; set; }
    /// <summary>True when release is draft.</summary>
    public bool IsDraft { get; set; }
    /// <summary>True when this is the newest stable release.</summary>
    public bool IsLatestStable { get; set; }
    /// <summary>True when this is the newest prerelease.</summary>
    public bool IsLatestPrerelease { get; set; }
    /// <summary>Body markdown.</summary>
    [JsonPropertyName("body_md")]
    public string? BodyMarkdown { get; set; }
    /// <summary>Release assets.</summary>
    public List<WebReleaseHubAsset> Assets { get; set; } = new();
}

/// <summary>Asset item model.</summary>
public sealed class WebReleaseHubAsset
{
    /// <summary>Deterministic asset identifier.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Asset filename.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Asset browser download URL.</summary>
    public string DownloadUrl { get; set; } = string.Empty;
    /// <summary>Asset size in bytes.</summary>
    public long? Size { get; set; }
    /// <summary>Asset content type.</summary>
    public string? ContentType { get; set; }
    /// <summary>Product identifier (for example: officeimo.word).</summary>
    public string Product { get; set; } = "unknown";
    /// <summary>Channel marker (for example: stable/preview).</summary>
    public string Channel { get; set; } = "stable";
    /// <summary>Platform marker (windows/linux/macos/any).</summary>
    public string Platform { get; set; } = "any";
    /// <summary>Architecture marker (x64/arm64/x86/any).</summary>
    public string Arch { get; set; } = "any";
    /// <summary>Asset kind marker (zip/msi/exe/...)</summary>
    public string Kind { get; set; } = "file";
    /// <summary>Optional SHA256 checksum.</summary>
    public string? Sha256 { get; set; }
}

/// <summary>Product catalog entry.</summary>
public sealed class WebReleaseHubProduct
{
    /// <summary>Product identifier.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Optional ordering hint.</summary>
    public int? Order { get; set; }
}

/// <summary>Input product catalog entry.</summary>
public sealed class WebReleaseHubProductInput
{
    /// <summary>Product identifier.</summary>
    public string? Id { get; set; }
    /// <summary>Display name.</summary>
    public string? Name { get; set; }
    /// <summary>Optional ordering hint.</summary>
    public int? Order { get; set; }
}

/// <summary>Input asset classification rule.</summary>
public sealed class WebReleaseHubAssetRuleInput
{
    /// <summary>Target product identifier.</summary>
    public string? Product { get; set; }
    /// <summary>Optional display label.</summary>
    public string? Label { get; set; }
    /// <summary>Glob patterns for filename matching.</summary>
    public List<string> Match { get; set; } = new();
    /// <summary>Contains match list for filename matching.</summary>
    public List<string> Contains { get; set; } = new();
    /// <summary>Regex pattern for filename matching.</summary>
    public string? Regex { get; set; }
    /// <summary>Forced channel classification.</summary>
    public string? Channel { get; set; }
    /// <summary>Forced platform classification.</summary>
    public string? Platform { get; set; }
    /// <summary>Forced architecture classification.</summary>
    public string? Arch { get; set; }
    /// <summary>Forced kind classification.</summary>
    public string? Kind { get; set; }
    /// <summary>Optional rule priority hint (lower value evaluated first).</summary>
    public int? Priority { get; set; }
}
