using PowerForgeStudio.Domain.Workspace;

namespace PowerForgeStudio.Orchestrator.Workspace;

/// <summary>Builds a review-only inventory of primary checkouts and registered worktrees.</summary>
public interface IWorkspaceStorageInspectionService
{
    Task<WorkspaceStorageSnapshot> InspectAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default);

    Task<WorkspaceStorageSnapshot> InspectAsync(
        string workspaceRoot,
        IProgress<WorkspaceStorageScanProgress>? progress,
        CancellationToken cancellationToken = default)
        => InspectAsync(workspaceRoot, cancellationToken);
}
