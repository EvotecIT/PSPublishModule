using PowerForgeStudio.Domain.Catalog;

namespace PowerForgeStudio.Orchestrator.Catalog;

/// <summary>Asynchronous repository discovery for a selected workspace or single checkout.</summary>
public interface IWorkspaceRepositorySource
{
    Task<IReadOnlyList<RepositoryCatalogEntry>> DiscoverAsync(string root, CancellationToken cancellationToken = default);
}

public sealed class WorkspaceRepositorySource : IWorkspaceRepositorySource
{
    public Task<IReadOnlyList<RepositoryCatalogEntry>> DiscoverAsync(string root, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<RepositoryCatalogEntry>>(() =>
        {
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
            var scanner = new RepositoryCatalogScanner();
            return WorktreeDetector.IsGitRepository(root)
                ? [scanner.InspectRepository(root)]
                : scanner.Scan(root).Where(entry => WorktreeDetector.IsGitRepository(entry.RootPath)).ToArray();
        }, cancellationToken);
}
