using DBAClientX;

namespace PowerForgeStudio.Orchestrator.Storage;

public sealed record ReleaseHistoryEntry(string SessionId, string WorkingCopy, DateTimeOffset CreatedAtUtc)
{
    public string DisplayName => $"{Path.GetFileName(WorkingCopy)} · {CreatedAtUtc.LocalDateTime:g}";
}

public interface IReleaseHistoryService
{
    Task<IReadOnlyList<ReleaseHistoryEntry>> ListAsync(CancellationToken cancellationToken = default);
    Task<ReleaseCheckpointSnapshot?> LoadAsync(string sessionId, CancellationToken cancellationToken = default);
}

/// <summary>Reads bounded release history from the dedicated local release journal.</summary>
public sealed class ReleaseHistoryService(string databasePath) : IReleaseHistoryService
{
    public async Task<IReadOnlyList<ReleaseHistoryEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(databasePath)) return [];
        await new ReleaseStateDatabase(databasePath).InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var session = await new SQLite().OpenSessionAsync(databasePath, cancellationToken).ConfigureAwait(false);
        return await session.QueryAsListAsync("SELECT session_id, workspace_root, created_at_utc FROM release_queue_session ORDER BY created_at_utc DESC, session_id LIMIT 100;",
            row => new ReleaseHistoryEntry(row.GetString(0), row.GetString(1), DateTimeOffset.Parse(row.GetString(2))), cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<ReleaseCheckpointSnapshot?> LoadAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(databasePath)) return null;
        var database = new ReleaseStateDatabase(databasePath);
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return await database.LoadReleaseCheckpointAsync(sessionId, cancellationToken).ConfigureAwait(false);
    }
}
