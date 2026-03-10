using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Portfolio;
using PowerForgeStudio.Orchestrator.PowerShell;
using PowerForgeStudio.Orchestrator.Queue;
using PowerForgeStudio.Orchestrator.Storage;

namespace PowerForgeStudio.Orchestrator.Workspace;

public sealed class WorkspaceSnapshotService : IWorkspaceSnapshotService
{
    private readonly RepositoryCatalogScanner _catalogScanner;
    private readonly RepositoryPortfolioService _portfolioService;
    private readonly RepositoryPlanPreviewService _planPreviewService;
    private readonly GitHubInboxService _gitHubInboxService;
    private readonly RepositoryReleaseDriftService _releaseDriftService;
    private readonly RepositoryWorkspaceFamilyService _workspaceFamilyService;
    private readonly RepositoryPortfolioDashboardService _portfolioDashboardService;
    private readonly RepositoryReleaseInboxService _releaseInboxService;
    private readonly ReleaseQueuePlanner _queuePlanner;
    private readonly ReleaseStationProjectionService _stationProjectionService;

    public WorkspaceSnapshotService()
        : this(
            new RepositoryCatalogScanner(),
            new RepositoryPortfolioService(),
            new RepositoryPlanPreviewService(),
            new GitHubInboxService(),
            new RepositoryReleaseDriftService(),
            new RepositoryWorkspaceFamilyService(),
            new RepositoryPortfolioDashboardService(),
            new RepositoryReleaseInboxService(),
            new ReleaseQueuePlanner(),
            new ReleaseStationProjectionService()) {
    }

    public WorkspaceSnapshotService(
        RepositoryCatalogScanner catalogScanner,
        RepositoryPortfolioService portfolioService,
        RepositoryPlanPreviewService planPreviewService,
        GitHubInboxService gitHubInboxService,
        RepositoryReleaseDriftService releaseDriftService,
        RepositoryWorkspaceFamilyService workspaceFamilyService,
        RepositoryPortfolioDashboardService portfolioDashboardService,
        RepositoryReleaseInboxService releaseInboxService,
        ReleaseQueuePlanner queuePlanner,
        ReleaseStationProjectionService stationProjectionService)
    {
        _catalogScanner = catalogScanner;
        _portfolioService = portfolioService;
        _planPreviewService = planPreviewService;
        _gitHubInboxService = gitHubInboxService;
        _releaseDriftService = releaseDriftService;
        _workspaceFamilyService = workspaceFamilyService;
        _portfolioDashboardService = portfolioDashboardService;
        _releaseInboxService = releaseInboxService;
        _queuePlanner = queuePlanner;
        _stationProjectionService = stationProjectionService;
    }

