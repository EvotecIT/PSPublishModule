using DBAClientX;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Orchestrator.Storage;

public sealed record ReleaseHistoryEntry(string SessionId, string WorkingCopy, DateTimeOffset CreatedAtUtc)
{
    public string DisplayName => $"{Path.GetFileName(WorkingCopy)} · {CreatedAtUtc.LocalDateTime:g}";
}

public interface IReleaseHistoryService
{
    Task<IReadOnlyList<ReleaseHistoryEntry>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReleaseHistoryEntry>> ListForWorkingCopyAsync(string workingCopyRoot, CancellationToken cancellationToken = default);
    Task<ReleaseCheckpointSnapshot?> LoadAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<ReleaseCheckpointSnapshot?> LoadForWorkingCopyAsync(string sessionId, string workingCopyRoot, CancellationToken cancellationToken = default);
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

    /// <summary>Queries matching queue items before applying the 100-session limit.</summary>
    public async Task<IReadOnlyList<ReleaseHistoryEntry>> ListForWorkingCopyAsync(
        string workingCopyRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingCopyRoot);
        if (!File.Exists(databasePath)) return [];
        var root = NormalizeRoot(workingCopyRoot);
        await new ReleaseStateDatabase(databasePath).InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var session = await new SQLite().OpenSessionAsync(databasePath, cancellationToken).ConfigureAwait(false);
        var storedRoots = await session.QueryAsListAsync(
            "SELECT DISTINCT root_path FROM release_queue_item;",
            row => row.GetString(0), cancellationToken: cancellationToken).ConfigureAwait(false);
        var matches = storedRoots.Where(stored => SameRoot(stored, root)).ToArray();
        if (matches.Length == 0) return [];
        var parameters = new Dictionary<string, object?>();
        var names = new string[matches.Length];
        for (var index = 0; index < matches.Length; index++)
        {
            names[index] = "@Root" + index;
            parameters[names[index]] = matches[index];
        }
        return await session.QueryAsListAsync($"""
            SELECT DISTINCT s.session_id, i.root_path, s.created_at_utc
            FROM release_queue_session AS s
            JOIN release_queue_item AS i ON i.session_id = s.session_id
            WHERE i.root_path IN ({string.Join(", ", names)})
            ORDER BY s.created_at_utc DESC, s.session_id DESC
            LIMIT 100;
            """,
            row => new ReleaseHistoryEntry(row.GetString(0), NormalizeRoot(row.GetString(1)), DateTimeOffset.Parse(row.GetString(2))),
            parameters, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Projects a saved batch onto one working copy; ambiguous batch progress stays in the full journal.</summary>
    public async Task<ReleaseCheckpointSnapshot?> LoadForWorkingCopyAsync(
        string sessionId, string workingCopyRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingCopyRoot);
        var snapshot = await LoadAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (snapshot is null) return null;
        var root = NormalizeRoot(workingCopyRoot);
        var items = snapshot.Session.Items.Where(item => SameRoot(item.RootPath, root)).ToArray();
        if (items.Length == 0) return null;
        var isBatch = items.Length != snapshot.Session.Items.Count;
        return new ReleaseCheckpointSnapshot(
            ReleaseQueueSessionFactory.WithItems(snapshot.Session, items),
            snapshot.SigningReceipts.Where(receipt => SameRoot(receipt.RootPath, root)).ToArray(),
            snapshot.PublishReceipts.Where(receipt => SameRoot(receipt.RootPath, root)).ToArray(),
            snapshot.VerificationReceipts.Where(receipt => SameRoot(receipt.RootPath, root)).ToArray())
        {
            Progress = isBatch ? [] : snapshot.Progress,
            IsScopedBatch = isBatch
        };
    }

    private static string NormalizeRoot(string root) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

    private static bool SameRoot(string left, string right)
        => string.Equals(NormalizeRoot(left), right,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
