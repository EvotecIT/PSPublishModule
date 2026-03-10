using PowerForgeStudio.Domain.Queue;

namespace PowerForgeStudio.Orchestrator.Queue;

public interface IReleaseSigningExecutionService
{
    Task<ReleaseSigningExecutionResult> ExecuteAsync(ReleaseQueueItem queueItem, CancellationToken cancellationToken = default);
}
