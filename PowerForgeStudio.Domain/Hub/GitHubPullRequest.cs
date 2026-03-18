namespace PowerForgeStudio.Domain.Hub;

public sealed record GitHubPullRequest(
    int Number,
    string Title,
    string State,
    string? AuthorLogin,
    string HeadBranch,
    string BaseBranch,
    GitHubPrReviewStatus ReviewStatus,
    GitHubPrMergeStatus MergeStatus,
    IReadOnlyList<string> Labels,
    int Additions,
    int Deletions,
    int ChangedFiles,
    DateTimeOffset CreatedAt,
    DateTimeOffset? MergedAt)
{
    public bool IsOpen => string.Equals(State, "open", StringComparison.OrdinalIgnoreCase);

    public string DiffStatDisplay => $"+{Additions} / -{Deletions}";

    public string ReviewStatusDisplay => ReviewStatus switch
    {
        GitHubPrReviewStatus.Approved => "Approved",
        GitHubPrReviewStatus.ChangesRequested => "Changes requested",
        GitHubPrReviewStatus.Pending => "Review pending",
        _ => "No reviews"
    };

    public string MergeStatusDisplay => MergeStatus switch
    {
        GitHubPrMergeStatus.Clean => "Ready to merge",
        GitHubPrMergeStatus.Blocked => "Blocked",
        GitHubPrMergeStatus.Behind => "Behind base",
        GitHubPrMergeStatus.Conflicting => "Conflicts",
        _ => "Unknown"
    };
}
