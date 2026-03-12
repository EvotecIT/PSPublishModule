using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.Queue;

namespace PowerForgeStudio.Orchestrator.Queue;

public static class ReleaseQueueItemFactory
{
    public static ReleaseQueueItem CreateFromPortfolioItem(RepositoryPortfolioItem item, int queueOrder, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.ReadinessKind == RepositoryReadinessKind.Blocked)
        {
            return CreateItem(item, queueOrder, ReleaseQueueStage.Prepare, ReleaseQueueItemStatus.Blocked, item.ReadinessReason, "prepare.blocked.readiness", timestamp);
        }

        var planResults = item.PlanResults ?? [];
        var failedPlan = planResults.FirstOrDefault(result => result.Status == RepositoryPlanStatus.Failed);
        if (failedPlan is not null)
        {
            return CreateItem(
                item,
                queueOrder,
                ReleaseQueueStage.Prepare,
                ReleaseQueueItemStatus.Blocked,
                FirstLine(failedPlan.ErrorTail ?? failedPlan.OutputTail ?? failedPlan.Summary),
                "prepare.blocked.plan",
                timestamp);
        }

        if (item.ReadinessKind == RepositoryReadinessKind.Attention)
        {
            return CreateItem(item, queueOrder, ReleaseQueueStage.Prepare, ReleaseQueueItemStatus.Pending, item.ReadinessReason, "prepare.pending.readiness", timestamp);
        }

        if (planResults.Count == 0)
        {
            return CreateItem(item, queueOrder, ReleaseQueueStage.Prepare, ReleaseQueueItemStatus.Pending, "Plan preview has not run yet.", "prepare.pending.plan", timestamp);
        }

        return CreateItem(item, queueOrder, ReleaseQueueStage.Build, ReleaseQueueItemStatus.ReadyToRun, "Prepare checks passed. Ready for build execution.", "build.ready", timestamp);
    }

    private static ReleaseQueueItem CreateItem(
        RepositoryPortfolioItem item,
        int queueOrder,
        ReleaseQueueStage stage,
        ReleaseQueueItemStatus status,
        string summary,
        string checkpointKey,
        DateTimeOffset timestamp)
        => new(
            RootPath: item.RootPath,
            RepositoryName: item.Name,
            RepositoryKind: item.RepositoryKind,
            WorkspaceKind: item.WorkspaceKind,
            QueueOrder: queueOrder,
            Stage: stage,
            Status: status,
            Summary: summary,
            CheckpointKey: checkpointKey,
            CheckpointStateJson: null,
            UpdatedAtUtc: timestamp);

    private static string FirstLine(string value)
        => value.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? value;
}
