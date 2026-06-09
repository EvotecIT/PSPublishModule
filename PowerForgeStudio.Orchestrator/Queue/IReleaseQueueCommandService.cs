using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.Queue;

namespace PowerForgeStudio.Orchestrator.Queue;

public interface IReleaseQueueCommandService
{
    Task<ReleaseQueueCommandResult> RunNextReadyItemAsync(string databasePath, CancellationToken cancellationToken = default);

    Task<ReleaseQueueCommandResult> ApproveUsbAsync(string databasePath, CancellationToken cancellationToken = default);

    Task<ReleaseQueueCommandResult> RetryFailedAsync(string databasePath, CancellationToken cancellationToken = default);

    Task<ReleaseQueueCommandResult> RetryFailedAsync(
        string databasePath,
        Func<ReleaseQueueItem, bool> predicate,
        CancellationToken cancellationToken = default);

    Task<ReleaseQueueCommandResult> PrepareQueueAsync(
        string databasePath,
        string workspaceRoot,
        IReadOnlyList<RepositoryPortfolioItem> portfolioItems,
        string? scopeKey = null,
        string? scopeDisplayName = null,
        CancellationToken cancellationToken = default);
}
