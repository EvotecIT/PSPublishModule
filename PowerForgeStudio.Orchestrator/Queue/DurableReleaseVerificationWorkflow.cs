using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Orchestrator.Storage;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed record ReleaseVerificationWorkflowResult(ReleaseQueueSession Session, ReleaseVerificationExecutionResult Execution)
{
    public string? PersistenceError { get; init; }
    public ReleaseQueueSession? PendingCheckpoint { get; init; }
}

public interface IReleaseVerificationWorkflow
{
    Task<ReleaseVerificationWorkflowResult> VerifyAsync(ReleaseQueueSession session, CancellationToken cancellationToken = default);
    Task<ReleaseVerificationWorkflowResult> RetrySaveAsync(ReleaseVerificationWorkflowResult result, CancellationToken cancellationToken = default);
}

/// <summary>Claims a saved publication checkpoint before remote verification and commits results atomically.</summary>
public sealed class DurableReleaseVerificationWorkflow(string databasePath, IReleaseVerificationExecutionService? verifier = null) : IReleaseVerificationWorkflow, IDisposable
{
    private readonly IReleaseVerificationExecutionService _verifier = verifier ?? new ReleaseVerificationExecutionService();
    private readonly bool _ownsVerifier = verifier is null;

    public void Dispose()
    {
        if (_ownsVerifier && _verifier is IDisposable disposable) disposable.Dispose();
    }

    public async Task<ReleaseVerificationWorkflowResult> VerifyAsync(ReleaseQueueSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var item = session.Items.Single();
        if (item.Stage != ReleaseQueueStage.Verify || item.Status != ReleaseQueueItemStatus.ReadyToRun)
            throw new InvalidOperationException("A saved, verification-ready publication checkpoint is required.");

        using var lease = ReleaseWorkingCopyLease.Acquire(databasePath, item.RootPath);
        var database = new ReleaseStateDatabase(databasePath);
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var interrupted = new ReleaseVerificationExecutionResult(item.RootPath, false,
            "Verification started without a final receipt. The remote delivery remains unverified until checks are rerun.",
            item.CheckpointStateJson, []);
        var runner = new ReleaseQueueRunner();
        var marker = runner.FailVerification(session, item.RootPath, interrupted).Session;
        if (!await database.TryAdvanceReleaseCheckpointAsync(session, marker, cancellationToken: cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("The saved release checkpoint changed or is missing. Reload it before verifying.");

        ReleaseVerificationExecutionResult execution;
        try
        {
            execution = await _verifier.ExecuteAsync(item, cancellationToken).ConfigureAwait(false);
            if (execution.RootPath != item.RootPath || execution.Receipts.Any(receipt => receipt.RootPath != item.RootPath))
                throw new InvalidOperationException("Verifier returned evidence for a different working copy.");
        }
        catch (Exception exception)
        {
            execution = interrupted with { WasCancelled = cancellationToken.IsCancellationRequested || exception is OperationCanceledException };
        }

        var completed = execution.Succeeded ? runner.CompleteVerification(session, item.RootPath, execution).Session
            : runner.FailVerification(session, item.RootPath, execution).Session;
        using var finalization = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return await RetrySaveAsync(new(completed, execution) { PendingCheckpoint = marker }, finalization.Token).ConfigureAwait(false);
    }

    /// <summary>Retries local checkpoint persistence without rerunning remote probes.</summary>
    public async Task<ReleaseVerificationWorkflowResult> RetrySaveAsync(ReleaseVerificationWorkflowResult result, CancellationToken cancellationToken = default)
    {
        if (result.PendingCheckpoint is not { } marker) return result;
        var database = new ReleaseStateDatabase(databasePath);
        try
        {
            var saved = await database.TryAdvanceReleaseCheckpointAsync(marker, result.Session, cancellationToken: cancellationToken,
                verificationReceipts: result.Execution.Receipts).ConfigureAwait(false);
            if (!saved)
            {
                var snapshot = await database.LoadReleaseCheckpointAsync(result.Session.SessionId, cancellationToken).ConfigureAwait(false);
                var expected = result.Execution.Receipts.GroupBy(receipt => receipt).ToDictionary(group => group.Key, group => group.Count());
                saved = snapshot is not null && ReleaseStateDatabase.MatchesCheckpoint(result.Session, snapshot.Session)
                    && snapshot.VerificationReceipts.Count == result.Execution.Receipts.Count
                    && snapshot.VerificationReceipts.GroupBy(receipt => receipt)
                        .All(group => expected.TryGetValue(group.Key, out var count) && count == group.Count());
            }

            return saved ? result with { PendingCheckpoint = null, PersistenceError = null }
                : result with { PersistenceError = "The saved checkpoint changed. Verification receipts remain here; saving will not overwrite a different checkpoint." };
        }
        catch (Exception)
        {
            return result with { PersistenceError = "Could not save verification completion. Receipts remain available for a local save retry." };
        }
    }
}
