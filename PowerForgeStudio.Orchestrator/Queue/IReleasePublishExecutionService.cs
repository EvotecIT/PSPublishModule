using PowerForgeStudio.Domain.Queue;

namespace PowerForgeStudio.Orchestrator.Queue;

public interface IReleasePublishExecutionService
{
    Task<ReleasePublishExecutionResult> ExecuteAsync(ReleaseQueueItem queueItem, CancellationToken cancellationToken = default);

    Task<ReleasePublishExecutionResult> ExecuteAsync(
        ReleaseQueueItem queueItem,
        CancellationToken cancellationToken,
        IReleaseArtifactProgressSink? progress)
        => progress is null
            ? ExecuteAsync(queueItem, cancellationToken)
            : Task.FromException<ReleasePublishExecutionResult>(new InvalidOperationException(
                "This publisher does not support durable progress and cannot run through the durable publication workflow."));
}
