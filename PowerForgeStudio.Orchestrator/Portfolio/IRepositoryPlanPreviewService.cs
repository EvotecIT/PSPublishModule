using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Orchestrator.Portfolio;

/// <summary>Validates or plans the detected contracts for one working copy.</summary>
public interface IRepositoryPlanPreviewService
{
    Task<IReadOnlyList<RepositoryPlanResult>> PlanRepositoryAsync(
        RepositoryCatalogEntry repository, CancellationToken cancellationToken = default);
}
