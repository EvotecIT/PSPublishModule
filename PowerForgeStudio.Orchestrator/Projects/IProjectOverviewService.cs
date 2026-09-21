using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Domain.Projects;

namespace PowerForgeStudio.Orchestrator.Projects;

public interface IProjectOverviewService
{
    Task<ProjectOverviewSnapshot> InspectAsync(
        RepositoryCatalogEntry repository,
        string workingCopyRoot,
        ProjectGitStatus git,
        CancellationToken cancellationToken = default);
}
