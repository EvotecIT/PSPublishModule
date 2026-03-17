using System.Collections.ObjectModel;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.PowerShell;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Orchestrator.Portfolio;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed class ShellWorkspaceProjectionService : IShellWorkspaceProjectionService
{
    private readonly RepositoryPortfolioFocusService _portfolioFocusService;
    private readonly RepositoryWorkspaceFamilyService _workspaceFamilyService;
    private readonly RepositoryReleaseInboxService _releaseInboxService;

    public ShellWorkspaceProjectionService()
        : this(
            new RepositoryPortfolioFocusService(),
            new RepositoryWorkspaceFamilyService(),
            new RepositoryReleaseInboxService())
    {
    }

    public ShellWorkspaceProjectionService(
        RepositoryPortfolioFocusService portfolioFocusService,
        RepositoryWorkspaceFamilyService workspaceFamilyService,
        RepositoryReleaseInboxService releaseInboxService)
    {
        _portfolioFocusService = portfolioFocusService ?? throw new ArgumentNullException(nameof(portfolioFocusService));
        _workspaceFamilyService = workspaceFamilyService ?? throw new ArgumentNullException(nameof(workspaceFamilyService));
        _releaseInboxService = releaseInboxService ?? throw new ArgumentNullException(nameof(releaseInboxService));
    }

    public ShellWorkspaceProjectionResult ApplyProjection(ShellWorkspaceProjectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var annotatedPortfolio = _workspaceFamilyService.AnnotateFamilies(request.PortfolioItems);
        var families = _workspaceFamilyService.BuildFamilies(annotatedPortfolio, request.QueueSession, request.GitQuickActionReceiptLookup);
        var selectedRepositoryFamilyKey = ResolveSelectedRepositoryFamilyKey(request.SelectedRepositoryFamilyKey, families);
        var filteredRepositories = _portfolioFocusService.Filter(
            annotatedPortfolio,
            request.QueueSession,
            request.PortfolioOverview.SelectedFocus.Mode,
            request.PortfolioOverview.SearchText,
            selectedRepositoryFamilyKey);
        var selectedRepository = ResolveSelectedRepository(filteredRepositories, request.SelectedRepositoryRootPath);
        var selectedFamily = ResolveSelectedFamily(families, selectedRepositoryFamilyKey, selectedRepository);
        var releaseInboxItems = request.ReleaseInboxItemsOverride
            ?? _releaseInboxService.BuildInbox(annotatedPortfolio, request.QueueSession, request.GitQuickActionReceiptLookup);

        ApplyPortfolioOverview(
            request.PortfolioOverview,
            request.WorkspaceRoot,
            request.Summary,
            annotatedPortfolio,
            request.QueueSession,
            selectedRepositoryFamilyKey,
            filteredRepositories,
            request.ResolvePortfolioPreset);
        ApplyRepositoryFamily(
            request.RepositoryFamily,
            families,
            annotatedPortfolio,
            request.QueueSession,
            request.GitQuickActionReceiptLookup,
            selectedFamily);
        ApplyReleaseSignals(request.ReleaseSignals, releaseInboxItems, annotatedPortfolio, request.Summary);
        ApplyStations(request.Stations, request.QueueSession, request.StationSnapshots);
        ApplyRepositories(request.Repositories, filteredRepositories);
        ApplyBuildEngineCard(request.PortfolioOverview, request.BuildEngineResolution);

        return new ShellWorkspaceProjectionResult(
            annotatedPortfolio,
            selectedRepository,
            selectedRepositoryFamilyKey);
    }

    private void ApplyPortfolioOverview(
        PortfolioOverviewViewModel portfolioOverview,
        string workspaceRoot,
        RepositoryPortfolioSummary summary,
        IReadOnlyList<RepositoryPortfolioItem> annotatedPortfolio,
        ReleaseQueueSession? queueSession,
        string? selectedRepositoryFamilyKey,
        IReadOnlyList<RepositoryPortfolioItem> filteredRepositories,
        Func<string?, RepositoryPortfolioFocusMode, string, PortfolioQuickPreset?> resolvePortfolioPreset)
    {
        portfolioOverview.SummaryHeadline = $"{summary.ReadyRepositories} ready, {summary.AttentionRepositories} attention, {summary.BlockedRepositories} blocked";
        portfolioOverview.SummaryDetails = $"Inspected {summary.TotalRepositories} managed repositories under {workspaceRoot}; {summary.DirtyRepositories} are dirty, {summary.BehindRepositories} are behind upstream, {summary.WorktreeRepositories} are worktrees, the GitHub inbox found {summary.OpenPullRequests} open PR(s) across {summary.GitHubAttentionRepositories} attention repo(s), and {summary.ReleaseDriftAttentionRepositories} repo(s) show release drift signals.";
        var previewCount = annotatedPortfolio.Count(item => item.PlanResults is { Count: > 0 });
        portfolioOverview.PlanCoverageText = $"Plan preview executed for {previewCount} managed repositories. Mixed repos run both module and project adapters.";

        var focus = portfolioOverview.SelectedFocus;
        portfolioOverview.FocusHeadline = $"{focus.DisplayName}: showing {filteredRepositories.Count} of {annotatedPortfolio.Count} repositories";
        var familySelection = annotatedPortfolio.FirstOrDefault(item => string.Equals(item.FamilyKey, selectedRepositoryFamilyKey, StringComparison.OrdinalIgnoreCase))?.FamilyDisplayName;
        var familyNote = string.IsNullOrWhiteSpace(familySelection)
            ? string.Empty
            : $" Family filter is set to '{familySelection}'.";
        portfolioOverview.FocusDetails = string.IsNullOrWhiteSpace(portfolioOverview.SearchText)
            ? $"{focus.Description}{familyNote}"
            : $"{focus.Description} Search is narrowing the view to '{portfolioOverview.SearchText.Trim()}'.{familyNote}";
        portfolioOverview.SelectedPreset = string.IsNullOrWhiteSpace(selectedRepositoryFamilyKey)
            ? resolvePortfolioPreset(portfolioOverview.SelectedPreset?.Key, focus.Mode, portfolioOverview.SearchText)
            : null;
        portfolioOverview.PresetHeadline = portfolioOverview.SelectedPreset is null
            ? (string.IsNullOrWhiteSpace(selectedRepositoryFamilyKey) ? "Custom triage view" : "Custom family triage view")
            : $"Preset active: {portfolioOverview.SelectedPreset.DisplayName}";

        var dashboardCards = BuildDashboardCards(annotatedPortfolio, queueSession);
        portfolioOverview.DashboardCards.Clear();
        foreach (var card in dashboardCards)
        {
            portfolioOverview.DashboardCards.Add(card);
        }
    }

    private void ApplyRepositoryFamily(
        RepositoryFamilyViewModel repositoryFamily,
        IReadOnlyList<RepositoryWorkspaceFamilySnapshot> families,
        IReadOnlyList<RepositoryPortfolioItem> annotatedPortfolio,
        ReleaseQueueSession? queueSession,
        IReadOnlyDictionary<string, RepositoryGitQuickActionReceipt> gitQuickActionReceiptLookup,
        RepositoryWorkspaceFamilySnapshot? selectedFamily)
    {
        repositoryFamily.Families.Clear();
        repositoryFamily.Families.Add(new RepositoryWorkspaceFamilySnapshot(
            FamilyKey: string.Empty,
            DisplayName: "All Families",
            PrimaryRootPath: null,
            TotalMembers: annotatedPortfolio.Count,
            WorktreeMembers: annotatedPortfolio.Count(item => item.WorkspaceKind == ReleaseWorkspaceKind.Worktree),
            AttentionMembers: _portfolioFocusService.Filter(annotatedPortfolio, queueSession, RepositoryPortfolioFocusMode.Attention, searchText: null).Count,
            ReadyMembers: _portfolioFocusService.Filter(annotatedPortfolio, queueSession, RepositoryPortfolioFocusMode.Ready, searchText: null).Count,
            QueueActiveMembers: _portfolioFocusService.Filter(annotatedPortfolio, queueSession, RepositoryPortfolioFocusMode.QueueActive, searchText: null).Count,
            MemberSummary: "Every managed repository in the current workspace snapshot."));

        foreach (var family in families)
        {
            repositoryFamily.Families.Add(family);
        }

        repositoryFamily.LaneItems.Clear();
        if (selectedFamily is null)
        {
            repositoryFamily.Headline = $"{families.Count} family group(s), {annotatedPortfolio.Count(item => item.WorkspaceKind == ReleaseWorkspaceKind.Worktree)} worktree repo(s)";
            repositoryFamily.Details = "Pick a family to keep the main portfolio focused on one repo plus its worktrees and review clones.";
            repositoryFamily.ActionHeadline = "Family queue actions are available once a specific repo family is selected.";
            repositoryFamily.LaneHeadline = "Select a family or repository to open the family lane board.";
            repositoryFamily.LaneDetails = "The family lane will show which members are ready, waiting on USB, publish-ready, verify-ready, failed, or completed.";
            return;
        }

        repositoryFamily.Headline = $"{selectedFamily.DisplayName}: {selectedFamily.TotalMembers} member(s), {selectedFamily.WorktreeMembers} worktree(s)";
        repositoryFamily.Details = $"{selectedFamily.MemberSummary}. Attention: {selectedFamily.AttentionMembers}, ready: {selectedFamily.ReadyMembers}, queue-active: {selectedFamily.QueueActiveMembers}.";
        repositoryFamily.ActionHeadline = $"Family actions target {selectedFamily.DisplayName}. Prepare a scoped queue or retry failed items only inside this family.";

        var lane = _workspaceFamilyService.BuildFamilyLane(
            annotatedPortfolio,
            queueSession,
            selectedFamily.FamilyKey,
            gitQuickActionReceiptLookup);
        if (lane is null)
        {
            repositoryFamily.LaneHeadline = "Select a family or repository to open the family lane board.";
            repositoryFamily.LaneDetails = "The family lane will show which members are ready, waiting on USB, publish-ready, verify-ready, failed, or completed.";
            return;
        }

        foreach (var item in lane.Members)
        {
            repositoryFamily.LaneItems.Add(item);
        }

        repositoryFamily.LaneHeadline = lane.Headline;
        repositoryFamily.LaneDetails = lane.Details;
    }

    private static void ApplyReleaseSignals(
        ReleaseSignalsViewModel releaseSignals,
        IReadOnlyList<RepositoryReleaseInboxItem> releaseInboxItems,
        IReadOnlyList<RepositoryPortfolioItem> annotatedPortfolio,
        RepositoryPortfolioSummary summary)
    {
        releaseSignals.ReleaseInboxItems.Clear();
        foreach (var item in releaseInboxItems)
        {
            releaseSignals.ReleaseInboxItems.Add(item);
        }

        if (releaseInboxItems.Count == 0)
        {
            releaseSignals.ReleaseInboxHeadline = "No actionable release inbox items.";
            releaseSignals.ReleaseInboxDetails = "Queue, GitHub, and drift signals are calm enough that nothing needs immediate escalation here.";
        }
        else
        {
            var failedCount = releaseInboxItems.Count(item => item.Badge == "Failed");
            var gitActionFailedCount = releaseInboxItems.Count(item => item.Badge == "Git Action Failed");
            var usbCount = releaseInboxItems.Count(item => item.Badge == "USB Waiting");
            releaseSignals.ReleaseInboxHeadline = $"{releaseInboxItems.Count} action item(s), {failedCount} queue failed, {gitActionFailedCount} git-action failed, {usbCount} USB waiting";
            releaseSignals.ReleaseInboxDetails = "This inbox ranks queue failures, failed git quick actions, git guard issues, USB pauses, and release-ready work first, so you can start at the top instead of scanning the whole shell.";
        }

        releaseSignals.GitHubInboxItems.Clear();
        foreach (var item in annotatedPortfolio
                     .Where(portfolioItem => portfolioItem.GitHubInbox is not null)
                     .OrderByDescending(portfolioItem => portfolioItem.GitHubInbox?.Status == RepositoryGitHubInboxStatus.Attention)
                     .ThenByDescending(portfolioItem => portfolioItem.GitHubInbox?.OpenPullRequestCount ?? 0)
                     .ThenBy(portfolioItem => portfolioItem.Name, StringComparer.OrdinalIgnoreCase))
        {
            releaseSignals.GitHubInboxItems.Add(item);
        }

        releaseSignals.GitHubInboxHeadline = $"{summary.GitHubAttentionRepositories} repo(s) need GitHub attention, {summary.OpenPullRequests} open PR(s)";
        releaseSignals.GitHubInboxDetails = "The inbox probes origin GitHub remotes for open pull requests, the latest workflow run, and the latest release tag. Repositories outside the probe limit stay explicitly marked as deferred instead of pretending everything is known.";

        var latestTagCount = annotatedPortfolio.Count(item => !string.IsNullOrWhiteSpace(item.GitHubInbox?.LatestReleaseTag));
        releaseSignals.ReleaseDriftHeadline = $"{summary.ReleaseDriftAttentionRepositories} repo(s) show release drift, {latestTagCount} repo(s) have a detected release tag";
        releaseSignals.ReleaseDriftDetails = "Release drift is a first-pass signal: it highlights repos that appear ahead of their latest detected GitHub release boundary, have open PR pressure, or have local changes that move them beyond the last release marker.";
    }

    private static void ApplyStations(
        ReleaseStationsViewModel stations,
        ReleaseQueueSession? queueSession,
        ShellWorkspaceStationSnapshots stationSnapshots)
    {
        if (queueSession is not null)
        {
            stations.ApplyQueueSession(queueSession);
        }

        stations.ApplySigningStationSnapshot(stationSnapshots.SigningStation);
        stations.ApplySigningReceiptBatch(stationSnapshots.SigningReceiptBatch);
        stations.ApplyPublishStationSnapshot(stationSnapshots.PublishStation);
        stations.ApplyPublishReceiptBatch(stationSnapshots.PublishReceiptBatch);
        stations.ApplyVerificationStationSnapshot(stationSnapshots.VerificationStation);
        stations.ApplyVerificationReceiptBatch(stationSnapshots.VerificationReceiptBatch);
    }

    private static void ApplyRepositories(
        ObservableCollection<RepositoryPortfolioItem> repositories,
        IReadOnlyList<RepositoryPortfolioItem> filteredRepositories)
    {
        repositories.Clear();
        foreach (var repository in filteredRepositories)
        {
            repositories.Add(repository);
        }
    }

    private static void ApplyBuildEngineCard(
        PortfolioOverviewViewModel portfolioOverview,
        PSPublishModuleResolution resolution)
    {
        portfolioOverview.BuildEngineStatus = resolution.StatusDisplay;
        portfolioOverview.BuildEngineHeadline = $"{resolution.SourceDisplay} ({resolution.VersionDisplay})";
        portfolioOverview.BuildEngineDetails = resolution.IsUsable
            ? resolution.ManifestPath
            : $"{resolution.ManifestPath} is the current fallback target, but the expected module files are not present yet.";
        portfolioOverview.BuildEngineAdvisory = resolution.Warning ?? resolution.Source switch
        {
            PSPublishModuleResolutionSource.EnvironmentOverride => "Environment override is active, so the shell will prefer that engine until the variable is removed or changed.",
            PSPublishModuleResolutionSource.RepositoryManifest => "The local PSPublishModule repo manifest is active, which is the safest path when iterating on unpublished engine changes.",
            PSPublishModuleResolutionSource.InstalledModule => "No immediate compatibility warning was detected, but this is still coming from the installed module cache.",
            _ => "No immediate engine compatibility warning was detected."
        };
    }

    private IReadOnlyList<PortfolioDashboardCard> BuildDashboardCards(
        IReadOnlyList<RepositoryPortfolioItem> annotatedPortfolio,
        ReleaseQueueSession? queueSession)
    {
        var readyToday = _portfolioFocusService.Filter(annotatedPortfolio, queueSession, RepositoryPortfolioFocusMode.Ready, string.Empty).Count;
        var usbWaiting = _portfolioFocusService.Filter(annotatedPortfolio, queueSession, RepositoryPortfolioFocusMode.WaitingUsb, string.Empty).Count;
        var publishReady = _portfolioFocusService.Filter(annotatedPortfolio, queueSession, RepositoryPortfolioFocusMode.PublishReady, string.Empty).Count;
        var verifyReady = _portfolioFocusService.Filter(annotatedPortfolio, queueSession, RepositoryPortfolioFocusMode.VerifyReady, string.Empty).Count;
        var failed = _portfolioFocusService.Filter(annotatedPortfolio, queueSession, RepositoryPortfolioFocusMode.Failed, string.Empty).Count;

        return [
            new PortfolioDashboardCard("ready-today", "Ready Today", readyToday.ToString(), "Repos ready to move into a real build.", RepositoryPortfolioFocusMode.Ready, PresetKey: "ready-today"),
            new PortfolioDashboardCard("usb-waiting", "USB Waiting", usbWaiting.ToString(), "Repos paused at the signing gate.", RepositoryPortfolioFocusMode.WaitingUsb, PresetKey: "usb-waiting"),
            new PortfolioDashboardCard("publish-ready", "Publish Ready", publishReady.ToString(), "Repos with signed outputs ready for publish.", RepositoryPortfolioFocusMode.PublishReady),
            new PortfolioDashboardCard("verify-ready", "Verify Ready", verifyReady.ToString(), "Repos whose publish results are ready to verify.", RepositoryPortfolioFocusMode.VerifyReady),
            new PortfolioDashboardCard("failed", "Failed", failed.ToString(), "Repos with failed queue transitions that likely need intervention.", RepositoryPortfolioFocusMode.Failed)
        ];
    }

    private static string? ResolveSelectedRepositoryFamilyKey(
        string? selectedRepositoryFamilyKey,
        IReadOnlyList<RepositoryWorkspaceFamilySnapshot> families)
    {
        if (string.IsNullOrWhiteSpace(selectedRepositoryFamilyKey))
        {
            return null;
        }

        return families.Any(family => string.Equals(family.FamilyKey, selectedRepositoryFamilyKey, StringComparison.OrdinalIgnoreCase))
            ? selectedRepositoryFamilyKey
            : null;
    }

    private static RepositoryPortfolioItem? ResolveSelectedRepository(
        IReadOnlyList<RepositoryPortfolioItem> filteredRepositories,
        string? selectedRepositoryRootPath)
        => filteredRepositories.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(selectedRepositoryRootPath)
            && string.Equals(item.RootPath, selectedRepositoryRootPath, StringComparison.OrdinalIgnoreCase))
           ?? filteredRepositories.FirstOrDefault();

    private static RepositoryWorkspaceFamilySnapshot? ResolveSelectedFamily(
        IReadOnlyList<RepositoryWorkspaceFamilySnapshot> families,
        string? selectedRepositoryFamilyKey,
        RepositoryPortfolioItem? selectedRepository)
    {
        var familyKey = selectedRepositoryFamilyKey ?? selectedRepository?.FamilyKey;
        if (string.IsNullOrWhiteSpace(familyKey))
        {
            return null;
        }

        return families.FirstOrDefault(family => string.Equals(family.FamilyKey, familyKey, StringComparison.OrdinalIgnoreCase));
    }
}
