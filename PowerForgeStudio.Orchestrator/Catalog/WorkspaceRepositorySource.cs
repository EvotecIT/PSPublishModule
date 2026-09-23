using PowerForgeStudio.Domain.Catalog;

namespace PowerForgeStudio.Orchestrator.Catalog;

/// <summary>Asynchronous project discovery for a workspace, checkout, or build-enabled local folder.</summary>
public interface IWorkspaceRepositorySource
{
    Task<IReadOnlyList<RepositoryCatalogEntry>> DiscoverAsync(string root, CancellationToken cancellationToken = default);
}

public sealed class WorkspaceRepositorySource : IWorkspaceRepositorySource
{
    public Task<IReadOnlyList<RepositoryCatalogEntry>> DiscoverAsync(string root, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<RepositoryCatalogEntry>>(() =>
        {
            return Discover(root, new RepositoryCatalogScanner(), cancellationToken);
        }, cancellationToken);

    internal static IReadOnlyList<RepositoryCatalogEntry> Discover(string root, RepositoryCatalogScanner scanner,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        if (WorktreeDetector.IsGitRepository(root))
            return [scanner.InspectRepository(root, cancellationToken: cancellationToken)];
        // A workspace can contain many child projects. Do not mistake one child's Build folder
        // for a build contract owned by the workspace root itself.
        var rootEntry = scanner.InspectRepository(root, includeImmediateChildBuildFolders: false,
            cancellationToken: cancellationToken);
        return rootEntry.IsReleaseManaged
            ? [rootEntry]
            : Directory.EnumerateDirectories(root)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Where(static path => !string.Equals(Path.GetFileName(path), "_worktrees", StringComparison.OrdinalIgnoreCase))
                .Select(path => scanner.InspectRepository(path,
                    includeImmediateChildBuildFolders: WorktreeDetector.IsGitRepository(path),
                    cancellationToken: cancellationToken))
                .Where(IsDiscoverableProject)
                .ToArray();
    }

    internal static bool IsDiscoverableProject(RepositoryCatalogEntry entry)
        => entry.IsReleaseManaged || WorktreeDetector.IsGitRepository(entry.RootPath);
}
