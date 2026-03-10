namespace PowerForgeStudio.Domain.Workspace;

public sealed record WorkspaceRefreshOptions(
    int MaxPlanRepositories = 12,
    int MaxGitHubRepositories = 15,
    bool PersistState = true);
