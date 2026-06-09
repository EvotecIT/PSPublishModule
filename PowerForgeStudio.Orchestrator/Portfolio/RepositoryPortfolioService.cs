using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Orchestrator.Git;

namespace PowerForgeStudio.Orchestrator.Portfolio;

public sealed class RepositoryPortfolioService
{
    private readonly GitRepositoryInspector _gitRepositoryInspector;
    private readonly RepositoryGitPreflightService _gitPreflightService;

    public RepositoryPortfolioService()
        : this(new GitRepositoryInspector(), new RepositoryGitPreflightService()) {
    }

    public RepositoryPortfolioService(
        GitRepositoryInspector gitRepositoryInspector,
        RepositoryGitPreflightService gitPreflightService)
    {
        _gitRepositoryInspector = gitRepositoryInspector;
        _gitPreflightService = gitPreflightService;
    }

    public IReadOnlyList<RepositoryPortfolioItem> BuildPortfolio(IEnumerable<RepositoryCatalogEntry> entries)
    {
        var items = new List<RepositoryPortfolioItem>();
        foreach (var entry in entries)
        {
            var gitSnapshot = _gitRepositoryInspector.Inspect(entry.RootPath);
            var gitDiagnostics = _gitPreflightService.Assess(entry, gitSnapshot);
            gitSnapshot = gitSnapshot with {
                Diagnostics = gitDiagnostics
            };
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

        var blockedDiagnostic = gitSnapshot.GitDiagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == RepositoryGitDiagnosticSeverity.Blocked);
        if (blockedDiagnostic is not null)
        {
            return new RepositoryReadiness(RepositoryReadinessKind.Blocked, blockedDiagnostic.Summary);
        }

        var attentionDiagnostic = gitSnapshot.GitDiagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == RepositoryGitDiagnosticSeverity.Attention);
        if (attentionDiagnostic is not null)
        {
            return new RepositoryReadiness(RepositoryReadinessKind.Attention, attentionDiagnostic.Summary);
        }

        if (gitSnapshot.AheadCount > 0)
        {
            return new RepositoryReadiness(RepositoryReadinessKind.Ready, $"Clean with {gitSnapshot.AheadCount} local commit(s) ahead.");
        }

        return new RepositoryReadiness(RepositoryReadinessKind.Ready, "Clean and ready for plan mode.");
    }
}
