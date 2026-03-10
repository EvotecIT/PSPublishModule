using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.Queue;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed class ReleaseQueuePlanner
{
    public ReleaseQueueSession CreateDraftQueue(
        string workspaceRoot,
        IEnumerable<RepositoryPortfolioItem> portfolioItems,
        string? scopeKey = null,
        string? scopeDisplayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        var createdAtUtc = DateTimeOffset.UtcNow;
        var orderedItems = portfolioItems
            .Where(item => item.Repository.IsReleaseManaged)
            .OrderBy(GetPriority)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var queueItems = new List<ReleaseQueueItem>(orderedItems.Count);
        for (var index = 0; index < orderedItems.Count; index++)
        {
            queueItems.Add(BuildQueueItem(orderedItems[index], index + 1, createdAtUtc));
        }

        var summary = BuildSummary(queueItems);
        return new ReleaseQueueSession(
            SessionId: Guid.NewGuid().ToString("N"),
            WorkspaceRoot: workspaceRoot,
            CreatedAtUtc: createdAtUtc,
            Summary: summary,
            Items: queueItems,
            ScopeKey: scopeKey,
            ScopeDisplayName: scopeDisplayName);
    }

    private static int GetPriority(RepositoryPortfolioItem item)
    {
        var planResults = item.PlanResults ?? [];
        if (item.ReadinessKind == RepositoryReadinessKind.Ready && planResults.Count > 0 && planResults.All(result => result.Status == RepositoryPlanStatus.Succeeded))
        {
            return 0;
        }

        if (item.ReadinessKind == RepositoryReadinessKind.Attention)
        {
            return 1;
        }

        if (planResults.Count == 0)
        {
            return 2;
        }

        if (planResults.Any(result => result.Status == RepositoryPlanStatus.Failed))
        {
            return 3;
        }

        return item.ReadinessKind == RepositoryReadinessKind.Blocked ? 4 : 2;
    }

    private static ReleaseQueueItem BuildQueueItem(RepositoryPortfolioItem item, int queueOrder, DateTimeOffset timestamp)
    {
        if (item.ReadinessKind == RepositoryReadinessKind.Blocked)
        {
            return new ReleaseQueueItem(
                RootPath: item.RootPath,
                RepositoryName: item.Name,
                RepositoryKind: item.RepositoryKind,
                WorkspaceKind: item.WorkspaceKind,
                QueueOrder: queueOrder,
                Stage: ReleaseQueueStage.Prepare,
                Status: ReleaseQueueItemStatus.Blocked,
                Summary: item.ReadinessReason,
                CheckpointKey: "prepare.blocked.readiness",
                CheckpointStateJson: null,
                UpdatedAtUtc: timestamp);
        }

        var planResults = item.PlanResults ?? [];
        var failedPlan = planResults.FirstOrDefault(result => result.Status == RepositoryPlanStatus.Failed);
        if (failedPlan is not null)
        {
            return new ReleaseQueueItem(
                RootPath: item.RootPath,
                RepositoryName: item.Name,
                RepositoryKind: item.RepositoryKind,
                WorkspaceKind: item.WorkspaceKind,
                QueueOrder: queueOrder,
                Stage: ReleaseQueueStage.Prepare,
                Status: ReleaseQueueItemStatus.Blocked,
                Summary: FirstLine(failedPlan.ErrorTail ?? failedPlan.OutputTail ?? failedPlan.Summary),
                CheckpointKey: "prepare.blocked.plan",
                CheckpointStateJson: null,
                UpdatedAtUtc: timestamp);
        }

        if (item.ReadinessKind == RepositoryReadinessKind.Attention)
        {
            return new ReleaseQueueItem(
                RootPath: item.RootPath,
                RepositoryName: item.Name,
                RepositoryKind: item.RepositoryKind,
                WorkspaceKind: item.WorkspaceKind,
                QueueOrder: queueOrder,
                Stage: ReleaseQueueStage.Prepare,
                Status: ReleaseQueueItemStatus.Pending,
                Summary: item.ReadinessReason,
                CheckpointKey: "prepare.pending.readiness",
                CheckpointStateJson: null,
                UpdatedAtUtc: timestamp);
        }

        if (planResults.Count == 0)
        {
            return new ReleaseQueueItem(
                RootPath: item.RootPath,
                RepositoryName: item.Name,
                RepositoryKind: item.RepositoryKind,
                WorkspaceKind: item.WorkspaceKind,
                QueueOrder: queueOrder,
                Stage: ReleaseQueueStage.Prepare,
                Status: ReleaseQueueItemStatus.Pending,
                Summary: "Plan preview has not run yet.",
                CheckpointKey: "prepare.pending.plan",
                CheckpointStateJson: null,
                UpdatedAtUtc: timestamp);
        }

        return new ReleaseQueueItem(
            RootPath: item.RootPath,
            RepositoryName: item.Name,
            RepositoryKind: item.RepositoryKind,
            WorkspaceKind: item.WorkspaceKind,
            QueueOrder: queueOrder,
            Stage: ReleaseQueueStage.Build,
            Status: ReleaseQueueItemStatus.ReadyToRun,
            Summary: "Prepare checks passed. Ready for build execution.",
            CheckpointKey: "build.ready",
            CheckpointStateJson: null,
            UpdatedAtUtc: timestamp);
    }

    private static ReleaseQueueSummary BuildSummary(IReadOnlyList<ReleaseQueueItem> items)
    {
        return new ReleaseQueueSummary(
            TotalItems: items.Count,
            BuildReadyItems: items.Count(item => item.Stage == ReleaseQueueStage.Build && item.Status == ReleaseQueueItemStatus.ReadyToRun),
            PreparePendingItems: items.Count(item => item.Stage == ReleaseQueueStage.Prepare && item.Status == ReleaseQueueItemStatus.Pending),
            WaitingApprovalItems: items.Count(item => item.Status == ReleaseQueueItemStatus.WaitingApproval),
            BlockedItems: items.Count(item => item.Status == ReleaseQueueItemStatus.Blocked || item.Status == ReleaseQueueItemStatus.Failed),
            VerificationReadyItems: items.Count(item => item.Stage == ReleaseQueueStage.Verify && item.Status == ReleaseQueueItemStatus.ReadyToRun));
    }

    private static string FirstLine(string value)
        => value.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? value;
}
