using System.Net;
using PowerForgeStudio.Domain.Activity;
using PowerForgeStudio.Domain.Automation;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Orchestrator.Automation;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Hub;
using PowerForgeStudio.Orchestrator.Portfolio;

namespace PowerForgeStudio.Orchestrator.Activity;

/// <summary>
/// Builds the cross-project attention inbox without running builds, scripts, schedules, or publication steps.
/// Existing owner services retain all readiness and provider classifications.
/// </summary>
public sealed class WorkspaceActivityInventoryService : IWorkspaceActivityInventoryService, IDisposable
{
    private readonly RepositoryCatalogScanner _catalog;
    private readonly RepositoryPortfolioService _portfolio;
    private readonly GitHubInboxService _gitHubInbox;
    private readonly RepositoryReleaseDriftService _releaseDrift;
    private readonly RepositoryReleaseInboxService _releaseInbox;
    private readonly IWorkspaceAutomationInventoryService _automations;
    private readonly IGitHubProjectService _gitHubProjects;
    private readonly bool _ownsGitHubServices;

    public WorkspaceActivityInventoryService()
        : this(
            new RepositoryCatalogScanner(),
            new RepositoryPortfolioService(),
            new GitHubInboxService(),
            new RepositoryReleaseDriftService(),
            new RepositoryReleaseInboxService(),
            new WorkspaceAutomationInventoryService(),
            new GitHubProjectService(),
            ownsGitHubServices: true)
    {
    }

    internal WorkspaceActivityInventoryService(
        RepositoryCatalogScanner catalog,
        RepositoryPortfolioService portfolio,
        GitHubInboxService gitHubInbox,
        RepositoryReleaseDriftService releaseDrift,
        RepositoryReleaseInboxService releaseInbox,
        IWorkspaceAutomationInventoryService automations,
        IGitHubProjectService gitHubProjects,
        bool ownsGitHubServices = false)
    {
        _catalog = catalog;
        _portfolio = portfolio;
        _gitHubInbox = gitHubInbox;
        _releaseDrift = releaseDrift;
        _releaseInbox = releaseInbox;
        _automations = automations;
        _gitHubProjects = gitHubProjects;
        _ownsGitHubServices = ownsGitHubServices;
    }

