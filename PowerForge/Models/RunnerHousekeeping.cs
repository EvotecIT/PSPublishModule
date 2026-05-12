using System;

namespace PowerForge;

/// <summary>
/// Specification for reclaiming disk space on a GitHub Actions runner.
/// </summary>
public sealed class RunnerHousekeepingSpec
{
    /// <summary>
    /// Optional explicit runner temp directory. When omitted, <c>RUNNER_TEMP</c> is used.
    /// </summary>
    public string? RunnerTempPath { get; set; }

    /// <summary>
    /// Optional explicit work root. When omitted, it is derived from the runner temp path or <c>GITHUB_WORKSPACE</c>.
    /// </summary>
    public string? WorkRootPath { get; set; }

    /// <summary>
    /// Optional explicit runner root. When omitted, it is derived from the work root.
    /// </summary>
    public string? RunnerRootPath { get; set; }

    /// <summary>
    /// Optional explicit diagnostics root. When omitted, <c>&lt;runnerRoot&gt;/_diag</c> is used.
    /// </summary>
    public string? DiagnosticsRootPath { get; set; }

    /// <summary>
    /// Optional explicit tool cache path. When omitted, <c>RUNNER_TOOL_CACHE</c> or <c>AGENT_TOOLSDIRECTORY</c> is used.
    /// </summary>
    public string? ToolCachePath { get; set; }

    /// <summary>
    /// Minimum free disk size, in GiB, required after cleanup. Set to <c>null</c> to disable the threshold.
    /// </summary>
    public int? MinFreeGb { get; set; } = 20;

    /// <summary>
    /// Free disk threshold, in GiB, below which aggressive cleanup is enabled.
    /// When omitted, the service uses <c>MinFreeGb + 5</c>.
    /// </summary>
    public int? AggressiveThresholdGb { get; set; }

    /// <summary>
    /// Retention window for runner diagnostics files.
    /// </summary>
    public int DiagnosticsRetentionDays { get; set; } = 14;

    /// <summary>
    /// Retention window for cached GitHub action working sets under <c>_actions</c>.
    /// </summary>
    public int ActionsRetentionDays { get; set; } = 7;

    /// <summary>
    /// Retention window for old repository workspaces under the runner work root. Use <c>0</c> to allow
    /// any age-qualified workspace that is not the active checkout or a runner-internal directory.
    /// </summary>
    public int WorkspacesRetentionDays { get; set; } = 3;

    /// <summary>
    /// Retention window for runner tool cache directories.
    /// </summary>
    public int ToolCacheRetentionDays { get; set; } = 30;

    /// <summary>
    /// When true, only plans deletions and external commands without executing them.
    /// </summary>
    public bool DryRun { get; set; } = true;

    /// <summary>
    /// Forces aggressive cleanup even when free disk is above the threshold.
    /// </summary>
    public bool Aggressive { get; set; }

    /// <summary>
    /// When true, runner diagnostics cleanup is enabled.
    /// </summary>
    public bool CleanDiagnostics { get; set; } = true;

    /// <summary>
    /// When true, runner temp contents are cleaned.
    /// </summary>
    public bool CleanRunnerTemp { get; set; } = true;

    /// <summary>
    /// When true, GitHub actions working sets under <c>_actions</c> are cleaned during aggressive cleanup.
    /// </summary>
    public bool CleanActionsCache { get; set; } = true;

    /// <summary>
    /// When true, old top-level repository workspaces under the runner work root are cleaned. This is opt-in
    /// for direct API and CLI callers; checked-in housekeeping configs can enable it explicitly.
    /// </summary>
    public bool CleanWorkspaces { get; set; }

    /// <summary>
    /// When true, runner tool cache directories are cleaned during aggressive cleanup.
    /// </summary>
    public bool CleanToolCache { get; set; } = true;

    /// <summary>
    /// When true, <c>dotnet nuget locals all --clear</c> is executed during aggressive cleanup.
    /// </summary>
    public bool ClearDotNetCaches { get; set; } = true;

    /// <summary>
    /// When true, <c>docker system prune</c> is executed during aggressive cleanup.
    /// </summary>
    public bool PruneDocker { get; set; } = true;

    /// <summary>
    /// When true, Docker volumes are included in the prune operation.
    /// </summary>
    public bool IncludeDockerVolumes { get; set; } = true;

    /// <summary>
    /// When true, the service may use <c>sudo</c> for directory deletion on Unix when direct deletion fails.
    /// </summary>
    public bool AllowSudo { get; set; }
}

/// <summary>
/// Single cleanup step included in runner housekeeping output.
/// </summary>
public sealed class RunnerHousekeepingStepResult
{
    /// <summary>
    /// Stable step identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable step title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Whether the step was skipped.
    /// </summary>
    public bool Skipped { get; set; }

    /// <summary>
    /// Whether the step was a dry-run preview.
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// Whether the step completed successfully.
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Optional step message.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Number of filesystem entries deleted or planned.
    /// </summary>
    public int EntriesAffected { get; set; }

    /// <summary>
    /// Optional command text executed by the step.
    /// </summary>
    public string? Command { get; set; }

    /// <summary>
    /// Process exit code when the step runs an external command.
    /// </summary>
    public int? ExitCode { get; set; }

    /// <summary>
    /// Filesystem paths touched or planned by the step.
    /// </summary>
    public string[] Targets { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Result summary for a runner housekeeping run.
/// </summary>
public sealed class RunnerHousekeepingResult
{
    /// <summary>
    /// Runner root used by the cleanup run.
    /// </summary>
    public string RunnerRootPath { get; set; } = string.Empty;

    /// <summary>
    /// Work root used by the cleanup run.
    /// </summary>
    public string WorkRootPath { get; set; } = string.Empty;

    /// <summary>
    /// Runner temp path used by the cleanup run.
    /// </summary>
    public string RunnerTempPath { get; set; } = string.Empty;

    /// <summary>
    /// Optional diagnostics root used by the cleanup run.
    /// </summary>
    public string? DiagnosticsRootPath { get; set; }

    /// <summary>
    /// Optional tool cache path used by the cleanup run.
    /// </summary>
    public string? ToolCachePath { get; set; }

    /// <summary>
    /// Free disk before cleanup, in bytes.
    /// </summary>
    public long FreeBytesBefore { get; set; }

    /// <summary>
    /// Free disk after cleanup, in bytes.
    /// </summary>
    public long FreeBytesAfter { get; set; }

    /// <summary>
    /// Minimum required free disk after cleanup, in bytes.
    /// </summary>
    public long? RequiredFreeBytes { get; set; }

    /// <summary>
    /// Aggressive threshold used by the run, in bytes.
    /// </summary>
    public long? AggressiveThresholdBytes { get; set; }

    /// <summary>
    /// Whether aggressive cleanup was executed.
    /// </summary>
    public bool AggressiveApplied { get; set; }

    /// <summary>
    /// Whether run executed in dry-run mode.
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// Whether the run completed successfully.
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Optional warning or error message.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Detailed step results.
    /// </summary>
    public RunnerHousekeepingStepResult[] Steps { get; set; } = Array.Empty<RunnerHousekeepingStepResult>();
}
