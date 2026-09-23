using PowerForgeStudio.Domain.Workspace;

namespace PowerForgeStudio.Orchestrator.Workspace;

/// <summary>Persists explorer state without exposing profile or release behavior to a presentation host.</summary>
public interface IWorkspaceExplorerStateStore
{
    WorkspaceExplorerState LoadExplorer(string workspaceRoot);
    WorkspaceExplorerState SetFavorite(string workspaceRoot, string projectRoot, bool favorite);
    WorkspaceExplorerState SaveSession(string workspaceRoot, IReadOnlyList<WorkspaceDocumentReference> documents,
        WorkspaceDocumentReference? activeDocument, IReadOnlyList<string> expandedPaths,
        IReadOnlyList<string>? collapsedBuildPaths = null);
}
