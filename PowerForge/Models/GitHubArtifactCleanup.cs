using System;

namespace PowerForge;

/// <summary>
/// Specification for pruning GitHub Actions artifacts in a repository.
/// </summary>
public sealed class GitHubArtifactCleanupSpec
{
    /// <summary>
    /// Repository in <c>owner/repo</c> format.
    /// </summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>
    /// GitHub token used to access the Actions artifacts API.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Artifact name patterns to include (wildcards and <c>re:</c> regex supported).
    /// When empty, engine defaults are applied.
    /// </summary>
    public string[] IncludeNames { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Artifact name patterns to exclude (wildcards and <c>re:</c> regex supported).
    /// </summary>
    public string[] ExcludeNames { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Number of newest artifacts to keep per artifact name.
    /// </summary>
    public int KeepLatestPerName { get; set; } = 5;

    /// <summary>
    /// Minimum artifact age in days before deletion is allowed.
    /// Set to <c>null</c> (or less than 1) to disable age filtering.
    /// </summary>
    public int? MaxAgeDays { get; set; } = 7;

    /// <summary>
    /// Maximum number of artifacts to delete in a single run.
    /// </summary>
    public int MaxDelete { get; set; } = 200;

    /// <summary>
    /// Number of artifacts returned per GitHub API page (1-100).
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
/// Single artifact record included in cleanup output.
/// </summary>
public sealed class GitHubArtifactCleanupItem
{
    /// <summary>
    /// GitHub artifact identifier.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Artifact name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Size in bytes.
    /// </summary>
    public long SizeInBytes { get; set; }

    /// <summary>
    /// Creation timestamp (UTC) when available.
    /// </summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>
    /// Last update timestamp (UTC) when available.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Whether GitHub marked this artifact as expired.
    /// </summary>
    public bool Expired { get; set; }

    /// <summary>
    /// Optional workflow run ID associated with the artifact.
    /// </summary>
    public long? WorkflowRunId { get; set; }

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
/// Result summary for a GitHub artifact cleanup run.
/// </summary>
public sealed class GitHubArtifactCleanupResult
{
    /// <summary>
    /// Repository in <c>owner/repo</c> format.
    /// </summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>
    /// Effective include patterns used by the run.
    /// </summary>
    public string[] IncludeNames { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Effective exclude patterns used by the run.
    /// </summary>
    public string[] ExcludeNames { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Number of newest artifacts kept per name.
    /// </summary>
    public int KeepLatestPerName { get; set; }

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
    /// Total artifacts scanned from GitHub.
    /// </summary>
    public int ScannedArtifacts { get; set; }

    /// <summary>
    /// Artifacts matching include/exclude filters.
    /// </summary>
    public int MatchedArtifacts { get; set; }

    /// <summary>
    /// Number of artifacts skipped because they are in the newest keep window.
    /// </summary>
    public int KeptByRecentWindow { get; set; }

    /// <summary>
    /// Number of artifacts skipped because they are newer than the age threshold.
    /// </summary>
    public int KeptByAgeThreshold { get; set; }

    /// <summary>
    /// Number of artifacts planned for deletion.
    /// </summary>
    public int PlannedDeletes { get; set; }

    /// <summary>
    /// Total size in bytes of planned deletions.
    /// </summary>
    public long PlannedDeleteBytes { get; set; }

    /// <summary>
    /// Number of artifacts successfully deleted.
    /// </summary>
    public int DeletedArtifacts { get; set; }

    /// <summary>
    /// Total size in bytes of successfully deleted artifacts.
    /// </summary>
    public long DeletedBytes { get; set; }

    /// <summary>
    /// Number of artifacts that failed deletion.
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
    /// Artifacts selected for deletion.
    /// </summary>
    public GitHubArtifactCleanupItem[] Planned { get; set; } = Array.Empty<GitHubArtifactCleanupItem>();

    /// <summary>
    /// Artifacts successfully deleted.
    /// </summary>
    public GitHubArtifactCleanupItem[] Deleted { get; set; } = Array.Empty<GitHubArtifactCleanupItem>();

    /// <summary>
    /// Artifacts that failed deletion.
    /// </summary>
    public GitHubArtifactCleanupItem[] Failed { get; set; } = Array.Empty<GitHubArtifactCleanupItem>();
}
