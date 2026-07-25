namespace PowerForgeStudio.Domain.Catalog;

public sealed record RepositoryCatalogEntry(
    string Name,
    string RootPath,
    ReleaseRepositoryKind RepositoryKind,
    ReleaseWorkspaceKind WorkspaceKind,
    string? ModuleBuildScriptPath,
    string? ProjectBuildScriptPath,
    bool IsWorktree,
    bool HasWebsiteSignals,
    string? UnifiedReleaseConfigPath = null)
{
    public bool IsReleaseManaged => UnifiedReleaseConfigPath is not null || ModuleBuildScriptPath is not null || ProjectBuildScriptPath is not null;

    public string? PrimaryBuildScriptPath => UnifiedReleaseConfigPath ?? ModuleBuildScriptPath ?? ProjectBuildScriptPath;
}

