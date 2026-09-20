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
        => BuildTargetsByKey(queueItems, stage, tryReadCheckpoint, projectTargets, distinctKeySelector, StringComparer.OrdinalIgnoreCase);

    /// <summary>Projects pending targets using a caller-defined identity and equality contract.</summary>
    public IReadOnlyList<TTarget> BuildTargetsByKey<TCheckpoint, TTarget, TKey>(
        IEnumerable<ReleaseQueueItem> queueItems,
        ReleaseQueueStage stage,
        Func<ReleaseQueueItem, TCheckpoint?> tryReadCheckpoint,
        Func<ReleaseQueueItem, TCheckpoint, IEnumerable<TTarget>> projectTargets,
        Func<TTarget, TKey> distinctKeySelector,
        IEqualityComparer<TKey>? comparer = null)
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
            .DistinctBy(distinctKeySelector, comparer)
            .ToList();
    }
}
