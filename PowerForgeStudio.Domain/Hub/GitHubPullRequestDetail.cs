namespace PowerForgeStudio.Domain.Hub;

public sealed record GitHubPullRequestDetail(
    GitHubPullRequest PullRequest,
    IReadOnlyList<GitHubDiscussionComment> Comments,
    IReadOnlyList<GitHubTimelineEvent> TimelineEvents,
    bool HasMoreDiscussion = false);
