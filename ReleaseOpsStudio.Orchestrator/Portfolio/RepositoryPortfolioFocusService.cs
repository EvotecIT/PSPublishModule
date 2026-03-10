using ReleaseOpsStudio.Domain.Portfolio;
using ReleaseOpsStudio.Domain.Queue;

namespace ReleaseOpsStudio.Orchestrator.Portfolio;

public sealed class RepositoryPortfolioFocusService
{
    public IReadOnlyList<RepositoryPortfolioItem> Filter(
        IReadOnlyList<RepositoryPortfolioItem> portfolioItems,
        ReleaseQueueSession? queueSession,
        RepositoryPortfolioFocusMode focusMode,
        string? searchText,
        string? familyKey = null)
    {
        ArgumentNullException.ThrowIfNull(portfolioItems);

        var queueLookup = (queueSession?.Items ?? [])
            .ToDictionary(item => item.RootPath, StringComparer.OrdinalIgnoreCase);
        var normalizedSearch = string.IsNullOrWhiteSpace(searchText)
            ? null
            : searchText.Trim();

        return portfolioItems
            .Where(item => MatchesFocus(item, queueLookup.GetValueOrDefault(item.RootPath), focusMode))
            .Where(item => MatchesFamily(item, familyKey))
            .Where(item => MatchesSearch(item, normalizedSearch))
            .ToArray();
    }

    private static bool MatchesFocus(RepositoryPortfolioItem item, ReleaseQueueItem? queueItem, RepositoryPortfolioFocusMode focusMode)
        => focusMode switch
        {
            RepositoryPortfolioFocusMode.Attention => IsAttention(item, queueItem),
            RepositoryPortfolioFocusMode.Ready => IsReady(item, queueItem),
            RepositoryPortfolioFocusMode.QueueActive => IsQueueActive(queueItem),
            RepositoryPortfolioFocusMode.Blocked => IsBlocked(item, queueItem),
            RepositoryPortfolioFocusMode.WaitingUsb => IsWaitingUsb(queueItem),
            RepositoryPortfolioFocusMode.PublishReady => IsPublishReady(queueItem),
            RepositoryPortfolioFocusMode.VerifyReady => IsVerifyReady(queueItem),
            RepositoryPortfolioFocusMode.Failed => IsFailed(queueItem),
            _ => true
        };

    private static bool IsAttention(RepositoryPortfolioItem item, ReleaseQueueItem? queueItem)
    {
        if (item.ReadinessKind is RepositoryReadinessKind.Attention or RepositoryReadinessKind.Blocked)
        {
            return true;
        }

        if (item.GitHubInbox?.Status == RepositoryGitHubInboxStatus.Attention)
        {
            return true;
        }

        if (item.ReleaseDrift?.Status == RepositoryReleaseDriftStatus.Attention)
        {
            return true;
        }

        return queueItem?.Status is ReleaseQueueItemStatus.WaitingApproval
            or ReleaseQueueItemStatus.Failed
            or ReleaseQueueItemStatus.Blocked;
    }

    private static bool IsReady(RepositoryPortfolioItem item, ReleaseQueueItem? queueItem)
    {
        var planResults = item.PlanResults ?? [];
        return item.ReadinessKind == RepositoryReadinessKind.Ready
            && planResults.Count > 0
            && planResults.All(result => result.Status == RepositoryPlanStatus.Succeeded)
            && queueItem?.Status is not ReleaseQueueItemStatus.Blocked
            && queueItem?.Status is not ReleaseQueueItemStatus.Failed;
    }

    private static bool IsQueueActive(ReleaseQueueItem? queueItem)
    {
        if (queueItem is null)
        {
            return false;
        }

        if (queueItem.Stage != ReleaseQueueStage.Prepare)
        {
            return true;
        }

        return queueItem.Status is ReleaseQueueItemStatus.ReadyToRun
            or ReleaseQueueItemStatus.WaitingApproval
            or ReleaseQueueItemStatus.Failed;
    }

    private static bool IsBlocked(RepositoryPortfolioItem item, ReleaseQueueItem? queueItem)
        => item.ReadinessKind == RepositoryReadinessKind.Blocked
           || queueItem?.Status is ReleaseQueueItemStatus.Blocked
           || queueItem?.Status is ReleaseQueueItemStatus.Failed;

    private static bool IsWaitingUsb(ReleaseQueueItem? queueItem)
        => queueItem?.Stage == ReleaseQueueStage.Sign
           && queueItem.Status == ReleaseQueueItemStatus.WaitingApproval;

    private static bool IsPublishReady(ReleaseQueueItem? queueItem)
        => queueItem?.Stage == ReleaseQueueStage.Publish
           && queueItem.Status == ReleaseQueueItemStatus.ReadyToRun;

    private static bool IsVerifyReady(ReleaseQueueItem? queueItem)
        => queueItem?.Stage == ReleaseQueueStage.Verify
           && queueItem.Status == ReleaseQueueItemStatus.ReadyToRun;

    private static bool IsFailed(ReleaseQueueItem? queueItem)
        => queueItem?.Status == ReleaseQueueItemStatus.Failed;

    private static bool MatchesSearch(RepositoryPortfolioItem item, string? searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        return Contains(item.Name, searchText)
               || Contains(item.FamilyDisplayName, searchText)
               || Contains(item.RootPath, searchText)
               || Contains(item.BranchName, searchText)
               || Contains(item.ReadinessReason, searchText)
               || Contains(item.PlanSummary, searchText)
               || Contains(item.GitHubInbox?.RepositorySlug, searchText)
               || Contains(item.GitHubSummary, searchText)
               || Contains(item.ReleaseDriftSummary, searchText);
    }

    private static bool MatchesFamily(RepositoryPortfolioItem item, string? familyKey)
        => string.IsNullOrWhiteSpace(familyKey)
           || string.Equals(item.FamilyKey, familyKey, StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string? value, string searchText)
        => !string.IsNullOrWhiteSpace(value)
           && value.Contains(searchText, StringComparison.OrdinalIgnoreCase);
}
