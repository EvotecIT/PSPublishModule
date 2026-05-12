using System;

namespace PowerForge;

/// <summary>
/// Top-level configuration for GitHub housekeeping runs.
/// </summary>
public sealed class GitHubHousekeepingSpec
{
    /// <summary>
    /// Optional GitHub API base URL (for example <c>https://api.github.com/</c>).
    /// </summary>
    public string? ApiBaseUrl { get; set; }

    /// <summary>
    /// Repository in <c>owner/repo</c> format.
    /// </summary>
    public string? Repository { get; set; }

    /// <summary>
    /// Optional GitHub token used for artifact/cache cleanup.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Environment variable name used to resolve the GitHub token when <see cref="Token"/> is not provided.
    /// </summary>
    public string TokenEnvName { get; set; } = "GITHUB_TOKEN";

    /// <summary>
    /// When true, only plans deletions and does not execute them.
    /// </summary>
    public bool DryRun { get; set; } = true;

    /// <summary>
    /// Artifact cleanup settings.
    /// </summary>
    public GitHubHousekeepingArtifactSpec Artifacts { get; set; } = new();

    /// <summary>
    /// Cache cleanup settings.
    /// </summary>
    public GitHubHousekeepingCacheSpec Caches { get; set; } = new();

    /// <summary>
    /// Runner cleanup settings.
    /// </summary>
    public GitHubHousekeepingRunnerSpec Runner { get; set; } = new();
}

