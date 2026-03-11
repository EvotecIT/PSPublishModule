using PowerForgeStudio.Domain.Workspace;

namespace PowerForgeStudio.Orchestrator.Workspace;

public interface IWorkspaceSnapshotService
{
    Task<WorkspaceSnapshot> RefreshAsync(
        string workspaceRoot,
        string databasePath,
        WorkspaceRefreshOptions? options = null,
        CancellationToken cancellationToken = default);
}
