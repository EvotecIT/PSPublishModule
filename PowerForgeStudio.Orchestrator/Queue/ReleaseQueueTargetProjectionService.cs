using PowerForgeStudio.Domain.Queue;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed class ReleaseQueueTargetProjectionService
{
    public IReadOnlyList<TTarget> BuildTargets<TCheckpoint, TTarget>(
        IEnumerable<ReleaseQueueItem> queueItems,
        ReleaseQueueStage stage,
        Func<ReleaseQueueItem, TCheckpoint?> tryReadCheckpoint,
        Func<ReleaseQueueItem, TCheckpoint, IEnumerable<TTarget>> projectTargets,
        Func<TTarget, string> distinctKeySelector)
    {
        ArgumentNullException.ThrowIfNull(queueItems);
        ArgumentNullException.ThrowIfNull(tryReadCheckpoint);
        ArgumentNullException.ThrowIfNull(projectTargets);
        ArgumentNullException.ThrowIfNull(distinctKeySelector);

        var targets = new List<TTarget>();
        foreach (var item in queueItems.Where(candidate => candidate.Stage == stage && candidate.Status == ReleaseQueueItemStatus.ReadyToRun))
        {
            var checkpoint = tryReadCheckpoint(item);
            if (checkpoint is null)
            {
                continue;
            }

            targets.AddRange(projectTargets(item, checkpoint));
        }

        return targets
            .DistinctBy(distinctKeySelector, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
