namespace PowerForgeStudio.Domain.Portfolio;

public sealed record RepositoryGitHubInbox(
    RepositoryGitHubInboxStatus Status,
    string? RepositorySlug,
    int? OpenPullRequestCount,
    bool? LatestWorkflowFailed,
    string? LatestReleaseTag,
    string? DefaultBranch,
    string? ProbedBranch,
    bool? IsDefaultBranch,
    bool? BranchProtectionEnabled,
    string Summary,
    string Detail)
{
    public string StatusDisplay => Status switch
    {
        RepositoryGitHubInboxStatus.NotProbed => "Not probed",
        RepositoryGitHubInboxStatus.Ready => "Ready",
        RepositoryGitHubInboxStatus.Attention => "Attention",
        RepositoryGitHubInboxStatus.Unavailable => "Unavailable",
        _ => Status.ToString()
    };

    public string PullRequestDisplay => OpenPullRequestCount is null ? "-" : OpenPullRequestCount.Value.ToString();

    public string GovernanceSummary => BranchProtectionEnabled switch
    {
        true when !string.IsNullOrWhiteSpace(ProbedBranch) => $"{ProbedBranch} is protected on GitHub.",
        false when !string.IsNullOrWhiteSpace(ProbedBranch) => $"{ProbedBranch} is not marked protected on GitHub.",
        _ when !string.IsNullOrWhiteSpace(DefaultBranch) => $"Default branch is {DefaultBranch}.",
        _ => "Branch governance not confirmed remotely."
    };
}

