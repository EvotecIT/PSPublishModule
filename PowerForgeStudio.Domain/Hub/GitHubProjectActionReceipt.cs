namespace PowerForgeStudio.Domain.Hub;

public sealed record GitHubProjectActionReceipt(
    GitHubProjectActionKind Kind,
    string RepositorySlug,
    int Number,
    bool IsPullRequest,
    string? HtmlUrl,
    DateTimeOffset CompletedAtUtc)
{
    public string Summary => $"{Kind switch
    {
        GitHubProjectActionKind.Comment => "Comment posted",
        GitHubProjectActionKind.CloseIssue => "Issue closed",
        GitHubProjectActionKind.ReopenIssue => "Issue reopened",
        GitHubProjectActionKind.ApprovePullRequest => "PR approval submitted",
        GitHubProjectActionKind.RequestPullRequestChanges => "Changes requested",
        _ => "Action completed"
    }} for {(IsPullRequest ? "PR" : "issue")} #{Number}.";
}
