namespace PowerForgeStudio.Domain.Hub;

public sealed record GitHubDiscussionComment(
    long Id,
    GitHubDiscussionCommentKind Kind,
    string? AuthorLogin,
    string BodyMarkdown,
    DateTimeOffset CreatedAt,
    string? HtmlUrl = null,
    string? Path = null,
    long? ParentCommentId = null,
    long? PullRequestReviewId = null,
    int? Line = null,
    int? StartLine = null,
    string? DiffHunk = null);
