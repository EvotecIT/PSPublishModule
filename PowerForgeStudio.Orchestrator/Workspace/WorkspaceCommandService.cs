using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Orchestrator.Workspace;

public sealed class WorkspaceCommandService
{
    private readonly IReleaseQueueCommandService _queueCommandService;
    private readonly WorkspaceSnapshotQueryService _snapshotQueryService;

    public WorkspaceCommandService()
        : this(new ReleaseQueueCommandService(), new WorkspaceSnapshotQueryService())
    {
    }

    public WorkspaceCommandService(
        IReleaseQueueCommandService queueCommandService,
        WorkspaceSnapshotQueryService snapshotQueryService)
    {
        _queueCommandService = queueCommandService;
        _snapshotQueryService = snapshotQueryService;
    }

    public Task<ReleaseQueueCommandResult> PrepareQueueAsync(
        WorkspaceSnapshot snapshot,
        string databasePath,
        string? familySelector = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        if (string.IsNullOrWhiteSpace(familySelector))
        {
            return _queueCommandService.PrepareQueueAsync(
                databasePath,
                snapshot.WorkspaceRoot,
                snapshot.PortfolioItems,
                cancellationToken: cancellationToken);
        }

        var family = _snapshotQueryService.FindFamily(snapshot, familySelector);
        if (family is null)
        {
            throw new InvalidOperationException($"No repository family matched '{familySelector}'.");
        }

        var familyItems = snapshot.PortfolioItems
            .Where(item => string.Equals(item.FamilyKey, family.FamilyKey, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return _queueCommandService.PrepareQueueAsync(
            databasePath,
            snapshot.WorkspaceRoot,
            familyItems,
            scopeKey: family.FamilyKey,
            scopeDisplayName: family.DisplayName,
            cancellationToken: cancellationToken);
    }

    public Task<ReleaseQueueCommandResult> RunNextReadyItemAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
        => _queueCommandService.RunNextReadyItemAsync(databasePath, cancellationToken);

    public Task<ReleaseQueueCommandResult> ApproveUsbAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
        => _queueCommandService.ApproveUsbAsync(databasePath, cancellationToken);

    public Task<ReleaseQueueCommandResult> RetryFailedAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
        => _queueCommandService.RetryFailedAsync(databasePath, cancellationToken);

    public Task<ReleaseQueueCommandResult> RetryFailedAsync(
        WorkspaceSnapshot snapshot,
        string databasePath,
        string? familySelector = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        if (string.IsNullOrWhiteSpace(familySelector))
        {
            return _queueCommandService.RetryFailedAsync(databasePath, cancellationToken);
        }

        var family = _snapshotQueryService.FindFamily(snapshot, familySelector);
        if (family is null)
        {
            throw new InvalidOperationException($"No repository family matched '{familySelector}'.");
        }

        var familyRootPaths = snapshot.PortfolioItems
            .Where(item => string.Equals(item.FamilyKey, family.FamilyKey, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.RootPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return _queueCommandService.RetryFailedAsync(
            databasePath,
            item => familyRootPaths.Contains(item.RootPath),
            cancellationToken);
    }
}
