namespace ReleaseOpsStudio.Domain.Portfolio;

public sealed record RepositoryPortfolioSummary(
    int TotalRepositories,
    int ReadyRepositories,
    int AttentionRepositories,
    int BlockedRepositories,
    int DirtyRepositories,
    int BehindRepositories,
    int WorktreeRepositories,
    int GitHubAttentionRepositories,
    int OpenPullRequests,
    int ReleaseDriftAttentionRepositories);
