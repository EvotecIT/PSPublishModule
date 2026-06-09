using PowerForgeStudio.Domain.Catalog;

namespace PowerForgeStudio.Domain.Portfolio;

public sealed record RepositoryWorkspaceFamilyLaneItem(
    string RootPath,
    string RepositoryName,
    ReleaseWorkspaceKind WorkspaceKind,
    string LaneKey,
    string LaneDisplay,
    string Detail,
    string ReadinessDisplay,
    int SortOrder)
{
    public string WorkspaceDisplay => WorkspaceKind.ToString();
}