/// <summary>
/// Artifact cleanup configuration for a housekeeping run.
/// </summary>
public sealed class GitHubHousekeepingArtifactSpec
{
    /// <summary>
    /// Whether artifact cleanup is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Artifact name patterns to include.
    /// </summary>
    public string[] IncludeNames { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Artifact name patterns to exclude.
    /// </summary>
    public string[] ExcludeNames { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Number of newest artifacts kept per artifact name.
    /// </summary>
    public int KeepLatestPerName { get; set; } = 5;

    /// <summary>
    /// Minimum age in days before deletion is allowed.
    /// </summary>
    public int? MaxAgeDays { get; set; } = 7;

    /// <summary>
    /// Maximum number of artifacts to delete in one run.
    /// </summary>
    public int MaxDelete { get; set; } = 200;

    /// <summary>
    /// Number of API records requested per page.
    /// </summary>
    public int PageSize { get; set; } = 100;

    /// <summary>
    /// When true, the run fails if an artifact delete request fails.
    /// </summary>
    public bool FailOnDeleteError { get; set; }
}

/// <summary>
/// Cache cleanup configuration for a housekeeping run.
/// </summary>
public sealed class GitHubHousekeepingCacheSpec
{
    /// <summary>
    /// Whether cache cleanup is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Cache key patterns to include.
    /// </summary>
    public string[] IncludeKeys { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Cache key patterns to exclude.
    /// </summary>
    public string[] ExcludeKeys { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Number of newest caches kept per cache key.
    /// </summary>
    public int KeepLatestPerKey { get; set; } = 1;

    /// <summary>
    /// Minimum age in days before deletion is allowed.
    /// </summary>
    public int? MaxAgeDays { get; set; } = 14;

    /// <summary>
    /// Maximum number of caches to delete in one run.
    /// </summary>
    public int MaxDelete { get; set; } = 200;

    /// <summary>
    /// Number of API records requested per page.
    /// </summary>
    public int PageSize { get; set; } = 100;

    /// <summary>
    /// When true, the run fails if a cache delete request fails.
    /// </summary>
    public bool FailOnDeleteError { get; set; }
}

/// <summary>
/// Runner cleanup configuration for a housekeeping run.
/// </summary>
public sealed class GitHubHousekeepingRunnerSpec
{
    /// <summary>
    /// Whether runner cleanup is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Optional explicit runner temp directory.
    /// </summary>
    public string? RunnerTempPath { get; set; }

    /// <summary>
    /// Optional explicit work root.
    /// </summary>
    public string? WorkRootPath { get; set; }

    /// <summary>
    /// Optional explicit runner root.
    /// </summary>
    public string? RunnerRootPath { get; set; }

    /// <summary>
    /// Optional explicit diagnostics root.
    /// </summary>
    public string? DiagnosticsRootPath { get; set; }

    /// <summary>
    /// Optional explicit tool cache path.
    /// </summary>
    public string? ToolCachePath { get; set; }

    /// <summary>
    /// Minimum free disk required after cleanup, in GiB.
    /// </summary>
    public int? MinFreeGb { get; set; } = 20;

    /// <summary>
    /// Threshold below which aggressive cleanup is enabled, in GiB.
    /// </summary>
    public int? AggressiveThresholdGb { get; set; }

    /// <summary>
    /// Retention window for diagnostics files.
    /// </summary>
    public int DiagnosticsRetentionDays { get; set; } = 14;

    /// <summary>
    /// Retention window for action working sets.
    /// </summary>
    public int ActionsRetentionDays { get; set; } = 7;

    /// <summary>
    /// Retention window for old repository workspaces. Use <c>0</c> to allow any age-qualified workspace
    /// that is not the active checkout or a runner-internal directory.
    /// </summary>
    public int WorkspacesRetentionDays { get; set; } = 3;

    /// <summary>
    /// Retention window for tool cache directories.
    /// </summary>
    public int ToolCacheRetentionDays { get; set; } = 30;

    /// <summary>
    /// Forces aggressive cleanup.
    /// </summary>
    public bool Aggressive { get; set; }

    /// <summary>
    /// Enables diagnostics cleanup.
    /// </summary>
    public bool CleanDiagnostics { get; set; } = true;

    /// <summary>
    /// Enables runner temp cleanup.
    /// </summary>
    public bool CleanRunnerTemp { get; set; } = true;

    /// <summary>
    /// Enables action working set cleanup.
    /// </summary>
    public bool CleanActionsCache { get; set; } = true;

    /// <summary>
    /// Enables old repository workspace cleanup. This is opt-in and should be enabled explicitly in runner
    /// housekeeping configs that own a self-hosted runner work root.
    /// </summary>
    public bool CleanWorkspaces { get; set; }

    /// <summary>
    /// Enables runner tool cache cleanup.
    /// </summary>
    public bool CleanToolCache { get; set; } = true;

    /// <summary>
    /// Enables <c>dotnet nuget locals all --clear</c>.
    /// </summary>
    public bool ClearDotNetCaches { get; set; } = true;

    /// <summary>
    /// Enables Docker prune.
    /// </summary>
    public bool PruneDocker { get; set; } = true;

    /// <summary>
    /// Includes Docker volumes during prune.
    /// </summary>
    public bool IncludeDockerVolumes { get; set; } = true;

    /// <summary>
    /// Allows sudo on Unix for protected directories.
    /// </summary>
    public bool AllowSudo { get; set; }
}

/// <summary>
/// Aggregate result for a GitHub housekeeping run.
/// </summary>
public sealed class GitHubHousekeepingResult
{
    /// <summary>
    /// Repository used for remote GitHub cleanup.
    /// </summary>
    public string? Repository { get; set; }

    /// <summary>
    /// Whether the run executed in dry-run mode.
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// Whether the overall housekeeping run succeeded.
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Optional warning or error message.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Enabled sections in evaluation order.
    /// </summary>
    public string[] RequestedSections { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Sections that completed successfully.
    /// </summary>
    public string[] CompletedSections { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Sections that failed.
    /// </summary>
    public string[] FailedSections { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Artifact cleanup result when artifact cleanup is enabled.
    /// </summary>
    public GitHubArtifactCleanupResult? Artifacts { get; set; }

    /// <summary>
    /// Cache cleanup result when cache cleanup is enabled.
    /// </summary>
    public GitHubActionsCacheCleanupResult? Caches { get; set; }

    /// <summary>
    /// Runner cleanup result when runner cleanup is enabled.
    /// </summary>
    public RunnerHousekeepingResult? Runner { get; set; }
}
