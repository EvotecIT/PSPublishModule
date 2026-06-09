namespace PowerForgeStudio.Domain.Hub;

public sealed record GitHubTimelineEvent(
    long Id,
    string EventName,
    string? ActorLogin,
    DateTimeOffset CreatedAt,
    string Markdown);
