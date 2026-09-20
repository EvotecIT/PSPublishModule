using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;

namespace PowerForgeStudio.Orchestrator.Storage;

public sealed partial class ReleaseStateDatabase
{
    /// <summary>Atomically creates a new session or replaces exactly the observed checkpoint. A stale caller makes no changes.</summary>
    public async Task<bool> TryAdvanceReleaseCheckpointAsync(ReleaseQueueSession? expected, ReleaseQueueSession next,
        IReadOnlyList<ReleaseSigningReceipt>? signingReceipts = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentException.ThrowIfNullOrWhiteSpace(next.SessionId);
        if (expected is not null && expected.SessionId != next.SessionId)
            throw new ArgumentException("A transition must retain its session identity.", nameof(next));
        await using var database = await _sqlite.OpenSessionAsync(DatabasePath, cancellationToken).ConfigureAwait(false);
        return await database.RunInTransactionAsync(async (transaction, token) =>
        {
            var current = await LoadQueueSessionCoreAsync(transaction, next.SessionId, token).ConfigureAwait(false);
            if (expected is null ? current is not null : current is null || !MatchesCheckpoint(expected, current)) return false;
            await PersistQueueSessionCoreAsync(transaction, next, token).ConfigureAwait(false);
            if (signingReceipts is not null)
                await PersistReceiptSetAsync(transaction, next.SessionId, signingReceipts, SigningReceiptTable, token).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    internal static bool MatchesCheckpoint(ReleaseQueueSession expected, ReleaseQueueSession current)
        => expected.SessionId == current.SessionId && expected.WorkspaceRoot == current.WorkspaceRoot
            && expected.CreatedAtUtc == current.CreatedAtUtc && expected.ScopeKey == current.ScopeKey
            && expected.ScopeDisplayName == current.ScopeDisplayName && expected.Items.SequenceEqual(current.Items);
}
