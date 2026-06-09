using PowerForge;

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
        if (string.IsNullOrWhiteSpace(repositoryRoot) || !Directory.Exists(repositoryRoot))
        {
            return null;
        }

        try
        {
            var result = await _getRemoteUrlAsync(repositoryRoot, "origin", cancellationToken).ConfigureAwait(false);
            return result.Succeeded
                ? result.StdOut.Trim()
                : null;
        }
        catch
        {
            return null;
        }
    }
}
