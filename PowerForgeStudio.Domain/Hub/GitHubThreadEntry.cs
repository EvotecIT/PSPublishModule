namespace PowerForgeStudio.Domain.Hub;

public sealed record GitHubThreadEntry(
    GitHubThreadEntryKind Kind,
    string Title,
    string? AuthorLogin,
    DateTimeOffset CreatedAt,
    string Markdown,
    string? HtmlUrl = null,
    string? Path = null)
{
    public string BadgeText => Kind switch
    {
        GitHubThreadEntryKind.Description => "Description",
        GitHubThreadEntryKind.Comment => "Comment",
        GitHubThreadEntryKind.ReviewThread => "Review",
        GitHubThreadEntryKind.TimelineEvent => "Activity",
        _ => "Entry"
    };

    public bool HasPath => !string.IsNullOrWhiteSpace(Path);
}
