namespace PowerForge;

/// <summary>
/// Host-facing request for executing or planning a project build using a <c>project.build.json</c> file.
/// </summary>
public sealed class ProjectBuildHostRequest
{
    /// <summary>
    /// Path to the <c>project.build.json</c> configuration file.
    /// </summary>
    public string ConfigPath { get; set; } = string.Empty;

    /// <summary>
    /// Optional path where the generated plan JSON should be written.
    /// </summary>
    public string? PlanOutputPath { get; set; }

    /// <summary>
    /// When true, executes the real build path after the planning pass.
    /// When false, only the what-if/plan pass runs.
    /// </summary>
    public bool ExecuteBuild { get; set; }

    /// <summary>
    /// Optional override for plan-only mode.
    /// </summary>
    public bool? PlanOnly { get; set; }

    /// <summary>
    /// Optional override for version updates.
    /// </summary>
    public bool? UpdateVersions { get; set; }

    /// <summary>
    /// Optional override for building/packing projects.
    /// </summary>
    public bool? Build { get; set; }

    /// <summary>
    /// Optional override for NuGet publishing.
    /// </summary>
    public bool? PublishNuget { get; set; }

    /// <summary>
    /// Optional override for GitHub release publishing.
    /// </summary>
    public bool? PublishGitHub { get; set; }
}
