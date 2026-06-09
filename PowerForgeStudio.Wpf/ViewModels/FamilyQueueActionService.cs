using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed class FamilyQueueActionService : IFamilyQueueActionService
{
    private readonly IReleaseQueueCommandService _queueCommandService;

    public FamilyQueueActionService()
        : this(new ReleaseQueueCommandService())
    {
    }

    public FamilyQueueActionService(IReleaseQueueCommandService queueCommandService)
    {
        _queueCommandService = queueCommandService ?? throw new ArgumentNullException(nameof(queueCommandService));
    }

    public bool CanPrepare(bool isBusy, RepositoryWorkspaceFamilySnapshot? family)
        => !isBusy && family is not null;

    public bool CanRetryFailed(
        bool isBusy,
        ReleaseQueueSession? queueSession,
        IReadOnlyList<RepositoryPortfolioItem> portfolioItems,
        RepositoryWorkspaceFamilySnapshot? family)
    {
        if (isBusy || queueSession is null || family is null)
        {
            return false;
        }

        var familyRootPaths = GetFamilyRootPaths(portfolioItems, family);
        return queueSession.Items.Any(item =>
            item.Status == ReleaseQueueItemStatus.Failed
            && familyRootPaths.Contains(item.RootPath));
    }

    public async Task<FamilyQueueActionResult> PrepareQueueAsync(
        string databasePath,
        string workspaceRoot,
        IReadOnlyList<RepositoryPortfolioItem> portfolioItems,
        RepositoryWorkspaceFamilySnapshot? family,
        CancellationToken cancellationToken = default)
    {
        if (family is null)
        {
            return new FamilyQueueActionResult("Select a repository family first, then prepare a family-scoped queue.");
        }

        var familyItems = GetFamilyItems(portfolioItems, family);
        if (familyItems.Length == 0)
        {
            return new FamilyQueueActionResult($"No portfolio items are currently available for the {family.DisplayName} family.");
        }

        var result = await _queueCommandService.PrepareQueueAsync(
            databasePath,
            workspaceRoot,
            familyItems,
            scopeKey: family.FamilyKey,
            scopeDisplayName: family.DisplayName,
            cancellationToken).ConfigureAwait(false);

        return new FamilyQueueActionResult(result.Message, result);
    }

    public async Task<FamilyQueueActionResult> RetryFailedAsync(
        string databasePath,
        ReleaseQueueSession? queueSession,
        IReadOnlyList<RepositoryPortfolioItem> portfolioItems,
        RepositoryWorkspaceFamilySnapshot? family,
        CancellationToken cancellationToken = default)
    {
        if (family is null)
        {
            return new FamilyQueueActionResult("Select a repository family first, then retry failed items for that family.");
        }

        if (queueSession is null)
        {
            return new FamilyQueueActionResult("Prepare a draft queue first, then retry failed items for that family.");
        }

        var familyRootPaths = GetFamilyRootPaths(portfolioItems, family);
        if (familyRootPaths.Count == 0)
        {
            return new FamilyQueueActionResult($"No portfolio items are currently available for the {family.DisplayName} family.");
        }

        if (!queueSession.Items.Any(item =>
                item.Status == ReleaseQueueItemStatus.Failed
                && familyRootPaths.Contains(item.RootPath)))
        {
            return new FamilyQueueActionResult($"No failed queue items are currently available for the {family.DisplayName} family.");
        }

        var result = await _queueCommandService.RetryFailedAsync(
            databasePath,
            item => familyRootPaths.Contains(item.RootPath),
            cancellationToken).ConfigureAwait(false);

        return new FamilyQueueActionResult(result.Message, result);
    }

    private static RepositoryPortfolioItem[] GetFamilyItems(
        IReadOnlyList<RepositoryPortfolioItem> portfolioItems,
        RepositoryWorkspaceFamilySnapshot family)
        => portfolioItems
            .Where(item => string.Equals(item.FamilyKey, family.FamilyKey, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private static HashSet<string> GetFamilyRootPaths(
        IReadOnlyList<RepositoryPortfolioItem> portfolioItems,
        RepositoryWorkspaceFamilySnapshot family)
        => GetFamilyItems(portfolioItems, family)
            .Select(item => item.RootPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
