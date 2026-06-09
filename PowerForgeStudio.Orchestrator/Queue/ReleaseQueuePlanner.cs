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
            queueItems.Add(ReleaseQueueItemFactory.CreateFromPortfolioItem(orderedItems[index], index + 1, createdAtUtc));
        }

        return ReleaseQueueSessionFactory.Create(
            workspaceRoot: workspaceRoot,
            items: queueItems,
            createdAtUtc: createdAtUtc,
            scopeKey: scopeKey,
            scopeDisplayName: scopeDisplayName);
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

}
