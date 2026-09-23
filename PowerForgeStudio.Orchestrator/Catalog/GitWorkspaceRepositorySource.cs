using PowerForgeStudio.Domain.Catalog;

namespace PowerForgeStudio.Orchestrator.Catalog;

/// <summary>Discovers immediate Git checkouts for workspace operations that do not need build-contract inspection.</summary>
public sealed class GitWorkspaceRepositorySource : IWorkspaceRepositorySource
{
    public Task<IReadOnlyList<RepositoryCatalogEntry>> DiscoverAsync(
        string root,
        CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<RepositoryCatalogEntry>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);

            var paths = WorktreeDetector.IsGitRepository(root)
                ? [root]
                : Directory.EnumerateDirectories(root)
                    .Where(WorktreeDetector.IsGitRepository)
                    .ToArray();

            return paths.Select(path =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var isWorktree = WorktreeDetector.IsWorktree(path);
                return new RepositoryCatalogEntry(
                    Path.GetFileName(Path.TrimEndingDirectorySeparator(path)),
                    Path.GetFullPath(path),
                    ReleaseRepositoryKind.Unknown,
                    isWorktree ? ReleaseWorkspaceKind.Worktree : ReleaseWorkspaceKind.PrimaryRepository,
                    null, null, isWorktree, false);
            }).ToArray();
        }, cancellationToken);
}
