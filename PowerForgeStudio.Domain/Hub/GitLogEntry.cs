namespace PowerForgeStudio.Domain.Hub;

public sealed record GitLogEntry(
    string Hash,
    string ShortHash,
    string AuthorName,
    string Message,
    DateTimeOffset CommittedAt)
{
    public string RelativeTime
    {
        get
        {
            var elapsed = DateTimeOffset.UtcNow - CommittedAt;
            return elapsed.TotalMinutes < 1 ? "just now"
                : elapsed.TotalHours < 1 ? $"{(int)elapsed.TotalMinutes}m ago"
                : elapsed.TotalDays < 1 ? $"{(int)elapsed.TotalHours}h ago"
                : elapsed.TotalDays < 30 ? $"{(int)elapsed.TotalDays}d ago"
                : CommittedAt.LocalDateTime.ToString("yyyy-MM-dd");
        }
    }
}
