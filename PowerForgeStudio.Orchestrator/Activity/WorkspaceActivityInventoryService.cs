using System.Net;
using PowerForgeStudio.Domain.Activity;
using PowerForgeStudio.Domain.Automation;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Orchestrator.Automation;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Git;
using PowerForgeStudio.Orchestrator.Hub;
using PowerForgeStudio.Orchestrator.Host;
using PowerForgeStudio.Orchestrator.Portfolio;
using PowerForgeStudio.Orchestrator.Storage;
using PowerForgeStudio.Orchestrator.Workspace;
using PowerForgeStudio.Domain.Queue;

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
    private readonly IReleaseHistoryService _releaseHistory;
    private readonly IGitHubProjectService _gitHubProjects;
    private readonly IWorkspaceExplorerStateStore _explorerState;
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
            new ReleaseHistoryService(PowerForgeStudioHostPaths.GetReleaseHistoryDatabasePath()),
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
        IReleaseHistoryService releaseHistory,
        bool ownsGitHubServices = false,
        IWorkspaceExplorerStateStore? explorerState = null)
    {
        _catalog = catalog;
        _portfolio = portfolio;
        _gitHubInbox = gitHubInbox;
        _releaseDrift = releaseDrift;
        _releaseInbox = releaseInbox;
        _automations = automations;
        _releaseHistory = releaseHistory;
        _gitHubProjects = gitHubProjects;
        _explorerState = explorerState ?? new WorkspaceRootCatalogService();
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
        var journalTask = InspectReleaseJournalSafelyAsync(root, cancellationToken);
        var entries = await Task.Run(() => _catalog.Scan(root), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var managed = entries.Where(static entry => entry.IsReleaseManaged).ToArray();
        var managedPortfolio = await _portfolio.BuildPortfolioAsync(managed, cancellationToken).ConfigureAwait(false);
        // Ordinary repositories are eligible for GitHub evidence too. Avoid a full Git status scan over
        // every project merely to order the bounded remote probe; inspect only selected candidates.
        var unmanagedCandidates = entries.Where(static entry => !entry.IsReleaseManaged
                && GitRepositoryOwnership.HasOwnWorkingCopy(entry.RootPath))
            .Select(static entry => new RepositoryPortfolioItem(entry,
                new RepositoryGitSnapshot(false, null, null, 0, 0, 0, 0),
                new RepositoryReadiness(RepositoryReadinessKind.Blocked, "No release contract detected.")));
        var priorityOrder = PrioritizeGitHubProbes(root, managedPortfolio
            .Where(static item => GitRepositoryOwnership.HasOwnWorkingCopy(item.RootPath))
            .Concat(unmanagedCandidates).ToArray());
        var selectedUnmanaged = priorityOrder.Take(options.MaxGitHubRepositories)
            .Where(static item => !item.Repository.IsReleaseManaged)
            .Select(static item => item.Repository).ToArray();
        using var gitHubDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        gitHubDeadline.CancelAfter(TimeSpan.FromSeconds(options.GitHubTimeoutSeconds));
        IReadOnlyList<RepositoryPortfolioItem> enriched;
        var gitHubTimedOut = false;
        try
        {
            var inspectedUnmanaged = await _portfolio.BuildPortfolioAsync(selectedUnmanaged, gitHubDeadline.Token)
                .ConfigureAwait(false);
            var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var inspectedByPath = inspectedUnmanaged.ToDictionary(static item => item.RootPath, pathComparer);
            var probeOrder = priorityOrder.Select(item => inspectedByPath.GetValueOrDefault(item.RootPath) ?? item).ToArray();
            enriched = await _gitHubInbox.PopulateInboxAsync(
                probeOrder,
                new GitHubInboxOptions { MaxRepositories = options.MaxGitHubRepositories },
                gitHubDeadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            gitHubTimedOut = true;
            enriched = priorityOrder.Select(static item => item with
            {
                GitHubInbox = new RepositoryGitHubInbox(
                    RepositoryGitHubInboxStatus.NotProbed, null, null, null, null, null, null, null, null,
                    "GitHub refresh timed out.", "The bounded Activity provider deadline elapsed.")
            }).ToArray();
        }
        var enrichedByPath = enriched.ToDictionary(static item => item.RootPath,
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var managedEnriched = managedPortfolio.Select(item => enrichedByPath.GetValueOrDefault(item.RootPath) ?? item)
            .ToArray();
        managedEnriched = _releaseDrift.PopulateReleaseDrift(managedEnriched).ToArray();

        var activity = BuildPortfolioEntries(managedEnriched, enriched, inspectedAt);
        var journal = await journalTask.ConfigureAwait(false);
        activity.AddRange(BuildReleaseJournalEntries(journal.Entries));
        var sources = new List<WorkspaceActivitySourceState>
        {
            new("Local workspace", journal.Available ? "Available" : "Partial",
                activity.Count(static entry => entry.Provider is "PowerForge portfolio" or "Release journal"), inspectedAt,
                $"Inspected {managed.Length} release-managed repositories without running build scripts. " +
                (journal.Available ? $"Read {journal.Entries.Count} saved release item(s) from the local journal."
                    : "The local release journal could not be read; saved history may be missing."))
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

        return new WorkspaceActivitySnapshot(DateTimeOffset.UtcNow, displayed, sources, entries.Count, ordered.Length);
    }

    private IReadOnlyList<RepositoryPortfolioItem> PrioritizeGitHubProbes(
        string workspaceRoot, IReadOnlyList<RepositoryPortfolioItem> portfolio)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        HashSet<string> favorites;
        HashSet<string> archived;
        try
        {
            var state = _explorerState.LoadExplorer(workspaceRoot);
            favorites = new HashSet<string>(state.FavoriteProjectRoots, comparer);
            archived = new HashSet<string>(state.ArchivedProjectRoots ?? [], comparer);
        }
        catch
        {
            // A damaged local preference catalog must not prevent workspace evidence from loading.
            favorites = new HashSet<string>(comparer);
            archived = new HashSet<string>(comparer);
        }

        return portfolio
            .OrderBy(item => archived.Contains(item.RootPath) ? 1 : 0)
            .ThenBy(item => favorites.Contains(item.RootPath) ? 0 : 1)
            .ThenBy(item => item.ReadinessKind == RepositoryReadinessKind.Attention || item.Git.IsDirty ? 0 : 1)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private List<WorkspaceActivityEntry> BuildPortfolioEntries(
        IReadOnlyList<RepositoryPortfolioItem> managedPortfolio,
        IReadOnlyList<RepositoryPortfolioItem> gitHubPortfolio,
        DateTimeOffset observedAt)
    {
        var result = new List<WorkspaceActivityEntry>();
        var inboxItems = _releaseInbox.BuildInbox(managedPortfolio, queueSession: null, maxItems: int.MaxValue);
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

        foreach (var item in gitHubPortfolio)
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

    private async Task<ReleaseJournalInspection> InspectReleaseJournalSafelyAsync(string root, CancellationToken token)
    {
        try
        {
            var entries = await _releaseHistory.ListForWorkspaceAsync(root, cancellationToken: token).ConfigureAwait(false);
            return new ReleaseJournalInspection(entries, true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new ReleaseJournalInspection([], false);
        }
    }

    private static IEnumerable<WorkspaceActivityEntry> BuildReleaseJournalEntries(
        IReadOnlyList<WorkspaceReleaseJournalEntry> entries)
    {
        foreach (var entry in entries)
        {
            var severity = entry.Status switch
            {
                ReleaseQueueItemStatus.Failed => "Critical",
                ReleaseQueueItemStatus.Blocked or ReleaseQueueItemStatus.WaitingApproval or ReleaseQueueItemStatus.ReadyToRun => "Warning",
                _ => "Information"
            };
            var detail = StudioOutputSanitizer.Sanitize(entry.Summary);
            if (detail.Length > 1000) detail = detail[..1000] + "…";
            yield return new WorkspaceActivityEntry(
                $"journal:{entry.SessionId}:{NormalizeId(entry.WorkingCopy)}",
                string.IsNullOrWhiteSpace(entry.RepositoryName) ? Path.GetFileName(entry.WorkingCopy) : entry.RepositoryName,
                "Release", severity, entry.Status.ToString(), $"Saved release · {entry.Stage}", detail,
                "Release journal", entry.UpdatedAtUtc, entry.WorkingCopy,
                Directory.Exists(entry.WorkingCopy) ? entry.WorkingCopy : null,
                severity != "Information") { ReleaseSessionId = entry.SessionId };
        }
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

        var deferred = portfolio.Count(static item => item.GitHubInbox?.Status == RepositoryGitHubInboxStatus.NotProbed);
        var withoutOrigin = portfolio.Count(static item => item.GitHubInbox?.Summary == "GitHub origin not detected.");
        var providerGaps = portfolio.Count(static item => item.GitHubInbox?.Status is
            RepositoryGitHubInboxStatus.NotProbed or RepositoryGitHubInboxStatus.Unavailable);
        var state = authenticationRequired && successful == 0 ? "Authentication required"
            : accessDenied && successful == 0 ? "Access denied"
            : rateLimited && successful == 0 ? "Rate limited"
            : successful > 0 && (unavailable > 0 || authenticationRequired || accessDenied || rateLimited || deferred > 0 || providerGaps > 0) ? "Partial"
            : successful > 0 ? "Available"
            : candidates.Length == 0 && deferred > 0 ? "Deferred"
            : candidates.Length == 0 && withoutOrigin == portfolio.Count ? "Absent"
            : candidates.Length == 0 ? "Unavailable"
            : "Unavailable";
        var message = state switch
        {
            "Authentication required" => "GitHub issue reads require a valid provider credential.",
            "Access denied" => "GitHub denied issue access. The credential may not cover these repositories.",
            "Rate limited" => "GitHub refused or rate-limited issue reads. Retry after the provider recovers.",
            "Partial" => $"Issue evidence loaded for {successful} repository(s); {Math.Max(unavailable + deferred, providerGaps)} had unavailable or deferred GitHub signals. The bounded scan prioritizes non-archived favorites and local attention.",
            "Available" => $"Open issues were observed for {successful} repository(s).",
            "Deferred" => "GitHub evidence was intentionally deferred by the bounded repository limit. The scan prioritizes non-archived favorites and local attention.",
            "Absent" => "No supported GitHub origin was observed among the repositories checked.",
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

    private sealed record ReleaseJournalInspection(
        IReadOnlyList<WorkspaceReleaseJournalEntry> Entries,
        bool Available);
}
