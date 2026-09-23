using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Orchestrator.Catalog;
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
        => BuildPortfolioAsync(entries, CancellationToken.None).GetAwaiter().GetResult();

    public async Task<IReadOnlyList<RepositoryPortfolioItem>> BuildPortfolioAsync(
        IEnumerable<RepositoryCatalogEntry> entries, CancellationToken cancellationToken = default)
    {
        var items = new List<RepositoryPortfolioItem>();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var gitSnapshot = await _gitRepositoryInspector.InspectAsync(entry.RootPath, cancellationToken)
                .ConfigureAwait(false);
            // A build-enabled local folder has no Git contract. Keep a broken Git checkout
            // diagnostic, but do not turn an intentionally local project into a Git warning.
            var gitDiagnostics = WorktreeDetector.IsGitRepository(entry.RootPath)
                ? _gitPreflightService.Assess(entry, gitSnapshot)
                : [];
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
            return WorktreeDetector.IsGitRepository(entry.RootPath)
                ? new RepositoryReadiness(RepositoryReadinessKind.Blocked, "Git metadata unavailable.")
                : new RepositoryReadiness(RepositoryReadinessKind.Ready, "Local build project; Git workflows are unavailable.");
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
