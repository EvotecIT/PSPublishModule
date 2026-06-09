using PowerForgeStudio.Domain.Queue;

namespace PowerForgeStudio.Orchestrator.Queue;

public static class ReleaseQueueSessionFactory
{
    public static ReleaseQueueSession Create(
        string workspaceRoot,
        IReadOnlyList<ReleaseQueueItem> items,
        DateTimeOffset createdAtUtc,
        string? scopeKey = null,
        string? scopeDisplayName = null,
        string? sessionId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(items);

        return new ReleaseQueueSession(
            SessionId: string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId,
            WorkspaceRoot: workspaceRoot,
            CreatedAtUtc: createdAtUtc,
            Summary: ReleaseQueueSummaryFactory.Create(items),
            Items: items,
            ScopeKey: scopeKey,
            ScopeDisplayName: scopeDisplayName);
    }

    public static ReleaseQueueSession WithItems(
        ReleaseQueueSession session,
        IReadOnlyList<ReleaseQueueItem> items)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(items);

        return session with {
            Items = items,
            Summary = ReleaseQueueSummaryFactory.Create(items)
        };
    }
}
