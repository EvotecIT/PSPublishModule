namespace PowerForgeStudio.Domain.Hub;

public sealed record GitHubIssueDetail(
    GitHubIssue Issue,
    IReadOnlyList<GitHubDiscussionComment> Comments,
    IReadOnlyList<GitHubTimelineEvent> TimelineEvents,
    bool HasMoreDiscussion = false);
