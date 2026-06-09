using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Portfolio;
using PowerForgeStudio.Orchestrator.Storage;

namespace PowerForgeStudio.Orchestrator.Workspace;

public sealed class WorkspaceGitCommandService
{
    private readonly WorkspaceSnapshotQueryService _snapshotQueryService;
    private readonly RepositoryGitRemediationService _gitRemediationService;
    private readonly RepositoryGitQuickActionService _gitQuickActionService;
    private readonly IRepositoryGitQuickActionExecutionService _gitQuickActionExecutionService;

    public WorkspaceGitCommandService()
        : this(
            new WorkspaceSnapshotQueryService(),
            new RepositoryGitRemediationService(),
            new RepositoryGitQuickActionService(),
            new RepositoryGitQuickActionExecutionService())
    {
    }

    public WorkspaceGitCommandService(
        WorkspaceSnapshotQueryService snapshotQueryService,
        RepositoryGitRemediationService gitRemediationService,
        RepositoryGitQuickActionService gitQuickActionService,
        IRepositoryGitQuickActionExecutionService gitQuickActionExecutionService)
    {
        _snapshotQueryService = snapshotQueryService;
        _gitRemediationService = gitRemediationService;
        _gitQuickActionService = gitQuickActionService;
        _gitQuickActionExecutionService = gitQuickActionExecutionService;
    }

    public async Task<RepositoryGitActionCatalog> GetActionCatalogAsync(
        WorkspaceSnapshot snapshot,
        string databasePath,
        string repositorySelector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositorySelector);

        var repository = _snapshotQueryService.FindRepository(snapshot, repositorySelector)
            ?? throw new InvalidOperationException($"No repository matched '{repositorySelector}'.");
        var latestReceipt = await LoadLatestReceiptAsync(databasePath, repository.RootPath, cancellationToken).ConfigureAwait(false);
        return BuildCatalog(repository, latestReceipt);
    }

    public async Task<WorkspaceGitActionCommandResult> ExecuteActionAsync(
        WorkspaceSnapshot snapshot,
        string databasePath,
        string repositorySelector,
        string actionSelector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositorySelector);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionSelector);

        var repository = _snapshotQueryService.FindRepository(snapshot, repositorySelector)
            ?? throw new InvalidOperationException($"No repository matched '{repositorySelector}'.");
        var latestReceipt = await LoadLatestReceiptAsync(databasePath, repository.RootPath, cancellationToken).ConfigureAwait(false);
        var catalog = BuildCatalog(repository, latestReceipt);
        var action = ResolveAction(catalog.Actions, actionSelector)
            ?? throw new InvalidOperationException($"No git action matched '{actionSelector}' for {repository.Name}.");

        var execution = await _gitQuickActionExecutionService.ExecuteAsync(repository.RootPath, action, cancellationToken).ConfigureAwait(false);
        var receipt = new RepositoryGitQuickActionReceipt(
            RootPath: repository.RootPath,
            ActionTitle: action.Title,
            ActionKind: action.Kind,
            Payload: action.Payload,
            Succeeded: execution.Succeeded,
            Summary: execution.Summary,
            OutputTail: execution.OutputTail,
            ErrorTail: execution.ErrorTail,
            ExecutedAtUtc: DateTimeOffset.UtcNow);

        await using (await ReleaseStateDatabase.AcquireExclusiveAccessAsync(databasePath, cancellationToken).ConfigureAwait(false))
        {
            var stateDatabase = new ReleaseStateDatabase(databasePath);
            await stateDatabase.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await stateDatabase.PersistGitQuickActionReceiptAsync(receipt, cancellationToken).ConfigureAwait(false);
        }

        var updatedCatalog = BuildCatalog(repository, receipt);
        return new WorkspaceGitActionCommandResult(
            Changed: true,
            Message: execution.Summary,
            Catalog: updatedCatalog,
            SelectedAction: action,
            Receipt: receipt);
    }

    private RepositoryGitActionCatalog BuildCatalog(
        RepositoryPortfolioItem repository,
        RepositoryGitQuickActionReceipt? latestReceipt)
    {
        var remediationSteps = _gitRemediationService.BuildSteps(repository);
        var actions = _gitQuickActionService.BuildActions(repository, remediationSteps);
        return new RepositoryGitActionCatalog(
            RepositoryName: repository.Name,
            RootPath: repository.RootPath,
            FamilyDisplayName: repository.FamilyDisplayName,
            Actions: actions,
            LatestReceipt: latestReceipt);
    }

    private static RepositoryGitQuickAction? ResolveAction(
        IReadOnlyList<RepositoryGitQuickAction> actions,
        string actionSelector)
    {
        if (int.TryParse(actionSelector, out var ordinal))
        {
            if (ordinal >= 1 && ordinal <= actions.Count)
            {
                return actions[ordinal - 1];
            }
        }

        var exactTitle = actions.FirstOrDefault(action =>
            string.Equals(action.Title, actionSelector, StringComparison.OrdinalIgnoreCase));
        if (exactTitle is not null)
        {
            return exactTitle;
        }

        var exactPayload = actions.FirstOrDefault(action =>
            string.Equals(action.Payload, actionSelector, StringComparison.OrdinalIgnoreCase));
        if (exactPayload is not null)
        {
            return exactPayload;
        }

        var partialMatches = actions
            .Where(action =>
                action.Title.Contains(actionSelector, StringComparison.OrdinalIgnoreCase)
                || action.Payload.Contains(actionSelector, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return partialMatches.Length == 1
            ? partialMatches[0]
            : null;
    }

    private static async Task<RepositoryGitQuickActionReceipt?> LoadLatestReceiptAsync(
        string databasePath,
        string rootPath,
        CancellationToken cancellationToken)
    {
        var stateDatabase = new ReleaseStateDatabase(databasePath);
        await stateDatabase.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var receipts = await stateDatabase.LoadGitQuickActionReceiptsAsync(cancellationToken).ConfigureAwait(false);
        return receipts
            .Where(receipt => string.Equals(receipt.RootPath, rootPath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(receipt => receipt.ExecutedAtUtc)
            .FirstOrDefault();
    }
}
