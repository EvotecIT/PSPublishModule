using PowerForgeStudio.Domain.Queue;

namespace PowerForgeStudio.Orchestrator.Queue;

public interface IReleasePublishExecutionService
{
    Task<ReleasePublishExecutionResult> ExecuteAsync(ReleaseQueueItem queueItem, CancellationToken cancellationToken = default);
}
