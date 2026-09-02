namespace PowerForge;

internal sealed class ProjectBuildPlanProgressReporter : IProjectBuildProgressReporterV2
{
    private readonly IProjectBuildProgressReporter _inner;

    internal ProjectBuildPlanProgressReporter(IProjectBuildProgressReporter inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public void PhaseStarted(ProjectBuildProgressPhase phase, int totalItems, string? detail = null)
    {
    }

    public void PhaseUpdated(
        ProjectBuildProgressPhase phase,
        int completedItems,
        int totalItems,
        string? detail = null)
    {
        if (phase == ProjectBuildProgressPhase.Plan)
            _inner.PhaseUpdated(phase, completedItems, totalItems, detail);
    }

    public void PhaseCompleted(ProjectBuildProgressPhase phase, string? detail = null)
    {
    }

    public void PhaseFailed(ProjectBuildProgressPhase phase, string? detail = null)
    {
    }

    public void ItemsPlanned(ProjectBuildProgressPhase phase, IReadOnlyList<ProjectBuildProgressItem> items)
    {
    }

    public void ItemUpdated(
        ProjectBuildProgressItem item,
        ProjectBuildProgressItemState state,
        string? detail = null)
    {
    }
}
