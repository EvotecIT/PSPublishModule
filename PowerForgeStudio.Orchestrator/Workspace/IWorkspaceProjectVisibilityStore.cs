using PowerForgeStudio.Domain.Workspace;

namespace PowerForgeStudio.Orchestrator.Workspace;

/// <summary>Persists reversible local project visibility without changing repository files.</summary>
public interface IWorkspaceProjectVisibilityStore
{
    WorkspaceExplorerState SetProjectArchived(string workspaceRoot, string projectRoot, bool archived);
}
