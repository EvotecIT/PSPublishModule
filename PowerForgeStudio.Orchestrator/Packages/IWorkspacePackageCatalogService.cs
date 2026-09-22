using PowerForgeStudio.Domain.Packages;

namespace PowerForgeStudio.Orchestrator.Packages;

/// <summary>Reads the published, credential-free package snapshot.</summary>
public interface IWorkspacePackageCatalogService
{
    Task<WorkspacePackageSnapshot> ReadAsync(CancellationToken cancellationToken = default);
}
