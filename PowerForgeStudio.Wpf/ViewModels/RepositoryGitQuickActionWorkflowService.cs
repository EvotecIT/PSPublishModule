using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Orchestrator.Portfolio;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed class RepositoryGitQuickActionWorkflowService : IRepositoryGitQuickActionWorkflowService
{
    private readonly IRepositoryGitQuickActionExecutionService _executionService;
    private readonly IRepositoryGitQuickActionReceiptPersistenceService _persistenceService;

    public RepositoryGitQuickActionWorkflowService()
        : this(
            new RepositoryGitQuickActionExecutionService(),
            new RepositoryGitQuickActionReceiptPersistenceService())
    {
    }

    public RepositoryGitQuickActionWorkflowService(
        IRepositoryGitQuickActionExecutionService executionService,
        IRepositoryGitQuickActionReceiptPersistenceService persistenceService)
    {
        _executionService = executionService ?? throw new ArgumentNullException(nameof(executionService));
        _persistenceService = persistenceService ?? throw new ArgumentNullException(nameof(persistenceService));
    }

    public async Task<RepositoryGitQuickActionWorkflowResult> ExecuteAsync(
        string databasePath,
        RepositoryPortfolioItem? repository,
        RepositoryGitQuickAction? action,
        CancellationToken cancellationToken = default)
    {
        if (repository is null)
        {
            return new RepositoryGitQuickActionWorkflowResult("Select a repository first.");
        }

        if (action is null)
        {
            return new RepositoryGitQuickActionWorkflowResult("Select a git quick action first.");
        }

        var result = await _executionService.ExecuteAsync(repository.RootPath, action, cancellationToken).ConfigureAwait(false);
        var receipt = new RepositoryGitQuickActionReceipt(
            RootPath: repository.RootPath,
            ActionTitle: action.Title,
            ActionKind: action.Kind,
            Payload: action.Payload,
            Succeeded: result.Succeeded,
            Summary: result.Summary,
            OutputTail: result.OutputTail,
            ErrorTail: result.ErrorTail,
            ExecutedAtUtc: DateTimeOffset.UtcNow);

        await _persistenceService.PersistAsync(databasePath, receipt, cancellationToken).ConfigureAwait(false);

        var tail = string.IsNullOrWhiteSpace(result.ErrorTail)
            ? result.OutputTail
            : result.ErrorTail;
        var statusMessage = string.IsNullOrWhiteSpace(tail)
            ? result.Summary
            : $"{result.Summary} {tail}";

        return new RepositoryGitQuickActionWorkflowResult(
            statusMessage,
            receipt,
            ShouldRefresh: result.Succeeded && action.Kind == RepositoryGitQuickActionKind.GitCommand);
    }
}
