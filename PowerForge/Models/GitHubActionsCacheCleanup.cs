using System;

namespace PowerForge;

/// <summary>
/// Specification for pruning GitHub Actions dependency caches in a repository.
/// </summary>
public sealed class GitHubActionsCacheCleanupSpec
{
    /// <summary>
    /// GitHub API base URL (for example <c>https://api.github.com/</c>).
    /// </summary>
    public string? ApiBaseUrl { get; set; }

    /// <summary>
    /// Repository in <c>owner/repo</c> format.
    /// </summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>
    /// GitHub token used to access the Actions caches API.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Cache key patterns to include (wildcards and <c>re:</c> regex supported).
    /// When empty, all cache keys are eligible.
    /// </summary>
    public string[] IncludeKeys { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Cache key patterns to exclude (wildcards and <c>re:</c> regex supported).
    /// </summary>
    public string[] ExcludeKeys { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Number of newest caches to keep per cache key.
    /// </summary>
    public int KeepLatestPerKey { get; set; } = 1;

    /// <summary>
    /// Minimum cache age in days before deletion is allowed.
    /// Set to <c>null</c> (or less than 1) to disable age filtering.
    /// </summary>
    public int? MaxAgeDays { get; set; } = 14;

    /// <summary>
    /// Maximum number of caches to delete in a single run.
    /// </summary>
    public int MaxDelete { get; set; } = 200;

    /// <summary>
    /// Number of caches returned per GitHub API page (1-100).
    /// </summary>
    public int PageSize { get; set; } = 100;

    /// <summary>
    /// When true, only plans deletions and does not call delete endpoints.
    /// </summary>
    public bool DryRun { get; set; } = true;

    /// <summary>
    /// When true, the run is marked failed when any delete request fails.
    /// </summary>
    public bool FailOnDeleteError { get; set; }
}

/// <summary>
/// Single cache record included in cleanup output.
/// </summary>
public sealed class GitHubActionsCacheCleanupItem
{
    /// <summary>
    /// GitHub cache identifier.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Cache key.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Git reference associated with the cache.
    /// </summary>
    public string? Ref { get; set; }

    /// <summary>
    /// Cache version fingerprint reported by GitHub.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Size in bytes.
    /// </summary>
    public long SizeInBytes { get; set; }

    /// <summary>
    /// Creation timestamp (UTC) when available.
    /// </summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>
    /// Last access timestamp (UTC) when available.
    /// </summary>
    public DateTimeOffset? LastAccessedAt { get; set; }

    /// <summary>
    /// Reason this item was selected (or failed) in cleanup output.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// HTTP status code from delete operation (when available).
    /// </summary>
    public int? DeleteStatusCode { get; set; }

    /// <summary>
    /// Error message from delete operation (when available).
    /// </summary>
    public string? DeleteError { get; set; }
}

/// <summary>
/// Repository cache usage snapshot returned by GitHub.
/// </summary>
public sealed class GitHubActionsCacheUsage
{
    /// <summary>
    /// Total cache count reported by GitHub.
    /// </summary>
    public int ActiveCachesCount { get; set; }

    /// <summary>
    /// Total cache size in bytes reported by GitHub.
    /// </summary>
    public long ActiveCachesSizeInBytes { get; set; }
}

/// <summary>
/// Result summary for a GitHub Actions cache cleanup run.
/// </summary>
public sealed class GitHubActionsCacheCleanupResult
{
    /// <summary>
    /// Repository in <c>owner/repo</c> format.
    /// </summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>
    /// Effective include key patterns used by the run.
    /// </summary>
    public string[] IncludeKeys { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Effective exclude key patterns used by the run.
    /// </summary>
    public string[] ExcludeKeys { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Number of newest caches kept per cache key.
    /// </summary>
    public int KeepLatestPerKey { get; set; }

    /// <summary>
    /// Minimum age requirement (days) applied during selection.
    /// </summary>
    public int? MaxAgeDays { get; set; }

    /// <summary>
    /// Maximum number of deletions allowed by this run.
    /// </summary>
    public int MaxDelete { get; set; }

    /// <summary>
    /// Whether run executed in dry-run mode.
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// Cache usage reported by GitHub before cleanup started.
    /// </summary>
    public GitHubActionsCacheUsage? UsageBefore { get; set; }

    /// <summary>
    /// Total caches scanned from GitHub.
    /// </summary>
    public int ScannedCaches { get; set; }

    /// <summary>
    /// Caches matching include/exclude filters.
    /// </summary>
    public int MatchedCaches { get; set; }

    /// <summary>
    /// Number of caches skipped because they are in the newest keep window.
    /// </summary>
    public int KeptByRecentWindow { get; set; }

    /// <summary>
    /// Number of caches skipped because they are newer than the age threshold.
    /// </summary>
    public int KeptByAgeThreshold { get; set; }

    /// <summary>
    /// Number of caches planned for deletion.
    /// </summary>
    public int PlannedDeletes { get; set; }

    /// <summary>
    /// Total size in bytes of planned deletions.
    /// </summary>
    public long PlannedDeleteBytes { get; set; }

    /// <summary>
    /// Number of caches successfully deleted.
    /// </summary>
    public int DeletedCaches { get; set; }

    /// <summary>
    /// Total size in bytes of successfully deleted caches.
    /// </summary>
    public long DeletedBytes { get; set; }

    /// <summary>
    /// Number of caches that failed deletion.
    /// </summary>
    public int FailedDeletes { get; set; }

    /// <summary>
    /// Whether the run completed successfully.
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Optional warning or non-fatal message.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Caches selected for deletion.
    /// </summary>
    public GitHubActionsCacheCleanupItem[] Planned { get; set; } = Array.Empty<GitHubActionsCacheCleanupItem>();

    /// <summary>
    /// Caches successfully deleted.
    /// </summary>
    public GitHubActionsCacheCleanupItem[] Deleted { get; set; } = Array.Empty<GitHubActionsCacheCleanupItem>();

    /// <summary>
    /// Caches that failed deletion.
    /// </summary>
    public GitHubActionsCacheCleanupItem[] Failed { get; set; } = Array.Empty<GitHubActionsCacheCleanupItem>();
}
