namespace PowerForgeStudio.Domain.Hub;

public enum GitHubProjectActionKind
{
    Comment,
    CloseIssue,
    ReopenIssue,
    ApprovePullRequest,
    RequestPullRequestChanges
}
