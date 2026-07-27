using System;
using System.Collections.Generic;

namespace PowerForge;

/// <summary>
/// Stable phases reported by the JSON-driven project build workflow.
/// </summary>
public enum ProjectBuildProgressPhase
{
    /// <summary>Discover projects, resolve the release plan, and validate inputs.</summary>
    Plan,
    /// <summary>Resolve and optionally update project versions.</summary>
    Versioning,
    /// <summary>Build assemblies, create packages, and create release archives.</summary>
    PackageBuild,
    /// <summary>Sign produced NuGet packages.</summary>
    PackageSigning,
    /// <summary>Publish package artifacts to the configured NuGet feed.</summary>
    NuGetPublish,
    /// <summary>Create or update the configured GitHub release.</summary>
    GitHubPublish
}

/// <summary>
/// Receives structured project-build progress without depending on a particular console renderer.
/// </summary>
public interface IProjectBuildProgressReporter
{
    /// <summary>Marks a phase as started.</summary>
    void PhaseStarted(ProjectBuildProgressPhase phase, int totalItems, string? detail = null);

    /// <summary>Updates the completed item count and current detail for a phase.</summary>
    void PhaseUpdated(ProjectBuildProgressPhase phase, int completedItems, int totalItems, string? detail = null);

    /// <summary>Marks a phase as completed.</summary>
    void PhaseCompleted(ProjectBuildProgressPhase phase, string? detail = null);

    /// <summary>Marks a phase as failed.</summary>
    void PhaseFailed(ProjectBuildProgressPhase phase, string? detail = null);
}

/// <summary>State of a concrete project-build work item.</summary>
public enum ProjectBuildProgressItemState
{
    /// <summary>The item has not started.</summary>
    Planned,
    /// <summary>The item is currently running.</summary>
    Started,
    /// <summary>The item completed successfully.</summary>
    Completed,
    /// <summary>The item failed.</summary>
    Failed,
    /// <summary>The item was not required or was cancelled.</summary>
    Skipped
}

/// <summary>
/// Describes one durable work item within a project-build phase.
/// </summary>
public sealed class ProjectBuildProgressItem
{
    /// <summary>Owning project-build phase.</summary>
    public ProjectBuildProgressPhase Phase { get; set; }

    /// <summary>Stable key within the owning phase.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Human-friendly item title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional item kind used by presentation hosts.</summary>
    public string? Kind { get; set; }

    /// <summary>One-based position in the owning phase.</summary>
    public int Position { get; set; }

    /// <summary>Total number of items in the owning phase.</summary>
    public int Total { get; set; }

    /// <summary>Measured duration after the item reaches a terminal state.</summary>
    public TimeSpan? Duration { get; set; }
}

/// <summary>
/// Optional detailed project-build progress contract. Hosts implementing this interface
/// receive individual project work items in addition to aggregate phase progress.
/// </summary>
public interface IProjectBuildProgressReporterV2 : IProjectBuildProgressReporter
{
    /// <summary>Registers planned work items for a project-build phase.</summary>
    void ItemsPlanned(
        ProjectBuildProgressPhase phase,
        IReadOnlyList<ProjectBuildProgressItem> items);

    /// <summary>Updates one concrete project-build work item.</summary>
    void ItemUpdated(
        ProjectBuildProgressItem item,
        ProjectBuildProgressItemState state,
        string? detail = null);
}
