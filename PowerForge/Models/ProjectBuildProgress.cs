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
