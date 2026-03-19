namespace PowerForgeStudio.Domain.Hub;

public sealed record GitLogEntry(
    string Hash,
    string ShortHash,
    string AuthorName,
    string Message,
    DateTimeOffset CommittedAt)
{
    public string RelativeTime => RelativeTimeFormatter.FormatWithAgo(CommittedAt);
}
