namespace PowerForgeStudio.Domain.Workspace;

public sealed record WorkspaceRefreshOptions(
    int MaxPlanRepositories = -1,
    int MaxGitHubRepositories = -1,
    bool PersistState = true);
