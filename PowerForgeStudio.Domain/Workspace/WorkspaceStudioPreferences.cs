using PowerForgeStudio.Domain.Activity;

namespace PowerForgeStudio.Domain.Workspace;

/// <summary>Machine-local Studio behavior. Credential values and provider runtime state never belong here.</summary>
public sealed record WorkspaceStudioPreferences(
    bool RestoreOpenDocuments = true,
    int ActivityMaxGitHubRepositories = 8,
    int ActivityMaxIssuesPerRepository = 3,
    int ActivityMaxEntries = 120,
    int ActivityGitHubTimeoutSeconds = 60)
{
    public static WorkspaceStudioPreferences Default { get; } = new();

    public WorkspaceActivityOptions ToActivityOptions()
        => new(ActivityMaxGitHubRepositories, ActivityMaxIssuesPerRepository, ActivityMaxEntries, ActivityGitHubTimeoutSeconds);
}
