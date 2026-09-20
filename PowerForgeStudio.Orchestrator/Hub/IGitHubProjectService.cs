using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Orchestrator.Hub;

/// <summary>Read-only project GitHub operations shared by desktop hosts.</summary>
public interface IGitHubProjectService
{
    Task<string?> ResolveRepositoryAsync(string workingCopy, CancellationToken cancellationToken = default);
    Task<GitHubPullRequestFiles> FetchPullRequestFilesAsync(string slug, int number, string expectedHeadSha, CancellationToken cancellationToken = default);
    Task<GitHubPage<GitHubCheck>> FetchChecksAsync(string slug, string headSha, CancellationToken cancellationToken = default);
    Task<GitHubPage<GitHubIssue>> FetchIssuesAsync(string slug, string state = "open", CancellationToken cancellationToken = default);
    Task<GitHubPage<GitHubPullRequest>> FetchPullRequestsAsync(string slug, string state = "open", CancellationToken cancellationToken = default);
    Task<GitHubIssueDetail?> FetchIssueDetailAsync(string slug, int issueNumber, CancellationToken cancellationToken = default);
    Task<GitHubPullRequestDetail?> FetchPullRequestDetailAsync(string slug, int pullRequestNumber, CancellationToken cancellationToken = default);
}
