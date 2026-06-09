using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Wpf.ViewModels;

public interface IRepositoryGitQuickActionWorkflowService
{
    Task<RepositoryGitQuickActionWorkflowResult> ExecuteAsync(
        string databasePath,
        RepositoryPortfolioItem? repository,
        RepositoryGitQuickAction? action,
        CancellationToken cancellationToken = default);
}
