using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Orchestrator.Portfolio;

public interface IRepositoryGitQuickActionExecutionService
{
    Task<RepositoryGitQuickActionExecutionResult> ExecuteAsync(
        string repositoryRoot,
        RepositoryGitQuickAction action,
        CancellationToken cancellationToken = default);
}
