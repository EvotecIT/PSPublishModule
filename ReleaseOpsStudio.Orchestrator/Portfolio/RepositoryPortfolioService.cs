using ReleaseOpsStudio.Domain.Catalog;
using ReleaseOpsStudio.Domain.Portfolio;
using ReleaseOpsStudio.Orchestrator.Git;

namespace ReleaseOpsStudio.Orchestrator.Portfolio;

public sealed class RepositoryPortfolioService
{
    private readonly GitRepositoryInspector _gitRepositoryInspector;

    public RepositoryPortfolioService()
        : this(new GitRepositoryInspector()) {
    }

    public RepositoryPortfolioService(GitRepositoryInspector gitRepositoryInspector)
    {
        _gitRepositoryInspector = gitRepositoryInspector;
    }

    public IReadOnlyList<RepositoryPortfolioItem> BuildPortfolio(IEnumerable<RepositoryCatalogEntry> entries)
    {
        var items = new List<RepositoryPortfolioItem>();
        foreach (var entry in entries)
        {
            var gitSnapshot = _gitRepositoryInspector.Inspect(entry.RootPath);
            var readiness = AssessReadiness(entry, gitSnapshot);
            items.Add(new RepositoryPortfolioItem(entry, gitSnapshot, readiness));
        }

        return items.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public RepositoryPortfolioSummary BuildSummary(IEnumerable<RepositoryPortfolioItem> items)
    {
        var materialized = items.ToList();
        return new RepositoryPortfolioSummary(
            TotalRepositories: materialized.Count,
            ReadyRepositories: materialized.Count(item => item.ReadinessKind == RepositoryReadinessKind.Ready),
            AttentionRepositories: materialized.Count(item => item.ReadinessKind == RepositoryReadinessKind.Attention),
            BlockedRepositories: materialized.Count(item => item.ReadinessKind == RepositoryReadinessKind.Blocked),
            DirtyRepositories: materialized.Count(item => item.Git.IsDirty),
            BehindRepositories: materialized.Count(item => item.Git.BehindCount > 0),
            WorktreeRepositories: materialized.Count(item => item.Repository.IsWorktree),
            GitHubAttentionRepositories: materialized.Count(item => item.GitHubInbox?.Status == RepositoryGitHubInboxStatus.Attention),
            OpenPullRequests: materialized.Sum(item => item.GitHubInbox?.OpenPullRequestCount ?? 0),
            ReleaseDriftAttentionRepositories: materialized.Count(item => item.ReleaseDrift?.Status == RepositoryReleaseDriftStatus.Attention));
    }

    private static RepositoryReadiness AssessReadiness(RepositoryCatalogEntry entry, RepositoryGitSnapshot gitSnapshot)
    {
        if (!entry.IsReleaseManaged)
        {
            return new RepositoryReadiness(RepositoryReadinessKind.Blocked, "No release contract detected.");
        }

        if (!gitSnapshot.IsGitRepository)
        {
            return new RepositoryReadiness(RepositoryReadinessKind.Blocked, "Git metadata unavailable.");
        }

        if (string.IsNullOrWhiteSpace(gitSnapshot.BranchName) || string.Equals(gitSnapshot.BranchName, "(detached)", StringComparison.OrdinalIgnoreCase))
        {
            return new RepositoryReadiness(RepositoryReadinessKind.Attention, "Detached HEAD or missing branch name.");
        }

        if (gitSnapshot.IsDirty)
        {
            return new RepositoryReadiness(
                RepositoryReadinessKind.Attention,
                $"{gitSnapshot.TrackedChangeCount} tracked and {gitSnapshot.UntrackedChangeCount} untracked local changes.");
        }

        if (gitSnapshot.BehindCount > 0)
        {
            return new RepositoryReadiness(RepositoryReadinessKind.Attention, $"Behind upstream by {gitSnapshot.BehindCount} commit(s).");
        }

        if (string.IsNullOrWhiteSpace(gitSnapshot.UpstreamBranch))
        {
            return new RepositoryReadiness(RepositoryReadinessKind.Attention, "No upstream branch configured.");
        }

        if (gitSnapshot.AheadCount > 0)
        {
            return new RepositoryReadiness(RepositoryReadinessKind.Ready, $"Clean with {gitSnapshot.AheadCount} local commit(s) ahead.");
        }

        return new RepositoryReadiness(RepositoryReadinessKind.Ready, "Clean and ready for plan mode.");
    }
}
