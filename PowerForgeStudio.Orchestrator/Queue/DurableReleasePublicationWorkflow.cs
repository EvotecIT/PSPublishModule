using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Orchestrator.Storage;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed record ReleasePublicationWorkflowResult(ReleaseQueueSession Session, ReleasePublishExecutionResult Execution)
{
    public string? PersistenceError { get; init; }
    public ReleaseQueueSession? PendingCheckpoint { get; init; }
}

public interface IReleasePublicationWorkflow
{
    Task<ReleasePublicationWorkflowResult> PublishAsync(ReleaseQueueSession session, CancellationToken cancellationToken = default);
    Task<ReleasePublicationWorkflowResult> PublishAsync(
        ReleaseQueueSession session,
        CancellationToken cancellationToken,
        IReleaseArtifactProgressSink? progress)
        => progress is null
            ? PublishAsync(session, cancellationToken)
            : Task.FromException<ReleasePublicationWorkflowResult>(new InvalidOperationException(
                "This publication workflow does not support live progress."));
    Task<ReleasePublicationWorkflowResult> RetrySaveAsync(ReleasePublicationWorkflowResult result, CancellationToken cancellationToken = default);
}

/// <summary>Claims a saved signing checkpoint before publication and commits completion and receipts atomically.</summary>
public sealed class DurableReleasePublicationWorkflow(string databasePath, IReleasePublishExecutionService? publisher = null) : IReleasePublicationWorkflow
{
    private readonly IReleasePublishExecutionService _publisher = publisher ?? new ReleasePublishExecutionService();

    public async Task<ReleasePublicationWorkflowResult> PublishAsync(ReleaseQueueSession session, CancellationToken cancellationToken = default)
        => await PublishAsync(session, cancellationToken, progress: null).ConfigureAwait(false);

    public async Task<ReleasePublicationWorkflowResult> PublishAsync(
        ReleaseQueueSession session,
        CancellationToken cancellationToken,
        IReleaseArtifactProgressSink? progress)
    {
        ArgumentNullException.ThrowIfNull(session);
        var item = session.Items.Single();
        if (item.Stage != ReleaseQueueStage.Publish || item.Status != ReleaseQueueItemStatus.ReadyToRun)
            throw new InvalidOperationException("A saved, publish-ready signing checkpoint is required.");
        using var lease = ReleaseWorkingCopyLease.Acquire(databasePath, item.RootPath);
        var database = new ReleaseStateDatabase(databasePath);
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var interrupted = new ReleasePublishExecutionResult(item.RootPath, false,
            "Publication started without a final receipt. It may still be running or may have been interrupted. Reconcile remote state before retrying.",
            item.CheckpointStateJson, []) { RequiresReconciliation = true };
        var runner = new ReleaseQueueRunner();
        var marker = runner.FailPublish(session, item.RootPath, interrupted).Session;
        if (!await database.TryAdvanceReleaseCheckpointAsync(session, marker, cancellationToken: cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("The saved release checkpoint changed or is missing. Reload it before publishing.");

        var durableProgress = new DurableReleaseArtifactProgressSink(database, marker.SessionId, progress);

        ReleasePublishExecutionResult execution;
        try
        {
            execution = await _publisher.ExecuteAsync(item, cancellationToken, durableProgress).ConfigureAwait(false);
            if (execution.RootPath != item.RootPath || execution.Receipts.Any(receipt => receipt.RootPath != item.RootPath))
                throw new InvalidOperationException("Publisher returned evidence for a different working copy.");
        }
        catch (Exception)
        {
            // No remote replay: an executor failure cannot prove whether its in-flight operation completed.
            execution = interrupted with { WasCancelled = cancellationToken.IsCancellationRequested };
        }
        var completed = execution.Succeeded ? runner.CompletePublish(session, item.RootPath, execution).Session
            : runner.FailPublish(session, item.RootPath, execution).Session;
        using var finalization = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return await RetrySaveAsync(new(completed, execution) { PendingCheckpoint = marker }, finalization.Token).ConfigureAwait(false);
    }

    /// <summary>Retries only local receipt persistence, never the remote publisher.</summary>
    public async Task<ReleasePublicationWorkflowResult> RetrySaveAsync(ReleasePublicationWorkflowResult result, CancellationToken cancellationToken = default)
    {
        if (result.PendingCheckpoint is not { } marker) return result;
        var database = new ReleaseStateDatabase(databasePath);
        try
        {
            var saved = await database.TryAdvanceReleaseCheckpointAsync(marker, result.Session, cancellationToken: cancellationToken,
                publishReceipts: result.Execution.Receipts).ConfigureAwait(false);
            if (!saved)
            {
                var snapshot = await database.LoadReleaseCheckpointAsync(result.Session.SessionId, cancellationToken).ConfigureAwait(false);
                var expected = result.Execution.Receipts.GroupBy(receipt => receipt).ToDictionary(group => group.Key, group => group.Count());
                saved = snapshot is not null && ReleaseStateDatabase.MatchesCheckpoint(result.Session, snapshot.Session)
                    && snapshot.PublishReceipts.Count == result.Execution.Receipts.Count
                    && snapshot.PublishReceipts.GroupBy(receipt => receipt)
                        .All(group => expected.TryGetValue(group.Key, out var count) && count == group.Count());
            }
            return saved ? result with { PendingCheckpoint = null, PersistenceError = null }
                : result with { PersistenceError = "The saved checkpoint changed. Receipts remain here; saving will not overwrite a different checkpoint." };
        }
        catch (Exception)
        {
            return result with { PersistenceError = "Could not save publication completion. Receipts remain available for a local save retry." };
        }
    }
}
