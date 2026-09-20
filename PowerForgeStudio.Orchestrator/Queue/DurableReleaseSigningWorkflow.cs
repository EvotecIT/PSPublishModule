using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Orchestrator.Storage;

namespace PowerForgeStudio.Orchestrator.Queue;

/// <summary>Journals signing before artifact mutation and retains an interrupted-stage marker until finalization commits.</summary>
public sealed class DurableReleaseSigningWorkflow(string databasePath, IReleaseSigningWorkflow? signing = null) : IReleaseSigningWorkflow
{
    private readonly IReleaseSigningWorkflow _signing = signing ?? new ReleaseSigningWorkflow();

    public async Task<ReleaseSigningWorkflowResult> SignAsync(ReleaseBuildHandoff handoff, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handoff);
        var item = handoff.Session.Items.Single();
        if (item.Stage != ReleaseQueueStage.Sign || item.Status != ReleaseQueueItemStatus.WaitingApproval)
            throw new InvalidOperationException("Prepare a successful build before signing.");
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
        try
        {
            if (!await database.TryAdvanceReleaseCheckpointAsync(marker, result.Session, result.Execution.Receipts, finalization.Token).ConfigureAwait(false))
                return result with { PersistenceError = "The saved checkpoint changed during signing. Completed receipts are retained in this session; reopen the saved record before continuing." };
        }
        catch (Exception ex)
        {
            return result with { PersistenceError = "Could not save signing completion: " + Host.StudioOutputSanitizer.Sanitize(ex.Message) };
        }
        return result;
    }
}
