using PowerForgeStudio.Domain.Queue;

namespace PowerForgeStudio.Orchestrator.Queue;

public static class ReleaseQueueSummaryFactory
{
    public static ReleaseQueueSummary Create(IReadOnlyList<ReleaseQueueItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return new ReleaseQueueSummary(
            TotalItems: items.Count,
            BuildReadyItems: items.Count(item => item.Stage == ReleaseQueueStage.Build && item.Status == ReleaseQueueItemStatus.ReadyToRun),
            PreparePendingItems: items.Count(item => item.Stage == ReleaseQueueStage.Prepare && item.Status == ReleaseQueueItemStatus.Pending),
            WaitingApprovalItems: items.Count(item => item.Status == ReleaseQueueItemStatus.WaitingApproval),
            BlockedItems: items.Count(item => item.Status == ReleaseQueueItemStatus.Blocked || item.Status == ReleaseQueueItemStatus.Failed),
            VerificationReadyItems: items.Count(item => item.Stage == ReleaseQueueStage.Verify && item.Status == ReleaseQueueItemStatus.ReadyToRun));
    }
}