    public async Task<WorkspaceActivitySnapshot> InspectAsync(
        string workspaceRoot,
        WorkspaceActivityOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        options ??= new WorkspaceActivityOptions();
        if (options.MaxGitHubRepositories < 0 || options.MaxIssuesPerRepository < 0 || options.MaxEntries < 1
            || options.GitHubTimeoutSeconds < 1)
            throw new ArgumentOutOfRangeException(nameof(options));

        var root = Path.GetFullPath(workspaceRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var inspectedAt = DateTimeOffset.UtcNow;

        var automationTask = InspectAutomationsSafelyAsync(root, cancellationToken);
        var entries = await Task.Run(() => _catalog.Scan(root), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var managed = entries.Where(static entry => entry.IsReleaseManaged).ToArray();
        var portfolio = await Task.Run(() => _portfolio.BuildPortfolio(managed), cancellationToken).ConfigureAwait(false);
        using var gitHubDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        gitHubDeadline.CancelAfter(TimeSpan.FromSeconds(options.GitHubTimeoutSeconds));
        IReadOnlyList<RepositoryPortfolioItem> enriched;
        var gitHubTimedOut = false;
        try
        {
            enriched = await _gitHubInbox.PopulateInboxAsync(
                portfolio,
                new GitHubInboxOptions { MaxRepositories = options.MaxGitHubRepositories },
                gitHubDeadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            gitHubTimedOut = true;
            enriched = portfolio.Select(static item => item with
            {
                GitHubInbox = new RepositoryGitHubInbox(
                    RepositoryGitHubInboxStatus.NotProbed, null, null, null, null, null, null, null, null,
                    "GitHub refresh timed out.", "The bounded Activity provider deadline elapsed.")
            }).ToArray();
        }
        enriched = _releaseDrift.PopulateReleaseDrift(enriched);

        var activity = BuildPortfolioEntries(enriched, inspectedAt);
        var sources = new List<WorkspaceActivitySourceState>
        {
            new("Local workspace", "Available", activity.Count(static entry => entry.Provider == "PowerForge portfolio"), inspectedAt,
                $"Inspected {managed.Length} release-managed repositories without running build scripts.")
        };

        GitHubIssueResult gitHub;
        if (gitHubTimedOut)
        {
            gitHub = GitHubTimeoutResult(options.GitHubTimeoutSeconds);
        }
        else
        {
            try
            {
                gitHub = await BuildIssueEntriesAsync(enriched, options, inspectedAt, gitHubDeadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                gitHub = GitHubTimeoutResult(options.GitHubTimeoutSeconds);
            }
        }
        activity.AddRange(gitHub.Entries);
        sources.Add(gitHub.Source with
        {
            EntryCount = activity.Count(static entry => entry.Provider == "GitHub")
        });

        var automation = await automationTask.ConfigureAwait(false);
        activity.AddRange(BuildAutomationEntries(automation.Snapshot, inspectedAt));
        sources.AddRange(automation.Sources);

        var ordered = activity
            .GroupBy(static entry => entry.Id, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static entry => SeverityOrder(entry.Severity))
            .ThenByDescending(static entry => entry.ObservedAtUtc)
            .ThenBy(static entry => entry.Project, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var displayed = ordered
            .Take(options.MaxEntries)
            .ToArray();

        return new WorkspaceActivitySnapshot(DateTimeOffset.UtcNow, displayed, sources, managed.Length, ordered.Length);
    }

    private List<WorkspaceActivityEntry> BuildPortfolioEntries(
        IReadOnlyList<RepositoryPortfolioItem> portfolio,
        DateTimeOffset observedAt)
    {
        var result = new List<WorkspaceActivityEntry>();
        var inboxItems = _releaseInbox.BuildInbox(portfolio, queueSession: null, maxItems: int.MaxValue);
        foreach (var item in inboxItems.Where(static item => item.Badge != "Ready Today"))
        {
            var kind = item.Badge.Contains("Release", StringComparison.OrdinalIgnoreCase)
                       || item.Badge.Contains("Publish", StringComparison.OrdinalIgnoreCase)
                       || item.Badge.Contains("Verify", StringComparison.OrdinalIgnoreCase)
                ? "Release"
                : item.Badge == "GitHub" ? "GitHub" : "Workspace";
            result.Add(new WorkspaceActivityEntry(
                $"portfolio:{NormalizeId(item.RootPath)}:{item.Badge}", item.RepositoryName, kind,
                item.Badge == "Failed" ? "Critical" : "Warning", item.Badge, item.Title, item.Detail,
                item.Badge == "GitHub" ? "GitHub" : "PowerForge portfolio", observedAt,
                item.RootPath, item.RootPath, IsActionable: true));
        }

        foreach (var item in portfolio)
        {
            var inbox = item.GitHubInbox;
            if (inbox?.LatestWorkflowFailed == true)
            {
                result.Add(new WorkspaceActivityEntry(
                    $"ci:{NormalizeId(item.RootPath)}", item.Name, "CI", "Critical", "Failed",
                    "Latest GitHub workflow failed", inbox.Summary, "GitHub", observedAt,
                    inbox.RepositorySlug ?? item.RootPath, GitHubUrl(inbox.RepositorySlug, "actions"), IsActionable: true));
            }
            if ((inbox?.OpenPullRequestCount ?? 0) > 0)
            {
                result.Add(new WorkspaceActivityEntry(
                    $"prs:{NormalizeId(item.RootPath)}", item.Name, "Pull request", "Review", "Open",
                    $"{inbox!.OpenPullRequestCount} pull request(s) need review", inbox.Summary, "GitHub", observedAt,
                    inbox.RepositorySlug ?? item.RootPath, GitHubUrl(inbox.RepositorySlug, "pulls"), IsActionable: true));
            }
        }
        return result;
    }

    private async Task<GitHubIssueResult> BuildIssueEntriesAsync(
        IReadOnlyList<RepositoryPortfolioItem> portfolio,
        WorkspaceActivityOptions options,
        DateTimeOffset observedAt,
        CancellationToken token)
    {
        var candidates = portfolio
            .Where(static item => !string.IsNullOrWhiteSpace(item.GitHubInbox?.RepositorySlug))
            .Take(options.MaxGitHubRepositories)
            .ToArray();
        var entries = new List<WorkspaceActivityEntry>();
        var successful = 0;
        var unavailable = 0;
        var authenticationRequired = false;
        var accessDenied = false;
        var rateLimited = false;

        foreach (var item in candidates)
        {
            token.ThrowIfCancellationRequested();
            var slug = item.GitHubInbox!.RepositorySlug!;
            try
            {
                var page = await _gitHubProjects.FetchIssuesAsync(slug, cancellationToken: token).ConfigureAwait(false);
                successful++;
                foreach (var issue in page.Items.Take(options.MaxIssuesPerRepository))
                {
                    entries.Add(new WorkspaceActivityEntry(
                        $"issue:{slug}:{issue.Number}", item.Name, "Issue", "Review", "Open",
                        $"#{issue.Number} {issue.Title}",
                        BuildIssueDetail(issue, page.HasMore), "GitHub", observedAt, slug,
                        SafeGitHubUrl(issue.HtmlUrl) ?? $"https://github.com/{slug}/issues/{issue.Number}", IsActionable: true));
                }
            }
            catch (GitHubAccessException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationRequired = true;
            }
            catch (GitHubAccessException exception) when (exception.StatusCode == HttpStatusCode.Forbidden)
            {
                accessDenied = true;
            }
            catch (GitHubAccessException exception) when (exception.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rateLimited = true;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                unavailable++;
            }
            catch (Exception) when (!token.IsCancellationRequested)
            {
                unavailable++;
            }
        }

        var deferred = Math.Max(0, portfolio.Count - candidates.Length);
        var providerGaps = portfolio.Count(static item => item.GitHubInbox?.Status is
            RepositoryGitHubInboxStatus.NotProbed or RepositoryGitHubInboxStatus.Unavailable);
        var state = authenticationRequired && successful == 0 ? "Authentication required"
            : accessDenied && successful == 0 ? "Access denied"
            : rateLimited && successful == 0 ? "Rate limited"
            : successful > 0 && (unavailable > 0 || authenticationRequired || accessDenied || rateLimited || deferred > 0 || providerGaps > 0) ? "Partial"
            : successful > 0 ? "Available"
            : candidates.Length == 0 && portfolio.Count > 0 ? "Deferred"
            : candidates.Length == 0 ? "Absent"
            : "Unavailable";
        var message = state switch
        {
            "Authentication required" => "GitHub issue reads require a valid provider credential.",
            "Access denied" => "GitHub denied issue access. The credential may not cover these repositories.",
            "Rate limited" => "GitHub refused or rate-limited issue reads. Retry after the provider recovers.",
            "Partial" => $"Issue evidence loaded for {successful} repository(s); {Math.Max(unavailable + deferred, providerGaps)} had unavailable or deferred GitHub signals.",
            "Available" => $"Open issues were observed for {successful} repository(s).",
            "Deferred" => "GitHub evidence was intentionally deferred by the bounded repository limit.",
            "Absent" => "No release-managed GitHub repositories were detected.",
            _ => "GitHub issue evidence could not be observed. Empty results are not assumed."
        };
        return new GitHubIssueResult(entries,
            new WorkspaceActivitySourceState("GitHub", state, entries.Count, successful > 0 ? observedAt : null, message));
    }

    private async Task<AutomationInspectionResult> InspectAutomationsSafelyAsync(string root, CancellationToken token)
    {
        try
        {
            var snapshot = await _automations.InspectAsync(root, token).ConfigureAwait(false);
            var relevantCounts = snapshot.Entries
                .Where(static entry => entry.IsRelevant)
                .GroupBy(static entry => entry.Provider, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.OrdinalIgnoreCase);
            var states = snapshot.Sources.Select(source => new WorkspaceActivitySourceState(
                source.Provider, source.State,
                relevantCounts.GetValueOrDefault(source.Provider),
                snapshot.InspectedAtUtc, source.Message)).ToArray();
            return new AutomationInspectionResult(snapshot, states);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new AutomationInspectionResult(
                new WorkspaceAutomationSnapshot(DateTimeOffset.UtcNow, [], []),
                [new WorkspaceActivitySourceState("Automations", "Unavailable", 0, null,
                    "Automation evidence could not be read. No schedule was changed or executed.")]);
        }
    }

    private static IEnumerable<WorkspaceActivityEntry> BuildAutomationEntries(
        WorkspaceAutomationSnapshot snapshot,
        DateTimeOffset fallbackObservedAt)
    {
        foreach (var entry in snapshot.Entries.Where(static item => item.IsRelevant))
        {
            var severity = entry.State == "Failed" ? "Critical" : entry.NeedsAttention ? "Warning" : "Information";
            var observed = entry.LastRunAt ?? snapshot.InspectedAtUtc;
            if (observed == default) observed = fallbackObservedAt;
            yield return new WorkspaceActivityEntry(
                $"automation:{entry.Provider}:{entry.Id}", entry.ProjectDisplay, "Schedule", severity, entry.State,
                entry.Name, $"{entry.Schedule}. {entry.LastResult}. {entry.Detail}", entry.Provider, observed,
                entry.SourcePath, File.Exists(entry.SourcePath) ? entry.SourcePath : null, entry.NeedsAttention);
        }
    }

    private static string BuildIssueDetail(GitHubIssue issue, bool hasMore)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(issue.AuthorLogin)) parts.Add($"Opened by {issue.AuthorLogin}.");
        if (issue.Labels.Count > 0) parts.Add($"Labels: {issue.LabelDisplay}.");
        if (issue.Assignees.Count > 0) parts.Add($"Assigned to {issue.AssigneeDisplay}.");
        if (hasMore) parts.Add("The provider reports more issue pages than this bounded view displays.");
        return parts.Count == 0 ? "Open GitHub issue." : string.Join(" ", parts);
    }

    private static string? GitHubUrl(string? slug, string area)
        => string.IsNullOrWhiteSpace(slug) ? null : $"https://github.com/{slug}/{area}";

    private static string? SafeGitHubUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && uri.Scheme == Uri.UriSchemeHttps
           && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            ? uri.GetLeftPart(UriPartial.Path)
            : null;

    private static string NormalizeId(string value)
        => value.Replace('\\', '/').Trim('/');

    private static int SeverityOrder(string severity) => severity switch
    {
        "Critical" => 0,
        "Warning" => 1,
        "Review" => 2,
        _ => 3
    };

    private static GitHubIssueResult GitHubTimeoutResult(int timeoutSeconds)
        => new([], new WorkspaceActivitySourceState(
            "GitHub", "Unavailable", 0, null,
            $"The bounded {timeoutSeconds}-second GitHub refresh timed out. Empty results are not assumed."));

    public void Dispose()
    {
        if (!_ownsGitHubServices) return;
        _gitHubInbox.Dispose();
        if (_gitHubProjects is IDisposable disposable) disposable.Dispose();
    }

    private sealed record GitHubIssueResult(
        IReadOnlyList<WorkspaceActivityEntry> Entries,
        WorkspaceActivitySourceState Source);

    private sealed record AutomationInspectionResult(
        WorkspaceAutomationSnapshot Snapshot,
        IReadOnlyList<WorkspaceActivitySourceState> Sources);
}
