using PowerForgeStudio.Domain.Queue;

namespace PowerForgeStudio.Orchestrator.Queue;

public interface IReleaseVerificationExecutionService
{
    Task<ReleaseVerificationExecutionResult> ExecuteAsync(ReleaseQueueItem queueItem, CancellationToken cancellationToken = default);

    Task<ReleaseVerificationExecutionResult> ExecuteAsync(
        ReleaseQueueItem queueItem,
        CancellationToken cancellationToken,
        IReleaseArtifactProgressSink? progress)
        => progress is null
            ? ExecuteAsync(queueItem, cancellationToken)
            : Task.FromException<ReleaseVerificationExecutionResult>(new InvalidOperationException(
                "This verifier does not support durable progress and cannot run through the durable verification workflow."));
}
