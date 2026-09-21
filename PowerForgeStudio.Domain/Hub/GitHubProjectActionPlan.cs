namespace PowerForgeStudio.Domain.Hub;

public sealed record GitHubProjectActionPlan(
    string RepositorySlug,
    int Number,
    bool IsPullRequest,
    GitHubProjectActionKind Kind,
    string ExpectedState,
    string ExpectedHeadSha,
    string Body)
{
    public string TargetDisplay => $"{RepositorySlug} · {(IsPullRequest ? "PR" : "Issue")} #{Number}";

    public string ActionDisplay => Kind switch
    {
        GitHubProjectActionKind.Comment => IsPullRequest ? "Comment on pull request" : "Comment on issue",
        GitHubProjectActionKind.CloseIssue => "Close issue",
        GitHubProjectActionKind.ReopenIssue => "Reopen issue",
        GitHubProjectActionKind.ApprovePullRequest => "Approve pull request",
        GitHubProjectActionKind.RequestPullRequestChanges => "Request changes",
        _ => Kind.ToString()
    };

    public string RevisionDisplay => IsPullRequest
        ? $"Exact PR head: {ExpectedHeadSha}"
        : $"Observed issue state: {ExpectedState}";

    public bool HasBody => !string.IsNullOrWhiteSpace(Body);
}
