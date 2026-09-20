using PowerForgeStudio.Domain.Workspace;

namespace PowerForgeStudio.Orchestrator.Workspace;

/// <summary>Reviews and removes one registered linked worktree using exact, refreshed evidence.</summary>
public interface IWorkspaceStorageRemovalService
{
    Task<WorkspaceStorageRemovalReview> ReviewAsync(
        string workspaceRoot,
        WorkspaceStorageEntry entry,
        IReadOnlyCollection<string> protectedWorkingCopies,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(
        WorkspaceStorageRemovalReview reviewed,
        IReadOnlyCollection<string> protectedWorkingCopies,
        bool confirmNoExternalUse,
        bool confirmRetainedArtifacts,
        CancellationToken cancellationToken = default);
}
