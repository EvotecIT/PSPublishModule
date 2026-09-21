using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Orchestrator.Hub;

/// <summary>Executes one explicitly reviewed GitHub item action against refreshed target evidence.</summary>
public interface IGitHubProjectActionService
{
    Task<GitHubProjectActionReceipt> ExecuteAsync(
        GitHubProjectActionPlan plan,
        CancellationToken cancellationToken = default);
}
