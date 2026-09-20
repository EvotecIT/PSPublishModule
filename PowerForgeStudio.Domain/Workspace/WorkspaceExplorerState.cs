namespace PowerForgeStudio.Domain.Workspace;

/// <summary>A local document identity; document contents and credentials are never persisted here.</summary>
public sealed record WorkspaceDocumentReference(string WorkingCopyRoot, string Path);

/// <summary>Machine-local explorer preferences stored alongside the existing workspace catalog.</summary>
public sealed record WorkspaceExplorerState(
    string WorkspaceRoot,
    IReadOnlyList<string> FavoriteProjectRoots,
    IReadOnlyList<WorkspaceDocumentReference> OpenDocuments,
    WorkspaceDocumentReference? ActiveDocument,
    IReadOnlyList<string> ExpandedPaths);
