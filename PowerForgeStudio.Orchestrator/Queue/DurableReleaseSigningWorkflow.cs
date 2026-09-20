using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Orchestrator.Storage;

namespace PowerForgeStudio.Orchestrator.Queue;

/// <summary>Journals signing before artifact mutation and retains an interrupted-stage marker until finalization commits.</summary>
public interface IReleaseSigningRecovery
{
    Task<ReleaseSigningWorkflowResult> RetrySaveAsync(ReleaseSigningWorkflowResult result, CancellationToken cancellationToken = default);
}

public sealed class DurableReleaseSigningWorkflow(string databasePath, IReleaseSigningWorkflow? signing = null) : IReleaseSigningWorkflow, IReleaseSigningRecovery
{
    private readonly IReleaseSigningWorkflow _signing = signing ?? new ReleaseSigningWorkflow();

    public async Task<ReleaseSigningWorkflowResult> SignAsync(ReleaseBuildHandoff handoff, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handoff);
        var item = handoff.Session.Items.Single();
        if (item.Stage != ReleaseQueueStage.Sign || item.Status != ReleaseQueueItemStatus.WaitingApproval)
            throw new InvalidOperationException("Prepare a successful build before signing.");
        using var lease = ReleaseWorkingCopyLease.Acquire(databasePath, item.RootPath);
        var database = new ReleaseStateDatabase(databasePath);
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var interruption = new ReleaseSigningExecutionResult(item.RootPath, false,
            "Signing started without a final receipt. It may still be running or may have been interrupted. Confirm it has stopped and rebuild before another attempt.",
            item.CheckpointStateJson, []) { RequiresRebuild = true };
        var marker = new ReleaseQueueRunner().FailSigning(handoff.Session, item.RootPath, interruption).Session;
        // The session identity is a single-use claim. No executor starts unless this insertion commits.
        if (!await database.TryAdvanceReleaseCheckpointAsync(null, marker, cancellationToken: cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("This release session already has an execution record. Reopen its saved state before taking another action.");
        var result = await _signing.SignAsync(handoff, cancellationToken).ConfigureAwait(false);
        using var finalization = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return await RetrySaveAsync(result with { PendingCheckpoint = marker }, finalization.Token).ConfigureAwait(false);
    }

    /// <summary>Retries only local persistence; it never invokes the signer again.</summary>
    public async Task<ReleaseSigningWorkflowResult> RetrySaveAsync(ReleaseSigningWorkflowResult result, CancellationToken cancellationToken = default)
    {
        if (result.PendingCheckpoint is not { } marker) return result;
        var database = new ReleaseStateDatabase(databasePath);
        try
        {
            var saved = await database.TryAdvanceReleaseCheckpointAsync(marker, result.Session, result.Execution.Receipts, cancellationToken).ConfigureAwait(false);
            if (!saved)
            {
                // A commit may have succeeded even if its response was lost. Accept only the exact completed state and evidence.
                var snapshot = await database.LoadReleaseCheckpointAsync(result.Session.SessionId, cancellationToken).ConfigureAwait(false);
                saved = snapshot is not null && ReleaseStateDatabase.MatchesCheckpoint(result.Session, snapshot.Session)
                    && snapshot.SigningReceipts.OrderBy(x => x.ArtifactPath, StringComparer.Ordinal).SequenceEqual(result.Execution.Receipts.OrderBy(x => x.ArtifactPath, StringComparer.Ordinal));
            }
            return saved ? result with { PendingCheckpoint = null, PersistenceError = null }
                : result with { PersistenceError = "The saved checkpoint changed during signing. Receipts remain here; saving will not overwrite a different checkpoint." };
        }
        catch (Exception ex)
        {
            return result with { PersistenceError = "Could not save signing completion: " + Host.StudioOutputSanitizer.Sanitize(ex.Message) };
        }
    }
}
