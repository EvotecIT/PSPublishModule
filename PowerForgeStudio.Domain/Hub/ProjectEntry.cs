using PowerForgeStudio.Domain.Catalog;

namespace PowerForgeStudio.Domain.Hub;

public sealed record ProjectEntry(
    string Name,
    string RootPath,
    string? GitHubSlug,
    string? AzureDevOpsSlug,
    ProjectCategory Category,
    ProjectKind Kind,
    ReleaseRepositoryKind RepositoryKind,
    ReleaseWorkspaceKind WorkspaceKind,
    BuildScriptKind BuildScriptKind,
    string? PrimaryBuildScriptPath,
    bool HasPowerForgeJson,
    bool HasProjectBuildJson,
    bool HasSolution,
    bool HasGitHubWorkflows,
    bool HasAzurePipelines,
    DateTimeOffset? LastCommitUtc,
    DateTimeOffset? LastScanUtc)
{
    public bool IsReleaseManaged => BuildScriptKind is not BuildScriptKind.None;

    public string CategoryDisplay => Category switch
    {
        ProjectCategory.PowerShellModule => "PS Module",
        ProjectCategory.DotNetLibrary => ".NET Library",
        ProjectCategory.Website => "Website",
        ProjectCategory.Tool => "Tool",
        ProjectCategory.Mixed => "Mixed",
        _ => "Unknown"
    };

    public string KindDisplay => Kind switch
    {
        ProjectKind.Worktree => "Worktree",
        ProjectKind.Fork => "Fork",
        ProjectKind.Archive => "Archive",
        ProjectKind.Template => "Template",
        _ => "Primary"
    };
}
