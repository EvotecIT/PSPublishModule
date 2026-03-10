namespace ReleaseOpsStudio.Domain.Catalog;

public sealed record RepositoryCatalogEntry(
    string Name,
    string RootPath,
    ReleaseRepositoryKind RepositoryKind,
    ReleaseWorkspaceKind WorkspaceKind,
    string? ModuleBuildScriptPath,
    string? ProjectBuildScriptPath,
    bool IsWorktree,
    bool HasWebsiteSignals)
{
    public bool IsReleaseManaged => ModuleBuildScriptPath is not null || ProjectBuildScriptPath is not null;

    public string? PrimaryBuildScriptPath => ModuleBuildScriptPath ?? ProjectBuildScriptPath;
}
