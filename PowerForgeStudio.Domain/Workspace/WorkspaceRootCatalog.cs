namespace PowerForgeStudio.Domain.Workspace;

public sealed record WorkspaceRootCatalog(
    string ActiveWorkspaceRoot,
    IReadOnlyList<string> RecentWorkspaceRoots,
    string? ActiveProfileId,
    IReadOnlyList<WorkspaceProfile> Profiles,
    IReadOnlyList<WorkspaceProfileTemplate>? Templates = null);
