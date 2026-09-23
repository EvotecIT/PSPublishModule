using PowerForge;
using PowerForgeStudio.Orchestrator.Catalog;

namespace PowerForgeStudio.Orchestrator.Portfolio;

internal sealed class GitRemoteResolver : IGitRemoteResolver
{
    private readonly Func<string, string, CancellationToken, Task<GitCommandResult>> _getRemoteUrlAsync;

    public GitRemoteResolver()
        : this((repositoryRoot, remoteName, cancellationToken) => new GitClient().GetRemoteUrlAsync(repositoryRoot, remoteName, cancellationToken))
    {
    }

    internal GitRemoteResolver(Func<string, string, CancellationToken, Task<GitCommandResult>> getRemoteUrlAsync)
    {
        _getRemoteUrlAsync = getRemoteUrlAsync;
    }

    public async Task<string?> ResolveOriginUrlAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot) || !WorktreeDetector.IsGitRepository(repositoryRoot))
        {
            return null;
        }

        try
        {
            var result = await _getRemoteUrlAsync(repositoryRoot, "origin", cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result.Succeeded
                ? result.StdOut.Trim()
                : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
