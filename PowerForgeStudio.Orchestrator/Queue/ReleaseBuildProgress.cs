using PowerForge;
using PowerForgeStudio.Orchestrator.Host;

namespace PowerForgeStudio.Orchestrator.Queue;

/// <summary>A real build phase or work-item update from the shared execution engine.</summary>
public sealed record ReleaseBuildProgress(string Phase, string State, string Detail);

internal sealed class ReleaseBuildProgressAdapter(IProgress<ReleaseBuildProgress>? progress)
    : IProjectBuildProgressReporterV2, IPowerForgeReleaseProgressReporterV2
{
    private void Report(string phase, string state, string? detail)
    {
        progress?.Report(new ReleaseBuildProgress(phase, state, StudioOutputSanitizer.Sanitize(detail)));
    }

    public void PhaseStarted(ProjectBuildProgressPhase phase, int totalItems, string? detail = null) => Report(phase.ToString(), "Started", detail);
    public void PhaseUpdated(ProjectBuildProgressPhase phase, int completedItems, int totalItems, string? detail = null) => Report(phase.ToString(), $"{completedItems}/{totalItems}", detail);
    public void PhaseCompleted(ProjectBuildProgressPhase phase, string? detail = null) => Report(phase.ToString(), "Completed", detail);
    public void PhaseFailed(ProjectBuildProgressPhase phase, string? detail = null) => Report(phase.ToString(), "Failed", detail);
    public void ItemsPlanned(ProjectBuildProgressPhase phase, IReadOnlyList<ProjectBuildProgressItem> items) => Report(phase.ToString(), "Planned", $"{items.Count} work items");
    public void ItemUpdated(ProjectBuildProgressItem item, ProjectBuildProgressItemState state, string? detail = null) => Report(item.Phase.ToString(), state.ToString(), $"{item.Title}: {detail}");
    public void PhaseStarted(PowerForgeReleaseProgressPhase phase, int totalItems, string? detail = null) => Report(phase.ToString(), "Started", detail);
    public void PhaseCompleted(PowerForgeReleaseProgressPhase phase, string? detail = null) => Report(phase.ToString(), "Completed", detail);
    public void PhaseFailed(PowerForgeReleaseProgressPhase phase, string? detail = null) => Report(phase.ToString(), "Failed", detail);
    public void ItemsPlanned(PowerForgeReleaseProgressPhase phase, IReadOnlyList<PowerForgeReleaseProgressItem> items) => Report(phase.ToString(), "Planned", $"{items.Count} work items");
    public void ItemUpdated(PowerForgeReleaseProgressItem item, PowerForgeReleaseProgressItemState state, string? detail = null) => Report(item.Phase.ToString(), state.ToString(), $"{item.Title}: {detail}");
}
