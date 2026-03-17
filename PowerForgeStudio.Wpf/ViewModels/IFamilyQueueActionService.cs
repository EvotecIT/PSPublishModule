using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.Queue;

namespace PowerForgeStudio.Wpf.ViewModels;

public interface IFamilyQueueActionService
{
    bool CanPrepare(bool isBusy, RepositoryWorkspaceFamilySnapshot? family);

    bool CanRetryFailed(
        bool isBusy,
        ReleaseQueueSession? queueSession,
        IReadOnlyList<RepositoryPortfolioItem> portfolioItems,
        RepositoryWorkspaceFamilySnapshot? family);

    Task<FamilyQueueActionResult> PrepareQueueAsync(
        string databasePath,
        string workspaceRoot,
        IReadOnlyList<RepositoryPortfolioItem> portfolioItems,
        RepositoryWorkspaceFamilySnapshot? family,
        CancellationToken cancellationToken = default);

    Task<FamilyQueueActionResult> RetryFailedAsync(
        string databasePath,
        ReleaseQueueSession? queueSession,
        IReadOnlyList<RepositoryPortfolioItem> portfolioItems,
        RepositoryWorkspaceFamilySnapshot? family,
        CancellationToken cancellationToken = default);
}