    public async Task<WorkspaceSnapshot> RefreshAsync(
        string workspaceRoot,
        string databasePath,
        WorkspaceRefreshOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        options ??= new WorkspaceRefreshOptions();
        var buildEngineResolution = PSPublishModuleLocator.Resolve();

        var entries = _catalogScanner.Scan(workspaceRoot);
        var managedEntries = entries.Where(entry => entry.IsReleaseManaged).ToList();
        var portfolioItems = _portfolioService.BuildPortfolio(managedEntries);
        var stateDatabase = new ReleaseStateDatabase(databasePath);
        RepositoryPortfolioViewState? savedPortfolioView;

        await using (await ReleaseStateDatabase.AcquireExclusiveAccessAsync(databasePath, cancellationToken).ConfigureAwait(false))
        {
            await stateDatabase.InitializeAsync(cancellationToken).ConfigureAwait(false);
            savedPortfolioView = await stateDatabase.LoadPortfolioViewStateAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var planEnrichedPortfolio = await _planPreviewService.PopulatePlanPreviewAsync(
            portfolioItems,
            new PlanPreviewOptions {
                MaxRepositories = options.MaxPlanRepositories
            },
            cancellationToken).ConfigureAwait(false);

        var inboxEnrichedPortfolio = await _gitHubInboxService.PopulateInboxAsync(
            planEnrichedPortfolio,
            new GitHubInboxOptions {
                MaxRepositories = options.MaxGitHubRepositories
            },
            cancellationToken).ConfigureAwait(false);

        var driftEnrichedPortfolio = _releaseDriftService.PopulateReleaseDrift(inboxEnrichedPortfolio);
        var familyAnnotatedPortfolio = _workspaceFamilyService.AnnotateFamilies(driftEnrichedPortfolio);

        IReadOnlyList<RepositoryPortfolioItem> persistedPortfolio;
        ReleaseQueueSession persistedQueue;
        IReadOnlyList<RepositoryGitQuickActionReceipt> gitQuickActionReceipts;
        IReadOnlyList<ReleaseSigningReceipt> signingReceipts;
        IReadOnlyList<ReleasePublishReceipt> publishReceipts;
        IReadOnlyList<ReleaseVerificationReceipt> verificationReceipts;

        if (options.PersistState)
        {
            await using (await ReleaseStateDatabase.AcquireExclusiveAccessAsync(databasePath, cancellationToken).ConfigureAwait(false))
            {
                await stateDatabase.InitializeAsync(cancellationToken).ConfigureAwait(false);
                await stateDatabase.PersistPortfolioSnapshotAsync(familyAnnotatedPortfolio, cancellationToken).ConfigureAwait(false);
                await stateDatabase.PersistPlanSnapshotsAsync(familyAnnotatedPortfolio, cancellationToken).ConfigureAwait(false);
                persistedPortfolio = _workspaceFamilyService.AnnotateFamilies(await stateDatabase.LoadPortfolioSnapshotAsync(cancellationToken).ConfigureAwait(false));

                var draftQueue = _queuePlanner.CreateDraftQueue(workspaceRoot, persistedPortfolio);
                await stateDatabase.PersistQueueSessionAsync(draftQueue, cancellationToken).ConfigureAwait(false);
                persistedQueue = await stateDatabase.LoadLatestQueueSessionAsync(cancellationToken).ConfigureAwait(false) ?? draftQueue;

                signingReceipts = await stateDatabase.LoadSigningReceiptsAsync(persistedQueue.SessionId, cancellationToken).ConfigureAwait(false);
                publishReceipts = await stateDatabase.LoadPublishReceiptsAsync(persistedQueue.SessionId, cancellationToken).ConfigureAwait(false);
                verificationReceipts = await stateDatabase.LoadVerificationReceiptsAsync(persistedQueue.SessionId, cancellationToken).ConfigureAwait(false);
                gitQuickActionReceipts = await stateDatabase.LoadGitQuickActionReceiptsAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            persistedPortfolio = familyAnnotatedPortfolio;
            persistedQueue = _queuePlanner.CreateDraftQueue(workspaceRoot, persistedPortfolio);
            signingReceipts = [];
            publishReceipts = [];
            verificationReceipts = [];
            gitQuickActionReceipts = [];
        }

        var summary = _portfolioService.BuildSummary(persistedPortfolio);
        var gitQuickActionLookup = BuildGitQuickActionLookup(gitQuickActionReceipts);
        var dashboardCards = _portfolioDashboardService.BuildCards(persistedPortfolio, persistedQueue);
        var repositoryFamilies = _workspaceFamilyService.BuildFamilies(persistedPortfolio, persistedQueue, gitQuickActionLookup);
        var repositoryFamilyLanes = _workspaceFamilyService.BuildFamilyLanes(persistedPortfolio, persistedQueue, gitQuickActionLookup);
        var releaseInboxItems = _releaseInboxService.BuildInbox(persistedPortfolio, persistedQueue, gitQuickActionLookup);
        var signingStation = _stationProjectionService.BuildSigningStation(persistedQueue);
        var signingReceiptBatch = _stationProjectionService.BuildSigningReceipts(signingReceipts);
        var publishStation = _stationProjectionService.BuildPublishStation(persistedQueue);
        var publishReceiptBatch = _stationProjectionService.BuildPublishReceipts(publishReceipts);
        var verificationStation = _stationProjectionService.BuildVerificationStation(persistedQueue);
        var verificationReceiptBatch = _stationProjectionService.BuildVerificationReceipts(verificationReceipts);
        return new WorkspaceSnapshot(
            WorkspaceRoot: workspaceRoot,
            DatabasePath: databasePath,
            BuildEngineResolution: buildEngineResolution,
            Summary: summary,
            PortfolioItems: persistedPortfolio,
            ReleaseInboxItems: releaseInboxItems,
            DashboardCards: dashboardCards,
            RepositoryFamilies: repositoryFamilies,
            RepositoryFamilyLanes: repositoryFamilyLanes,
            QueueSession: persistedQueue,
            SigningStation: signingStation,
            SigningReceipts: signingReceipts,
            SigningReceiptBatch: signingReceiptBatch,
            PublishStation: publishStation,
            PublishReceipts: publishReceipts,
            PublishReceiptBatch: publishReceiptBatch,
            VerificationStation: verificationStation,
            VerificationReceipts: verificationReceipts,
            VerificationReceiptBatch: verificationReceiptBatch,
            GitQuickActionReceipts: gitQuickActionReceipts,
            SavedPortfolioView: savedPortfolioView);
    }

    private static IReadOnlyDictionary<string, RepositoryGitQuickActionReceipt> BuildGitQuickActionLookup(
        IReadOnlyList<RepositoryGitQuickActionReceipt> gitQuickActionReceipts)
        => gitQuickActionReceipts
            .GroupBy(receipt => receipt.RootPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(receipt => receipt.ExecutedAtUtc).First(),
                StringComparer.OrdinalIgnoreCase);
}
