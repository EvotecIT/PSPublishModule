namespace PowerForgeStudio.Domain.Queue;

public sealed record ReleaseQueueSession(
    string SessionId,
    string WorkspaceRoot,
    DateTimeOffset CreatedAtUtc,
    ReleaseQueueSummary Summary,
    IReadOnlyList<ReleaseQueueItem> Items,
    string? ScopeKey = null,
    string? ScopeDisplayName = null);

