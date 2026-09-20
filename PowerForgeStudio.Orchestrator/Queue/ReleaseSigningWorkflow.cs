using PowerForgeStudio.Domain.Queue;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed record ReleaseSigningWorkflowResult(ReleaseQueueSession Session, ReleaseSigningExecutionResult Execution)
{
    /// <summary>Non-null when returned evidence is available but its durable checkpoint could not be committed.</summary>
    public string? PersistenceError { get; init; }
    /// <summary>Expected durable marker retained until completion can be saved.</summary>
    public ReleaseQueueSession? PendingCheckpoint { get; init; }
}

public interface IReleaseSigningWorkflow
{
    Task<ReleaseSigningWorkflowResult> SignAsync(ReleaseBuildHandoff handoff, CancellationToken cancellationToken = default);
}

/// <summary>Executes only the captured handoff's signing stage through the canonical executor and queue transitions.</summary>
public sealed class ReleaseSigningWorkflow(IReleaseSigningExecutionService? signing = null) : IReleaseSigningWorkflow
{
    private readonly IReleaseSigningExecutionService _signing = signing ?? new ReleaseSigningExecutionService();

    public async Task<ReleaseSigningWorkflowResult> SignAsync(ReleaseBuildHandoff handoff, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handoff);
        var item = handoff.Session.Items.Single();
        if (item.Stage != ReleaseQueueStage.Sign || item.Status != ReleaseQueueItemStatus.WaitingApproval)
            throw new InvalidOperationException("Prepare a successful build before signing.");
        var result = await _signing.ExecuteAsync(item, cancellationToken).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested)
            result = result with { Succeeded = false, RequiresRebuild = true,
                Summary = "Signing cancelled; completed receipts retained. Rebuild before signing again." };
        var runner = new ReleaseQueueRunner();
        var transition = result.Succeeded ? runner.CompleteSigning(handoff.Session, item.RootPath, result)
            : runner.FailSigning(handoff.Session, item.RootPath, result);
        return new(transition.Session, result);
    }
}
