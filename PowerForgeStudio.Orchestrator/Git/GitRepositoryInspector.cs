using PowerForge;
using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Orchestrator.Git;

public sealed class GitRepositoryInspector
{
    private readonly GitClient _gitClient;

    public GitRepositoryInspector()
        : this(new GitClient())
    {
    }

    public GitRepositoryInspector(GitClient gitClient)
    {
        _gitClient = gitClient;
    }

    public RepositoryGitSnapshot Inspect(string repositoryRoot)
        => InspectAsync(repositoryRoot, CancellationToken.None).GetAwaiter().GetResult();

    public async Task<RepositoryGitSnapshot> InspectAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        if (!Directory.Exists(repositoryRoot))
        {
            return new RepositoryGitSnapshot(false, null, null, 0, 0, 0, 0);
        }

        try
        {
            var snapshot = await _gitClient.GetStatusAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
            return new RepositoryGitSnapshot(
                snapshot.IsGitRepository,
                snapshot.BranchName,
                snapshot.UpstreamBranch,
                snapshot.AheadCount,
                snapshot.BehindCount,
                snapshot.TrackedChangeCount,
                snapshot.UntrackedChangeCount);
        }
        catch
        {
            return new RepositoryGitSnapshot(false, null, null, 0, 0, 0, 0);
        }
    }
}

