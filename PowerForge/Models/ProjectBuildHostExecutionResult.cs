namespace PowerForge;

/// <summary>
/// Result returned by the host-facing project build service.
/// </summary>
public sealed class ProjectBuildHostExecutionResult
{
    /// <summary>
    /// True when the requested operation completed successfully.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Optional error message when <see cref="Success"/> is false.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Full path to the configuration file used for this execution.
    /// </summary>
    public string ConfigPath { get; set; } = string.Empty;

    /// <summary>
    /// Resolved project/repository root used by the release workflow.
    /// </summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>
    /// Resolved staging path, when configured.
    /// </summary>
    public string? StagingPath { get; set; }

    /// <summary>
    /// Resolved package output path, when configured.
    /// </summary>
    public string? OutputPath { get; set; }

    /// <summary>
    /// Resolved release-zip output path, when configured.
    /// </summary>
    public string? ReleaseZipOutputPath { get; set; }

    /// <summary>
    /// Resolved plan output path, when configured.
    /// </summary>
    public string? PlanOutputPath { get; set; }

    /// <summary>
    /// Duration of the host operation.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Underlying project build result.
    /// </summary>
    public ProjectBuildResult Result { get; set; } = new();
}
