namespace PowerForgeStudio.Domain.Portfolio;

public sealed record RepositoryGitHubInbox(
    RepositoryGitHubInboxStatus Status,
    string? RepositorySlug,
    int? OpenPullRequestCount,
    bool? LatestWorkflowFailed,
    string? LatestReleaseTag,
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
}

