using System.IO;
using System.Collections.ObjectModel;
using System.Windows.Input;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.PowerShell;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Portfolio;
using PowerForgeStudio.Orchestrator.PowerShell;
using PowerForgeStudio.Orchestrator.Queue;
using PowerForgeStudio.Orchestrator.Storage;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed class ShellViewModel : ViewModelBase
{
    private readonly RepositoryCatalogScanner _catalogScanner = new();
    private readonly RepositoryPortfolioService _portfolioService = new();
    private readonly RepositoryPortfolioFocusService _portfolioFocusService = new();
    private readonly RepositoryWorkspaceFamilyService _workspaceFamilyService = new();
    private readonly RepositoryReleaseInboxService _releaseInboxService = new();
    private readonly RepositoryDetailService _repositoryDetailService = new();
    private readonly GitHubInboxService _gitHubInboxService = new();
    private readonly RepositoryReleaseDriftService _releaseDriftService = new();
    private readonly RepositoryPlanPreviewService _planPreviewService = new();
    private readonly ReleaseQueuePlanner _queuePlanner = new();
    private readonly ReleaseQueueRunner _queueRunner = new();
    private readonly ReleaseBuildExecutionService _buildExecutionService = new();
    private readonly ReleaseBuildCheckpointReader _buildCheckpointReader = new();
    private readonly ReleaseSigningExecutionService _signingExecutionService = new();
    private readonly ReleasePublishExecutionService _publishExecutionService = new();
    private readonly ReleaseVerificationExecutionService _verificationExecutionService = new();
    private CancellationTokenSource? _portfolioViewSaveCts;
    private bool _isInitialized;
    private bool _isBusy;
    private bool _isRestoringPortfolioViewState;
    private ReleaseQueueSession? _activeQueueSession;
    private IReadOnlyList<RepositoryPortfolioItem> _portfolioSnapshot = [];
    private PSPublishModuleResolution _buildEngineResolution = PSPublishModuleLocator.Resolve();
    private RepositoryPortfolioItem? _selectedRepository;
    private string _statusText = "Ready to scan the workspace.";
    private string _workspaceRoot = ResolveWorkspaceRoot();
    private string _databasePath = ReleaseStateDatabase.GetDefaultDatabasePath();
    private string _summaryHeadline = "Release cockpit foundation";
    private string _summaryDetails = "Scan the workspace, persist a catalog snapshot, and shape the shell before queue automation.";
    private string _portfolioFocusHeadline = "All managed repositories are visible.";
    private string _portfolioFocusDetails = "Use the focus selector to narrow the portfolio to the repos that matter right now.";
    private string _portfolioPresetHeadline = "Preset buttons are ready once the portfolio is loaded.";
    private string _portfolioViewMemory = "This triage view is saved automatically to local state once a portfolio snapshot exists.";
    private string _repositoryFamilyHeadline = "Repository families will appear after the first scan.";
    private string _repositoryFamilyDetails = "Family grouping will cluster primary repos with their worktrees and review clones so the portfolio can narrow to one operational unit.";
    private string _repositoryFamilyActionHeadline = "Select a repository family to unlock family-level queue actions.";
    private string _repositoryFamilyLaneHeadline = "Select a family or repository to open the family lane board.";
    private string _repositoryFamilyLaneDetails = "The family lane will show which members are ready, waiting on USB, publish-ready, verify-ready, failed, or completed.";
    private string _portfolioSearchText = string.Empty;
    private string? _selectedRepositoryFamilyKey;
    private string _planCoverageText = "Plan preview has not run yet.";
    private string _buildEngineStatus = "Unknown";
    private string _buildEngineHeadline = "PSPublishModule engine not resolved yet.";
    private string _buildEngineDetails = "Prepare Queue will capture the module manifest path used by build, publish, and signing adapters.";
    private string _buildEngineAdvisory = "Use this card to understand whether the shell is using the repo manifest, an override, or an installed module.";
    private string _repositoryDetailHeadline = "No repository selected";
    private string _repositoryDetailBadge = "Selection pending";
    private string _repositoryDetailReadiness = "Unknown";
    private string _repositoryDetailReason = "Select a managed repository to inspect its contract, queue state, and adapter evidence.";
    private string _repositoryDetailPath = "No repository path selected.";
    private string _repositoryDetailBranch = "Branch information unavailable";
    private string _repositoryDetailBuildContract = "No build contract selected.";
    private string _repositoryDetailQueueLane = "No queue state selected.";
    private string _repositoryDetailQueueCheckpoint = "No checkpoint selected.";
    private string _repositoryDetailQueueSummary = "Queue evidence will appear here after a repository is selected and the queue has been prepared.";
    private string _repositoryDetailQueuePayload = "No checkpoint payload captured yet.";
    private string _repositoryDetailReleaseDrift = "Unknown";
    private string _repositoryDetailReleaseDriftDetail = "Release drift will appear here once remote and local signals are available.";
    private string _repositoryDetailEngineDisplay = "Build engine not resolved yet.";
    private string _repositoryDetailEnginePath = "No engine path resolved yet.";
    private string _repositoryDetailEngineAdvisory = "Engine guidance will appear here once the shell resolves PSPublishModule.";
    private string _gitHubInboxHeadline = "GitHub inbox not probed yet.";
    private string _gitHubInboxDetails = "Refresh will probe a limited set of repositories for open PRs, latest workflow status, and latest release tags.";
    private string _releaseInboxHeadline = "Release inbox not assembled yet.";
    private string _releaseInboxDetails = "Refresh will rank actionable release work from queue state, GitHub attention, and release drift signals.";
    private string _releaseDriftHeadline = "Release drift not assessed yet.";
    private string _releaseDriftDetails = "Refresh will compare local git state with lightweight GitHub release signals.";
    private string _queueHeadline = "Draft queue not prepared yet.";
    private string _queueDetails = "Queue persistence will appear here once prepare completes.";
    private string _signingHeadline = "No signing batch waiting.";
    private string _signingDetails = "Build outputs that require the USB token will appear here once the queue reaches the signing gate.";
    private string _receiptHeadline = "No signing receipts yet.";
    private string _receiptDetails = "Signing outcomes will be stored through DbaClientX.SQLite before publish unlocks.";
    private string _publishHeadline = "No publish batch ready.";
    private string _publishDetails = "Publish targets will appear here after signing succeeds and before verify unlocks.";
    private string _publishReceiptHeadline = "No publish receipts yet.";
    private string _publishReceiptDetails = "Publish outcomes will be stored through DbaClientX.SQLite before verification closes the queue item.";
    private string _verificationHeadline = "No verification batch ready.";
    private string _verificationDetails = "Verification checks will appear here once publish succeeds and evidence is ready to confirm.";
    private string _verificationReceiptHeadline = "No verification receipts yet.";
    private string _verificationReceiptDetails = "Verification outcomes will be stored through DbaClientX.SQLite before a queue item is marked completed.";

    public ShellViewModel()
    {
        RefreshPortfolioCommand = new AsyncDelegateCommand(() => RefreshAsync(forceRefresh: true), () => !IsBusy);
        RunNextQueueStepCommand = new AsyncDelegateCommand(RunNextQueueStepAsync, () => !IsBusy && DraftQueue.Any(item => item.Status == ReleaseQueueItemStatus.ReadyToRun));
        ApproveUsbCommand = new AsyncDelegateCommand(ApproveUsbAsync, () => !IsBusy && DraftQueue.Any(item => item.Status == ReleaseQueueItemStatus.WaitingApproval && item.Stage == ReleaseQueueStage.Sign));
        RetryFailedCommand = new AsyncDelegateCommand(RetryFailedAsync, () => !IsBusy && DraftQueue.Any(item => item.Status == ReleaseQueueItemStatus.Failed));
        PrepareSelectedFamilyQueueCommand = new AsyncDelegateCommand(PrepareSelectedFamilyQueueAsync, CanPrepareSelectedFamilyQueue);
        RetrySelectedFamilyFailedCommand = new AsyncDelegateCommand(RetrySelectedFamilyFailedAsync, CanRetrySelectedFamilyFailed);
        ApplyPortfolioPresetCommand = new DelegateCommand<PortfolioQuickPreset>(ApplyPortfolioPreset, preset => preset is not null);
        ApplyDashboardCardCommand = new DelegateCommand<PortfolioDashboardCard>(ApplyDashboardCard, card => card is not null);
        ApplyRepositoryFamilyCommand = new DelegateCommand<RepositoryWorkspaceFamilySnapshot>(ApplyRepositoryFamily, family => family is not null);
        ApplyRepositoryFamilyLaneItemCommand = new DelegateCommand<RepositoryWorkspaceFamilyLaneItem>(ApplyRepositoryFamilyLaneItem, item => item is not null);
        ApplyReleaseInboxItemCommand = new DelegateCommand<RepositoryReleaseInboxItem>(ApplyReleaseInboxItem, item => item is not null);

        PortfolioFocusModes = new ObservableCollection<PortfolioFocusOption> {
            new(RepositoryPortfolioFocusMode.All, "All", "Show every managed repository in the current portfolio snapshot."),
            new(RepositoryPortfolioFocusMode.Attention, "Attention", "Focus on repos with local readiness issues, GitHub pressure, release drift, or failed queue states."),
            new(RepositoryPortfolioFocusMode.Ready, "Ready Today", "Focus on repos that are locally ready and already have successful plan previews."),
            new(RepositoryPortfolioFocusMode.QueueActive, "Queue Active", "Focus on repos already past prepare or holding an actionable queue state."),
            new(RepositoryPortfolioFocusMode.Blocked, "Blocked", "Focus on repos blocked by readiness or failed queue transitions."),
            new(RepositoryPortfolioFocusMode.WaitingUsb, "USB Waiting", "Focus on repos paused at the signing gate and waiting for the USB token.")
        };
        _selectedPortfolioFocus = PortfolioFocusModes[0];
        PortfolioQuickPresets = new ObservableCollection<PortfolioQuickPreset> {
            new("ready-today", "Ready Today", RepositoryPortfolioFocusMode.Ready, string.Empty, "Repos that are locally ready with successful plan previews."),
            new("attention", "Attention", RepositoryPortfolioFocusMode.Attention, string.Empty, "Repos that need action because of readiness, GitHub, drift, or queue pressure."),
            new("usb-waiting", "USB Waiting", RepositoryPortfolioFocusMode.WaitingUsb, string.Empty, "Repos currently paused at the signing checkpoint."),
            new("queue-active", "Queue Active", RepositoryPortfolioFocusMode.QueueActive, string.Empty, "Repos already moving through build, sign, publish, or verify."),
            new("all", "Reset", RepositoryPortfolioFocusMode.All, string.Empty, "Return to the full managed portfolio.")
        };

        PipelineStages = new ObservableCollection<string> {
            "Prepare: scan repos, classify contracts, and run plan-only checks.",
            "Build: produce artefacts without losing queue context.",
            "Sign: stop cleanly for USB-token approval and resume.",
            "Publish: push to GitHub, NuGet, and PowerShell Gallery behind an explicit safety flag.",
            "Verify: confirm receipts, versions, and release assets."
        };

        NextSlices = new ObservableCollection<string> {
            "Persist named triage layouts instead of only restoring the last-used view state.",
            "Add family-level batch execution previews so a family lane can show what would run next before you commit to a scoped queue."
        };

        ApplyBuildEngine(_buildEngineResolution);
    }

    public ObservableCollection<RepositoryPortfolioItem> Repositories { get; } = [];

    public ObservableCollection<PortfolioFocusOption> PortfolioFocusModes { get; }

    public ObservableCollection<PortfolioQuickPreset> PortfolioQuickPresets { get; }

    public ObservableCollection<PortfolioDashboardCard> PortfolioDashboardCards { get; } = [];

    public ObservableCollection<RepositoryWorkspaceFamilySnapshot> RepositoryFamilies { get; } = [];

    public ObservableCollection<RepositoryWorkspaceFamilyLaneItem> RepositoryFamilyLaneItems { get; } = [];

    public ObservableCollection<ReleaseQueueItem> DraftQueue { get; } = [];

    public ObservableCollection<ReleaseSigningArtifact> SigningArtifacts { get; } = [];

    public ObservableCollection<ReleaseSigningReceipt> SigningReceipts { get; } = [];

    public ObservableCollection<ReleasePublishTarget> PublishTargets { get; } = [];

    public ObservableCollection<ReleasePublishReceipt> PublishReceipts { get; } = [];

    public ObservableCollection<ReleaseVerificationTarget> VerificationTargets { get; } = [];

    public ObservableCollection<ReleaseVerificationReceipt> VerificationReceipts { get; } = [];

    public ObservableCollection<RepositoryAdapterEvidence> SelectedRepositoryEvidence { get; } = [];

    public ObservableCollection<RepositoryReleaseInboxItem> ReleaseInboxItems { get; } = [];

    public ObservableCollection<RepositoryPortfolioItem> GitHubInboxItems { get; } = [];

    private PortfolioFocusOption _selectedPortfolioFocus;
    private PortfolioQuickPreset? _selectedPortfolioPreset;

    public ICommand RefreshPortfolioCommand { get; }

    public ICommand RunNextQueueStepCommand { get; }

    public ICommand ApproveUsbCommand { get; }

    public ICommand RetryFailedCommand { get; }

    public ICommand PrepareSelectedFamilyQueueCommand { get; }

    public ICommand RetrySelectedFamilyFailedCommand { get; }

    public ICommand ApplyPortfolioPresetCommand { get; }

    public ICommand ApplyDashboardCardCommand { get; }

    public ICommand ApplyRepositoryFamilyCommand { get; }

    public ICommand ApplyRepositoryFamilyLaneItemCommand { get; }

    public ICommand ApplyReleaseInboxItemCommand { get; }

    public ObservableCollection<string> PipelineStages { get; }

    public ObservableCollection<string> NextSlices { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string WorkspaceRoot
    {
        get => _workspaceRoot;
        set => SetProperty(ref _workspaceRoot, value);
    }

    public string DatabasePath
    {
        get => _databasePath;
        private set => SetProperty(ref _databasePath, value);
    }

    public string SummaryHeadline
    {
        get => _summaryHeadline;
        private set => SetProperty(ref _summaryHeadline, value);
    }

    public string SummaryDetails
    {
        get => _summaryDetails;
        private set => SetProperty(ref _summaryDetails, value);
    }

    public string PlanCoverageText
    {
        get => _planCoverageText;
        private set => SetProperty(ref _planCoverageText, value);
    }

    public PortfolioFocusOption SelectedPortfolioFocus
    {
        get => _selectedPortfolioFocus;
        set
        {
            if (SetProperty(ref _selectedPortfolioFocus, value))
            {
                ApplyPortfolioFocus();
                SchedulePortfolioViewStateSave();
            }
        }
    }

    public string PortfolioFocusHeadline
    {
        get => _portfolioFocusHeadline;
        private set => SetProperty(ref _portfolioFocusHeadline, value);
    }

    public string PortfolioFocusDetails
    {
        get => _portfolioFocusDetails;
        private set => SetProperty(ref _portfolioFocusDetails, value);
    }

    public string PortfolioPresetHeadline
    {
        get => _portfolioPresetHeadline;
        private set => SetProperty(ref _portfolioPresetHeadline, value);
    }

    public string PortfolioViewMemory
    {
        get => _portfolioViewMemory;
        private set => SetProperty(ref _portfolioViewMemory, value);
    }

    public string RepositoryFamilyHeadline
    {
        get => _repositoryFamilyHeadline;
        private set => SetProperty(ref _repositoryFamilyHeadline, value);
    }

    public string RepositoryFamilyDetails
    {
        get => _repositoryFamilyDetails;
        private set => SetProperty(ref _repositoryFamilyDetails, value);
    }

    public string RepositoryFamilyActionHeadline
    {
        get => _repositoryFamilyActionHeadline;
        private set => SetProperty(ref _repositoryFamilyActionHeadline, value);
    }

    public string RepositoryFamilyLaneHeadline
    {
        get => _repositoryFamilyLaneHeadline;
        private set => SetProperty(ref _repositoryFamilyLaneHeadline, value);
    }

    public string RepositoryFamilyLaneDetails
    {
        get => _repositoryFamilyLaneDetails;
        private set => SetProperty(ref _repositoryFamilyLaneDetails, value);
    }

    public string PortfolioSearchText
    {
        get => _portfolioSearchText;
        set
        {
            if (SetProperty(ref _portfolioSearchText, value))
            {
                ApplyPortfolioFocus();
                SchedulePortfolioViewStateSave();
            }
        }
    }

    public string BuildEngineStatus
    {
        get => _buildEngineStatus;
        private set => SetProperty(ref _buildEngineStatus, value);
    }

    public string BuildEngineHeadline
    {
        get => _buildEngineHeadline;
        private set => SetProperty(ref _buildEngineHeadline, value);
    }

    public string BuildEngineDetails
    {
        get => _buildEngineDetails;
        private set => SetProperty(ref _buildEngineDetails, value);
    }

    public string BuildEngineAdvisory
    {
        get => _buildEngineAdvisory;
        private set => SetProperty(ref _buildEngineAdvisory, value);
    }

    public RepositoryPortfolioItem? SelectedRepository
    {
        get => _selectedRepository;
        set
        {
            if (SetProperty(ref _selectedRepository, value))
            {
                ApplyRepositoryDetail();
            }
        }
    }

    public string RepositoryDetailHeadline
    {
        get => _repositoryDetailHeadline;
        private set => SetProperty(ref _repositoryDetailHeadline, value);
    }

    public string RepositoryDetailBadge
    {
        get => _repositoryDetailBadge;
        private set => SetProperty(ref _repositoryDetailBadge, value);
    }

    public string RepositoryDetailReadiness
    {
        get => _repositoryDetailReadiness;
        private set => SetProperty(ref _repositoryDetailReadiness, value);
    }

    public string RepositoryDetailReason
    {
        get => _repositoryDetailReason;
        private set => SetProperty(ref _repositoryDetailReason, value);
    }

    public string RepositoryDetailPath
    {
        get => _repositoryDetailPath;
        private set => SetProperty(ref _repositoryDetailPath, value);
    }

    public string RepositoryDetailBranch
    {
        get => _repositoryDetailBranch;
        private set => SetProperty(ref _repositoryDetailBranch, value);
    }

    public string RepositoryDetailBuildContract
    {
        get => _repositoryDetailBuildContract;
        private set => SetProperty(ref _repositoryDetailBuildContract, value);
    }

    public string RepositoryDetailQueueLane
    {
        get => _repositoryDetailQueueLane;
        private set => SetProperty(ref _repositoryDetailQueueLane, value);
    }

    public string RepositoryDetailQueueCheckpoint
    {
        get => _repositoryDetailQueueCheckpoint;
        private set => SetProperty(ref _repositoryDetailQueueCheckpoint, value);
    }

    public string RepositoryDetailQueueSummary
    {
        get => _repositoryDetailQueueSummary;
        private set => SetProperty(ref _repositoryDetailQueueSummary, value);
    }

    public string RepositoryDetailQueuePayload
    {
        get => _repositoryDetailQueuePayload;
        private set => SetProperty(ref _repositoryDetailQueuePayload, value);
    }

    public string RepositoryDetailReleaseDrift
    {
        get => _repositoryDetailReleaseDrift;
        private set => SetProperty(ref _repositoryDetailReleaseDrift, value);
    }

    public string RepositoryDetailReleaseDriftDetail
    {
        get => _repositoryDetailReleaseDriftDetail;
        private set => SetProperty(ref _repositoryDetailReleaseDriftDetail, value);
    }

    public string RepositoryDetailEngineDisplay
    {
        get => _repositoryDetailEngineDisplay;
        private set => SetProperty(ref _repositoryDetailEngineDisplay, value);
    }

    public string RepositoryDetailEnginePath
    {
        get => _repositoryDetailEnginePath;
        private set => SetProperty(ref _repositoryDetailEnginePath, value);
    }

    public string RepositoryDetailEngineAdvisory
    {
        get => _repositoryDetailEngineAdvisory;
        private set => SetProperty(ref _repositoryDetailEngineAdvisory, value);
    }

    public string GitHubInboxHeadline
    {
        get => _gitHubInboxHeadline;
        private set => SetProperty(ref _gitHubInboxHeadline, value);
    }

    public string GitHubInboxDetails
    {
        get => _gitHubInboxDetails;
        private set => SetProperty(ref _gitHubInboxDetails, value);
    }

    public string ReleaseInboxHeadline
    {
        get => _releaseInboxHeadline;
        private set => SetProperty(ref _releaseInboxHeadline, value);
    }

    public string ReleaseInboxDetails
    {
        get => _releaseInboxDetails;
        private set => SetProperty(ref _releaseInboxDetails, value);
    }

    public string ReleaseDriftHeadline
    {
        get => _releaseDriftHeadline;
        private set => SetProperty(ref _releaseDriftHeadline, value);
    }

    public string ReleaseDriftDetails
    {
        get => _releaseDriftDetails;
        private set => SetProperty(ref _releaseDriftDetails, value);
    }

    public string QueueHeadline
    {
        get => _queueHeadline;
        private set => SetProperty(ref _queueHeadline, value);
    }

    public string QueueDetails
    {
        get => _queueDetails;
        private set => SetProperty(ref _queueDetails, value);
    }

    public string SigningHeadline
    {
        get => _signingHeadline;
        private set => SetProperty(ref _signingHeadline, value);
    }

    public string SigningDetails
    {
        get => _signingDetails;
        private set => SetProperty(ref _signingDetails, value);
    }

    public string ReceiptHeadline
    {
        get => _receiptHeadline;
        private set => SetProperty(ref _receiptHeadline, value);
    }

    public string ReceiptDetails
    {
        get => _receiptDetails;
        private set => SetProperty(ref _receiptDetails, value);
    }

    public string PublishHeadline
    {
        get => _publishHeadline;
        private set => SetProperty(ref _publishHeadline, value);
    }

    public string PublishDetails
    {
        get => _publishDetails;
        private set => SetProperty(ref _publishDetails, value);
    }

    public string PublishReceiptHeadline
    {
        get => _publishReceiptHeadline;
        private set => SetProperty(ref _publishReceiptHeadline, value);
    }

    public string PublishReceiptDetails
    {
        get => _publishReceiptDetails;
        private set => SetProperty(ref _publishReceiptDetails, value);
    }

    public string VerificationHeadline
    {
        get => _verificationHeadline;
        private set => SetProperty(ref _verificationHeadline, value);
    }

    public string VerificationDetails
    {
        get => _verificationDetails;
        private set => SetProperty(ref _verificationDetails, value);
    }

    public string VerificationReceiptHeadline
    {
        get => _verificationReceiptHeadline;
        private set => SetProperty(ref _verificationReceiptHeadline, value);
    }

    public string VerificationReceiptDetails
    {
        get => _verificationReceiptDetails;
        private set => SetProperty(ref _verificationReceiptDetails, value);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await RefreshAsync(forceRefresh: false, cancellationToken);
    }

    public async Task RefreshAsync(bool forceRefresh, CancellationToken cancellationToken = default)
    {
        if (_isInitialized && !forceRefresh)
        {
            return;
        }

        _isInitialized = true;
        IsBusy = true;
        try
        {
            var buildEngineResolution = PSPublishModuleLocator.Resolve();
            StatusText = $"Scanning {WorkspaceRoot}...";

            var entries = _catalogScanner.Scan(WorkspaceRoot);
            var managedEntries = entries.Where(entry => entry.IsReleaseManaged).ToList();
            var portfolioItems = _portfolioService.BuildPortfolio(managedEntries);
            var stateDatabase = new ReleaseStateDatabase(DatabasePath);

            await stateDatabase.InitializeAsync(cancellationToken);
            var savedPortfolioView = await stateDatabase.LoadPortfolioViewStateAsync(cancellationToken: cancellationToken);
            ApplySavedPortfolioView(savedPortfolioView);

            StatusText = $"Running plan preview for up to 12 repositories...";
            var planEnrichedPortfolio = await _planPreviewService.PopulatePlanPreviewAsync(portfolioItems, new PlanPreviewOptions {
                MaxRepositories = 12
            }, cancellationToken);
            var inboxEnrichedPortfolio = await _gitHubInboxService.PopulateInboxAsync(planEnrichedPortfolio, new GitHubInboxOptions {
                MaxRepositories = 15
            }, cancellationToken);
            var driftEnrichedPortfolio = _releaseDriftService.PopulateReleaseDrift(inboxEnrichedPortfolio);
            var familyAnnotatedPortfolio = _workspaceFamilyService.AnnotateFamilies(driftEnrichedPortfolio);
            await stateDatabase.PersistPortfolioSnapshotAsync(familyAnnotatedPortfolio, cancellationToken);
            await stateDatabase.PersistPlanSnapshotsAsync(familyAnnotatedPortfolio, cancellationToken);
            var persistedPortfolio = _workspaceFamilyService.AnnotateFamilies(await stateDatabase.LoadPortfolioSnapshotAsync(cancellationToken));
            var summary = _portfolioService.BuildSummary(persistedPortfolio);
            var draftQueue = _queuePlanner.CreateDraftQueue(WorkspaceRoot, persistedPortfolio);
            await stateDatabase.PersistQueueSessionAsync(draftQueue, cancellationToken);
            var persistedQueue = await stateDatabase.LoadLatestQueueSessionAsync(cancellationToken) ?? draftQueue;
            var persistedReceipts = await stateDatabase.LoadSigningReceiptsAsync(persistedQueue.SessionId, cancellationToken);
            var persistedPublishReceipts = await stateDatabase.LoadPublishReceiptsAsync(persistedQueue.SessionId, cancellationToken);
            var persistedVerificationReceipts = await stateDatabase.LoadVerificationReceiptsAsync(persistedQueue.SessionId, cancellationToken);

            _buildEngineResolution = buildEngineResolution;
            _activeQueueSession = persistedQueue;
            ApplyPortfolio(persistedPortfolio, summary);
            ApplyReleaseInbox(persistedPortfolio, persistedQueue);
            ApplyGitHubInbox(persistedPortfolio, summary);
            ApplyReleaseDrift(persistedPortfolio, summary);
            ApplyBuildEngine(buildEngineResolution);
            ApplyQueue(persistedQueue);
            ApplySigningReceipts(persistedReceipts);
            ApplyPublishStation(persistedQueue);
            ApplyPublishReceipts(persistedPublishReceipts);
            ApplyVerificationStation(persistedQueue);
            ApplyVerificationReceipts(persistedVerificationReceipts);
            StatusText = $"Portfolio ready. Persisted {persistedPortfolio.Count} portfolio rows, remote inbox signals, plan previews, and draft queue state to {DatabasePath}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunNextQueueStepAsync()
    {
        IsBusy = true;
        try
        {
            var stateDatabase = new ReleaseStateDatabase(DatabasePath);
            await stateDatabase.InitializeAsync();

            var currentSession = await stateDatabase.LoadLatestQueueSessionAsync();
            if (currentSession is null)
            {
                StatusText = "Queue state is not available yet. Prepare the queue first.";
                return;
            }

            var nextReadyItem = currentSession.Items.FirstOrDefault(item => item.Status == ReleaseQueueItemStatus.ReadyToRun);
            if (nextReadyItem is null)
            {
                StatusText = "No queue item is currently ready to run.";
                return;
            }

            ReleaseQueueTransitionResult result;
            if (nextReadyItem.Stage == ReleaseQueueStage.Build)
            {
                StatusText = $"Running real build for {nextReadyItem.RepositoryName} with publish/install disabled...";
                var buildResult = await _buildExecutionService.ExecuteAsync(nextReadyItem.RootPath);
                result = buildResult.Succeeded
                    ? _queueRunner.CompleteBuild(currentSession, nextReadyItem.RootPath, buildResult)
                    : _queueRunner.FailBuild(currentSession, nextReadyItem.RootPath, buildResult);
            }
            else if (nextReadyItem.Stage == ReleaseQueueStage.Publish)
            {
                StatusText = $"Publishing release targets for {nextReadyItem.RepositoryName}...";
                var publishResult = await _publishExecutionService.ExecuteAsync(nextReadyItem);
                await stateDatabase.PersistPublishReceiptsAsync(currentSession.SessionId, publishResult.Receipts);
                result = publishResult.Succeeded
                    ? _queueRunner.CompletePublish(currentSession, nextReadyItem.RootPath, publishResult)
                    : _queueRunner.FailPublish(currentSession, nextReadyItem.RootPath, publishResult);
            }
            else if (nextReadyItem.Stage == ReleaseQueueStage.Verify)
            {
                StatusText = $"Verifying published targets for {nextReadyItem.RepositoryName}...";
                var verificationResult = await _verificationExecutionService.ExecuteAsync(nextReadyItem);
                await stateDatabase.PersistVerificationReceiptsAsync(currentSession.SessionId, verificationResult.Receipts);
                result = verificationResult.Succeeded
                    ? _queueRunner.CompleteVerification(currentSession, nextReadyItem.RootPath, verificationResult)
                    : _queueRunner.FailVerification(currentSession, nextReadyItem.RootPath, verificationResult);
            }
            else
            {
                result = _queueRunner.AdvanceNextReadyItem(currentSession);
            }

            if (!result.Changed)
            {
                StatusText = result.Message;
                return;
            }

            await stateDatabase.PersistQueueSessionAsync(result.Session);
            var persistedQueue = await stateDatabase.LoadLatestQueueSessionAsync() ?? result.Session;
            _activeQueueSession = persistedQueue;
            ApplyQueue(persistedQueue);
            var persistedReceipts = await stateDatabase.LoadSigningReceiptsAsync(persistedQueue.SessionId);
            var persistedPublishReceipts = await stateDatabase.LoadPublishReceiptsAsync(persistedQueue.SessionId);
            var persistedVerificationReceipts = await stateDatabase.LoadVerificationReceiptsAsync(persistedQueue.SessionId);
            ApplySigningReceipts(persistedReceipts);
            ApplyPublishStation(persistedQueue);
            ApplyPublishReceipts(persistedPublishReceipts);
            ApplyVerificationStation(persistedQueue);
            ApplyVerificationReceipts(persistedVerificationReceipts);
            StatusText = result.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApproveUsbAsync()
    {
        IsBusy = true;
        try
        {
            var stateDatabase = new ReleaseStateDatabase(DatabasePath);
            await stateDatabase.InitializeAsync();

            var currentSession = await stateDatabase.LoadLatestQueueSessionAsync();
            if (currentSession is null)
            {
                StatusText = "Queue state is not available yet. Prepare the queue first.";
                return;
            }

            var waitingItem = currentSession.Items.FirstOrDefault(item => item.Stage == ReleaseQueueStage.Sign && item.Status == ReleaseQueueItemStatus.WaitingApproval);
            if (waitingItem is null)
            {
                StatusText = "No queue item is currently waiting on USB approval.";
                return;
            }

            StatusText = $"Signing artifacts for {waitingItem.RepositoryName}...";
            var signingResult = await _signingExecutionService.ExecuteAsync(waitingItem);
            await stateDatabase.PersistSigningReceiptsAsync(currentSession.SessionId, signingResult.Receipts);

            var result = signingResult.Succeeded
                ? _queueRunner.CompleteSigning(currentSession, waitingItem.RootPath, signingResult)
                : _queueRunner.FailSigning(currentSession, waitingItem.RootPath, signingResult);

            if (!result.Changed)
            {
                StatusText = result.Message;
                return;
            }

            await stateDatabase.PersistQueueSessionAsync(result.Session);
            var persistedQueue = await stateDatabase.LoadLatestQueueSessionAsync() ?? result.Session;
            _activeQueueSession = persistedQueue;
            var persistedReceipts = await stateDatabase.LoadSigningReceiptsAsync(persistedQueue.SessionId);
            var persistedPublishReceipts = await stateDatabase.LoadPublishReceiptsAsync(persistedQueue.SessionId);
            var persistedVerificationReceipts = await stateDatabase.LoadVerificationReceiptsAsync(persistedQueue.SessionId);
            ApplyQueue(persistedQueue);
            ApplySigningReceipts(persistedReceipts);
            ApplyPublishStation(persistedQueue);
            ApplyPublishReceipts(persistedPublishReceipts);
            ApplyVerificationStation(persistedQueue);
            ApplyVerificationReceipts(persistedVerificationReceipts);
            StatusText = result.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task RetryFailedAsync()
        => UpdateQueueSessionAsync(_queueRunner.RetryFailedItem);

    private async Task PrepareSelectedFamilyQueueAsync()
    {
        var family = ResolveSelectedFamily();
        if (family is null)
        {
            StatusText = "Select a repository family first, then prepare a family-scoped queue.";
            return;
        }

        var familyItems = _portfolioSnapshot
            .Where(item => string.Equals(item.FamilyKey, family.FamilyKey, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (familyItems.Length == 0)
        {
            StatusText = $"No portfolio items are currently available for the {family.DisplayName} family.";
            return;
        }

        IsBusy = true;
        try
        {
            var stateDatabase = new ReleaseStateDatabase(DatabasePath);
            await stateDatabase.InitializeAsync();

            var queueSession = _queuePlanner.CreateDraftQueue(
                WorkspaceRoot,
                familyItems,
                scopeKey: family.FamilyKey,
                scopeDisplayName: family.DisplayName);

            await stateDatabase.PersistQueueSessionAsync(queueSession);
            var persistedQueue = await stateDatabase.LoadLatestQueueSessionAsync() ?? queueSession;
            _activeQueueSession = persistedQueue;
            ApplyQueue(persistedQueue);
            ApplySigningReceipts(await stateDatabase.LoadSigningReceiptsAsync(persistedQueue.SessionId));
            ApplyPublishStation(persistedQueue);
            ApplyPublishReceipts(await stateDatabase.LoadPublishReceiptsAsync(persistedQueue.SessionId));
            ApplyVerificationStation(persistedQueue);
            ApplyVerificationReceipts(await stateDatabase.LoadVerificationReceiptsAsync(persistedQueue.SessionId));
            StatusText = $"Prepared a family-scoped queue for {family.DisplayName} with {familyItems.Length} repository row(s).";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task RetrySelectedFamilyFailedAsync()
    {
        var family = ResolveSelectedFamily();
        if (family is null)
        {
            StatusText = "Select a repository family first, then retry failed items for that family.";
            return Task.CompletedTask;
        }

        var familyRootPaths = _portfolioSnapshot
            .Where(item => string.Equals(item.FamilyKey, family.FamilyKey, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.RootPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return UpdateQueueSessionAsync(session => _queueRunner.RetryFailedItems(session, item => familyRootPaths.Contains(item.RootPath)));
    }

    private async Task UpdateQueueSessionAsync(Func<ReleaseQueueSession, ReleaseQueueTransitionResult> transition)
    {
        IsBusy = true;
        try
        {
            var stateDatabase = new ReleaseStateDatabase(DatabasePath);
            await stateDatabase.InitializeAsync();

            var currentSession = await stateDatabase.LoadLatestQueueSessionAsync();
            if (currentSession is null)
            {
                StatusText = "Queue state is not available yet. Prepare the queue first.";
                return;
            }

            var result = transition(currentSession);
            if (!result.Changed)
            {
                StatusText = result.Message;
                return;
            }

            await stateDatabase.PersistQueueSessionAsync(result.Session);
            var persistedQueue = await stateDatabase.LoadLatestQueueSessionAsync() ?? result.Session;
            _activeQueueSession = persistedQueue;
            var persistedReceipts = await stateDatabase.LoadSigningReceiptsAsync(persistedQueue.SessionId);
            var persistedPublishReceipts = await stateDatabase.LoadPublishReceiptsAsync(persistedQueue.SessionId);
            var persistedVerificationReceipts = await stateDatabase.LoadVerificationReceiptsAsync(persistedQueue.SessionId);
            ApplyQueue(persistedQueue);
            ApplySigningReceipts(persistedReceipts);
            ApplyPublishStation(persistedQueue);
            ApplyPublishReceipts(persistedPublishReceipts);
            ApplyVerificationStation(persistedQueue);
            ApplyVerificationReceipts(persistedVerificationReceipts);
            StatusText = result.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyPortfolio(IReadOnlyList<RepositoryPortfolioItem> portfolioItems, RepositoryPortfolioSummary summary)
    {
        _portfolioSnapshot = _workspaceFamilyService.AnnotateFamilies(portfolioItems);
        SummaryHeadline = $"{summary.ReadyRepositories} ready, {summary.AttentionRepositories} attention, {summary.BlockedRepositories} blocked";
        SummaryDetails = $"Inspected {summary.TotalRepositories} managed repositories under {WorkspaceRoot}; {summary.DirtyRepositories} are dirty, {summary.BehindRepositories} are behind upstream, {summary.WorktreeRepositories} are worktrees, the GitHub inbox found {summary.OpenPullRequests} open PR(s) across {summary.GitHubAttentionRepositories} attention repo(s), and {summary.ReleaseDriftAttentionRepositories} repo(s) show release drift signals.";
        PlanCoverageText = $"Plan preview executed for {Math.Min(12, _portfolioSnapshot.Count)} managed repositories. Mixed repos run both module and project adapters.";
        ApplyRepositoryFamilies();
        ApplyPortfolioFocus();
        ApplyPortfolioDashboardCards();
    }

    private void ApplyReleaseInbox(IReadOnlyList<RepositoryPortfolioItem> portfolioItems, ReleaseQueueSession? queueSession)
    {
        var inboxItems = _releaseInboxService.BuildInbox(portfolioItems, queueSession);
        ReleaseInboxItems.Clear();
        foreach (var item in inboxItems)
        {
            ReleaseInboxItems.Add(item);
        }

        if (inboxItems.Count == 0)
        {
            ReleaseInboxHeadline = "No actionable release inbox items.";
            ReleaseInboxDetails = "Queue, GitHub, and drift signals are calm enough that nothing needs immediate escalation here.";
            return;
        }

        var failedCount = inboxItems.Count(item => item.Badge == "Failed");
        var usbCount = inboxItems.Count(item => item.Badge == "USB Waiting");
        ReleaseInboxHeadline = $"{inboxItems.Count} action item(s), {failedCount} failed, {usbCount} USB waiting";
        ReleaseInboxDetails = "This inbox ranks real blockers first, then queues up release work that is ready to move, so you can start at the top instead of scanning the whole shell.";
    }

    private void ApplyRepositoryFamilies()
    {
        var families = _workspaceFamilyService.BuildFamilies(_portfolioSnapshot, _activeQueueSession);
        RepositoryFamilies.Clear();

        RepositoryFamilies.Add(new RepositoryWorkspaceFamilySnapshot(
            FamilyKey: string.Empty,
            DisplayName: "All Families",
            PrimaryRootPath: null,
            TotalMembers: _portfolioSnapshot.Count,
            WorktreeMembers: _portfolioSnapshot.Count(item => item.WorkspaceKind == ReleaseWorkspaceKind.Worktree),
            AttentionMembers: _portfolioFocusService.Filter(_portfolioSnapshot, _activeQueueSession, RepositoryPortfolioFocusMode.Attention, searchText: null).Count,
            ReadyMembers: _portfolioFocusService.Filter(_portfolioSnapshot, _activeQueueSession, RepositoryPortfolioFocusMode.Ready, searchText: null).Count,
            QueueActiveMembers: _portfolioFocusService.Filter(_portfolioSnapshot, _activeQueueSession, RepositoryPortfolioFocusMode.QueueActive, searchText: null).Count,
            MemberSummary: "Every managed repository in the current workspace snapshot."));

        foreach (var family in families.Take(10))
        {
            RepositoryFamilies.Add(family);
        }

        if (!string.IsNullOrWhiteSpace(_selectedRepositoryFamilyKey)
            && families.All(family => !string.Equals(family.FamilyKey, _selectedRepositoryFamilyKey, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedRepositoryFamilyKey = null;
        }

        var selectedFamily = ResolveSelectedFamily();
        if (selectedFamily is null)
        {
            RepositoryFamilyHeadline = $"{families.Count} family group(s), {_portfolioSnapshot.Count(item => item.WorkspaceKind == ReleaseWorkspaceKind.Worktree)} worktree repo(s)";
            RepositoryFamilyDetails = "Pick a family to keep the main portfolio focused on one repo plus its worktrees and review clones.";
            RepositoryFamilyActionHeadline = "Family queue actions are available once a specific repo family is selected.";
            ApplyRepositoryFamilyLane(null);
            RaiseCommandStates();
            return;
        }

        RepositoryFamilyHeadline = $"{selectedFamily.DisplayName}: {selectedFamily.TotalMembers} member(s), {selectedFamily.WorktreeMembers} worktree(s)";
        RepositoryFamilyDetails = $"{selectedFamily.MemberSummary}. Attention: {selectedFamily.AttentionMembers}, ready: {selectedFamily.ReadyMembers}, queue-active: {selectedFamily.QueueActiveMembers}.";
        RepositoryFamilyActionHeadline = $"Family actions target {selectedFamily.DisplayName}. Prepare a scoped queue or retry failed items only inside this family.";
        ApplyRepositoryFamilyLane(_workspaceFamilyService.BuildFamilyLane(_portfolioSnapshot, _activeQueueSession, selectedFamily.FamilyKey));
        RaiseCommandStates();
    }

    private void ApplyRepositoryFamilyLane(RepositoryWorkspaceFamilyLaneSnapshot? lane)
    {
        RepositoryFamilyLaneItems.Clear();

        if (lane is null)
        {
            RepositoryFamilyLaneHeadline = "Select a family or repository to open the family lane board.";
            RepositoryFamilyLaneDetails = "The family lane will show which members are ready, waiting on USB, publish-ready, verify-ready, failed, or completed.";
            return;
        }

        foreach (var item in lane.Members)
        {
            RepositoryFamilyLaneItems.Add(item);
        }

        RepositoryFamilyLaneHeadline = lane.Headline;
        RepositoryFamilyLaneDetails = lane.Details;
    }

    private void ApplyPortfolioFocus()
    {
        var selectedRootPath = SelectedRepository?.RootPath;
        var focus = SelectedPortfolioFocus;
        var filteredItems = _portfolioFocusService.Filter(_portfolioSnapshot, _activeQueueSession, focus.Mode, PortfolioSearchText, _selectedRepositoryFamilyKey);

        Repositories.Clear();
        foreach (var item in filteredItems.Take(60))
        {
            Repositories.Add(item);
        }

        PortfolioFocusHeadline = $"{focus.DisplayName}: showing {filteredItems.Count} of {_portfolioSnapshot.Count} repositories";
        var familySelection = _portfolioSnapshot.FirstOrDefault(item => string.Equals(item.FamilyKey, _selectedRepositoryFamilyKey, StringComparison.OrdinalIgnoreCase))?.FamilyDisplayName;
        var familyNote = string.IsNullOrWhiteSpace(familySelection)
            ? string.Empty
            : $" Family filter is set to '{familySelection}'.";
        PortfolioFocusDetails = string.IsNullOrWhiteSpace(PortfolioSearchText)
            ? $"{focus.Description}{familyNote}"
            : $"{focus.Description} Search is narrowing the view to '{PortfolioSearchText.Trim()}'.{familyNote}";
        _selectedPortfolioPreset = string.IsNullOrWhiteSpace(_selectedRepositoryFamilyKey)
            ? ResolvePortfolioPreset(_selectedPortfolioPreset?.Key, focus.Mode, PortfolioSearchText)
            : null;
        PortfolioPresetHeadline = _selectedPortfolioPreset is null
            ? (string.IsNullOrWhiteSpace(_selectedRepositoryFamilyKey) ? "Custom triage view" : "Custom family triage view")
            : $"Preset active: {_selectedPortfolioPreset.DisplayName}";

        SelectedRepository = Repositories.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(selectedRootPath)
            && string.Equals(item.RootPath, selectedRootPath, StringComparison.OrdinalIgnoreCase))
            ?? Repositories.FirstOrDefault();
    }

    private void ApplyPortfolioDashboardCards()
    {
        var dashboardCards = BuildPortfolioDashboardCards();
        PortfolioDashboardCards.Clear();
        foreach (var card in dashboardCards)
        {
            PortfolioDashboardCards.Add(card);
        }
    }

    private IReadOnlyList<PortfolioDashboardCard> BuildPortfolioDashboardCards()
    {
        var readyToday = _portfolioFocusService.Filter(_portfolioSnapshot, _activeQueueSession, RepositoryPortfolioFocusMode.Ready, string.Empty).Count;
        var usbWaiting = _portfolioFocusService.Filter(_portfolioSnapshot, _activeQueueSession, RepositoryPortfolioFocusMode.WaitingUsb, string.Empty).Count;
        var publishReady = _portfolioFocusService.Filter(_portfolioSnapshot, _activeQueueSession, RepositoryPortfolioFocusMode.PublishReady, string.Empty).Count;
        var verifyReady = _portfolioFocusService.Filter(_portfolioSnapshot, _activeQueueSession, RepositoryPortfolioFocusMode.VerifyReady, string.Empty).Count;
        var failed = _portfolioFocusService.Filter(_portfolioSnapshot, _activeQueueSession, RepositoryPortfolioFocusMode.Failed, string.Empty).Count;

        return [
            new PortfolioDashboardCard("ready-today", "Ready Today", readyToday.ToString(), "Repos ready to move into a real build.", RepositoryPortfolioFocusMode.Ready, PresetKey: "ready-today"),
            new PortfolioDashboardCard("usb-waiting", "USB Waiting", usbWaiting.ToString(), "Repos paused at the signing gate.", RepositoryPortfolioFocusMode.WaitingUsb, PresetKey: "usb-waiting"),
            new PortfolioDashboardCard("publish-ready", "Publish Ready", publishReady.ToString(), "Repos with signed outputs ready for publish.", RepositoryPortfolioFocusMode.PublishReady),
            new PortfolioDashboardCard("verify-ready", "Verify Ready", verifyReady.ToString(), "Repos whose publish results are ready to verify.", RepositoryPortfolioFocusMode.VerifyReady),
            new PortfolioDashboardCard("failed", "Failed", failed.ToString(), "Repos with failed queue transitions that likely need intervention.", RepositoryPortfolioFocusMode.Failed)
        ];
    }

    private void ApplySavedPortfolioView(RepositoryPortfolioViewState? viewState)
    {
        _isRestoringPortfolioViewState = true;
        try
        {
            if (viewState is null)
            {
                PortfolioViewMemory = "This triage view is saved automatically to local state once a portfolio snapshot exists.";
                return;
            }

            var selectedFocus = PortfolioFocusModes.FirstOrDefault(option => option.Mode == viewState.FocusMode)
                ?? PortfolioFocusModes[0];
            SelectedPortfolioFocus = selectedFocus;
            PortfolioSearchText = viewState.SearchText;
            _selectedRepositoryFamilyKey = viewState.FamilyKey;
            _selectedPortfolioPreset = ResolvePortfolioPreset(viewState.PresetKey, viewState.FocusMode, viewState.SearchText);
            var familySelection = string.IsNullOrWhiteSpace(viewState.FamilyKey)
                ? string.Empty
                : $" and family '{viewState.FamilyKey}'";
            PortfolioViewMemory = string.IsNullOrWhiteSpace(viewState.SearchText)
                ? $"Restored saved triage view: {selectedFocus.DisplayName}{familySelection}."
                : $"Restored saved triage view: {selectedFocus.DisplayName}{familySelection} with search '{viewState.SearchText}'.";
        }
        finally
        {
            _isRestoringPortfolioViewState = false;
        }
    }

    private void SchedulePortfolioViewStateSave()
    {
        if (!_isInitialized || _isRestoringPortfolioViewState)
        {
            return;
        }

        _portfolioViewSaveCts?.Cancel();
        _portfolioViewSaveCts?.Dispose();
        _portfolioViewSaveCts = new CancellationTokenSource();
        var token = _portfolioViewSaveCts.Token;
        var state = new RepositoryPortfolioViewState(
            PresetKey: _selectedPortfolioPreset?.Key,
            FocusMode: SelectedPortfolioFocus.Mode,
            SearchText: PortfolioSearchText,
            FamilyKey: _selectedRepositoryFamilyKey,
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        _ = PersistPortfolioViewStateAsync(state, token);
    }

    private async Task PersistPortfolioViewStateAsync(RepositoryPortfolioViewState state, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(350, cancellationToken).ConfigureAwait(false);
            var stateDatabase = new ReleaseStateDatabase(DatabasePath);
            await stateDatabase.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await stateDatabase.PersistPortfolioViewStateAsync(state, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Replaced by a newer triage-state change.
        }
    }

    private void ApplyPortfolioPreset(PortfolioQuickPreset? preset)
    {
        if (preset is null)
        {
            return;
        }

        _selectedPortfolioPreset = preset;
        _selectedRepositoryFamilyKey = null;
        var selectedFocus = PortfolioFocusModes.FirstOrDefault(option => option.Mode == preset.FocusMode)
            ?? PortfolioFocusModes[0];
        SelectedPortfolioFocus = selectedFocus;
        PortfolioSearchText = preset.SearchText;
        PortfolioViewMemory = $"Preset applied: {preset.DisplayName}.";
        ApplyPortfolioFocus();
        ApplyRepositoryFamilies();
    }

    private void ApplyDashboardCard(PortfolioDashboardCard? card)
    {
        if (card is null)
        {
            return;
        }

        _selectedPortfolioPreset = ResolvePortfolioPreset(card.PresetKey, card.FocusMode, card.SearchText);
        _selectedRepositoryFamilyKey = null;
        var selectedFocus = PortfolioFocusModes.FirstOrDefault(option => option.Mode == card.FocusMode)
            ?? PortfolioFocusModes[0];
        SelectedPortfolioFocus = selectedFocus;
        PortfolioSearchText = card.SearchText;
        PortfolioViewMemory = $"Dashboard card applied: {card.Title}.";
        ApplyPortfolioFocus();
        ApplyRepositoryFamilies();
    }

    private void ApplyRepositoryFamily(RepositoryWorkspaceFamilySnapshot? family)
    {
        if (family is null)
        {
            return;
        }

        _selectedPortfolioPreset = null;
        _selectedRepositoryFamilyKey = string.IsNullOrWhiteSpace(family.FamilyKey)
            ? null
            : family.FamilyKey;
        ApplyPortfolioFocus();
        ApplyRepositoryFamilies();
        PortfolioViewMemory = string.IsNullOrWhiteSpace(_selectedRepositoryFamilyKey)
            ? "Repository family filter cleared."
            : $"Repository family applied: {family.DisplayName}.";
        SchedulePortfolioViewStateSave();
    }

    private void ApplyRepositoryFamilyLaneItem(RepositoryWorkspaceFamilyLaneItem? item)
    {
        if (item is null)
        {
            return;
        }

        var selectedRepository = _portfolioSnapshot.FirstOrDefault(repository =>
            string.Equals(repository.RootPath, item.RootPath, StringComparison.OrdinalIgnoreCase));
        if (selectedRepository is null)
        {
            return;
        }

        _selectedPortfolioPreset = null;
        _selectedRepositoryFamilyKey = selectedRepository.FamilyKey;
        ApplyPortfolioFocus();
        ApplyRepositoryFamilies();
        SelectedRepository = Repositories.FirstOrDefault(repository =>
            string.Equals(repository.RootPath, item.RootPath, StringComparison.OrdinalIgnoreCase))
            ?? selectedRepository;
        PortfolioViewMemory = $"Family lane item applied: {item.LaneDisplay} for {item.RepositoryName}.";
        SchedulePortfolioViewStateSave();
    }

    private void ApplyReleaseInboxItem(RepositoryReleaseInboxItem? item)
    {
        if (item is null)
        {
            return;
        }

        _selectedPortfolioPreset = ResolvePortfolioPreset(item.PresetKey, item.FocusMode, item.SearchText);
        var selectedFocus = PortfolioFocusModes.FirstOrDefault(option => option.Mode == item.FocusMode)
            ?? PortfolioFocusModes[0];
        SelectedPortfolioFocus = selectedFocus;
        PortfolioSearchText = item.SearchText;
        ApplyPortfolioFocus();
        var selectedRepository = Repositories.FirstOrDefault(repository =>
            string.Equals(repository.RootPath, item.RootPath, StringComparison.OrdinalIgnoreCase))
            ?? _portfolioSnapshot.FirstOrDefault(repository =>
                string.Equals(repository.RootPath, item.RootPath, StringComparison.OrdinalIgnoreCase));
        _selectedRepositoryFamilyKey = selectedRepository?.FamilyKey;
        ApplyPortfolioFocus();
        ApplyRepositoryFamilies();
        SelectedRepository = Repositories.FirstOrDefault(repository =>
            string.Equals(repository.RootPath, item.RootPath, StringComparison.OrdinalIgnoreCase))
            ?? selectedRepository;
        PortfolioViewMemory = $"Release inbox item applied: {item.Badge} for {item.RepositoryName}.";
    }

    private PortfolioQuickPreset? ResolvePortfolioPreset(string? presetKey, RepositoryPortfolioFocusMode focusMode, string searchText)
    {
        if (!string.IsNullOrWhiteSpace(presetKey))
        {
            var byKey = PortfolioQuickPresets.FirstOrDefault(preset => string.Equals(preset.Key, presetKey, StringComparison.OrdinalIgnoreCase));
            if (byKey is not null)
            {
                return byKey;
            }
        }

        return PortfolioQuickPresets.FirstOrDefault(preset =>
            preset.FocusMode == focusMode
            && string.Equals(preset.SearchText, searchText ?? string.Empty, StringComparison.Ordinal));
    }

    private void ApplyBuildEngine(PSPublishModuleResolution resolution)
    {
        _buildEngineResolution = resolution;
        BuildEngineStatus = resolution.StatusDisplay;
        BuildEngineHeadline = $"{resolution.SourceDisplay} ({resolution.VersionDisplay})";
        BuildEngineDetails = resolution.IsUsable
            ? resolution.ManifestPath
            : $"{resolution.ManifestPath} is the current fallback target, but the expected module files are not present yet.";

        BuildEngineAdvisory = resolution.Warning ?? resolution.Source switch
        {
            PSPublishModuleResolutionSource.EnvironmentOverride => "Environment override is active, so the shell will prefer that engine until the variable is removed or changed.",
            PSPublishModuleResolutionSource.RepositoryManifest => "The local PSPublishModule repo manifest is active, which is the safest path when iterating on unpublished engine changes.",
            PSPublishModuleResolutionSource.InstalledModule => "No immediate compatibility warning was detected, but this is still coming from the installed module cache.",
            _ => "No immediate engine compatibility warning was detected."
        };
        ApplyRepositoryDetail();
    }

    private void ApplyQueue(ReleaseQueueSession queueSession)
    {
        _activeQueueSession = queueSession;
        DraftQueue.Clear();
        foreach (var item in queueSession.Items.Take(10))
        {
            DraftQueue.Add(item);
        }

        QueueHeadline = $"{queueSession.Summary.BuildReadyItems} build-ready, {queueSession.Summary.PreparePendingItems} waiting in prepare, {queueSession.Summary.BlockedItems} blocked";
        var scopeLabel = string.IsNullOrWhiteSpace(queueSession.ScopeDisplayName)
            ? "workspace-wide"
            : $"family-scoped for {queueSession.ScopeDisplayName}";
        QueueDetails = $"Draft queue {queueSession.SessionId[..8]} is {scopeLabel}, was persisted through DbaClientX.SQLite at {queueSession.CreatedAtUtc:yyyy-MM-dd HH:mm} UTC, and can become the resumable execution spine.";
        ApplyPortfolioFocus();
        ApplyRepositoryFamilies();
        ApplyPortfolioDashboardCards();
        ApplyReleaseInbox(_portfolioSnapshot, queueSession);
        ApplySigningStation(queueSession);
        ApplyPublishStation(queueSession);
        ApplyVerificationStation(queueSession);
        ApplyRepositoryDetail();
        RaiseCommandStates();
    }

    private void ApplyRepositoryDetail()
    {
        var snapshot = _repositoryDetailService.CreateDetail(SelectedRepository, _activeQueueSession, _buildEngineResolution);

        RepositoryDetailHeadline = snapshot.RepositoryName;
        RepositoryDetailBadge = snapshot.RepositoryBadge;
        RepositoryDetailReadiness = snapshot.ReadinessDisplay;
        RepositoryDetailReason = snapshot.ReadinessReason;
        RepositoryDetailPath = snapshot.RootPath;
        RepositoryDetailBranch = snapshot.BranchDisplay;
        RepositoryDetailBuildContract = snapshot.BuildContractDisplay;
        RepositoryDetailQueueLane = snapshot.QueueLaneDisplay;
        RepositoryDetailQueueCheckpoint = snapshot.QueueCheckpointDisplay;
        RepositoryDetailQueueSummary = snapshot.QueueSummary;
        RepositoryDetailQueuePayload = snapshot.QueueCheckpointPayload;
        RepositoryDetailReleaseDrift = snapshot.ReleaseDriftDisplay;
        RepositoryDetailReleaseDriftDetail = snapshot.ReleaseDriftDetail;
        RepositoryDetailEngineDisplay = snapshot.BuildEngineDisplay;
        RepositoryDetailEnginePath = snapshot.BuildEnginePath;
        RepositoryDetailEngineAdvisory = snapshot.BuildEngineAdvisory;

        SelectedRepositoryEvidence.Clear();
        foreach (var evidence in snapshot.AdapterEvidence)
        {
            SelectedRepositoryEvidence.Add(evidence);
        }

        ApplyRepositoryFamilies();
        RaiseCommandStates();
    }

    private void ApplyGitHubInbox(IReadOnlyList<RepositoryPortfolioItem> portfolioItems, RepositoryPortfolioSummary summary)
    {
        GitHubInboxItems.Clear();
        foreach (var item in portfolioItems
                     .Where(portfolioItem => portfolioItem.GitHubInbox is not null)
                     .OrderByDescending(portfolioItem => portfolioItem.GitHubInbox?.Status == RepositoryGitHubInboxStatus.Attention)
                     .ThenByDescending(portfolioItem => portfolioItem.GitHubInbox?.OpenPullRequestCount ?? 0)
                     .ThenBy(portfolioItem => portfolioItem.Name, StringComparer.OrdinalIgnoreCase)
                     .Take(8))
        {
            GitHubInboxItems.Add(item);
        }

        GitHubInboxHeadline = $"{summary.GitHubAttentionRepositories} repo(s) need GitHub attention, {summary.OpenPullRequests} open PR(s)";
        GitHubInboxDetails = "The inbox probes origin GitHub remotes for open pull requests, the latest workflow run, and the latest release tag. Repositories outside the probe limit stay explicitly marked as deferred instead of pretending everything is known.";
    }

    private void ApplyReleaseDrift(IReadOnlyList<RepositoryPortfolioItem> portfolioItems, RepositoryPortfolioSummary summary)
    {
        var latestTagCount = portfolioItems.Count(item => !string.IsNullOrWhiteSpace(item.GitHubInbox?.LatestReleaseTag));
        ReleaseDriftHeadline = $"{summary.ReleaseDriftAttentionRepositories} repo(s) show release drift, {latestTagCount} repo(s) have a detected release tag";
        ReleaseDriftDetails = "Release drift is a first-pass signal: it highlights repos that appear ahead of their latest detected GitHub release boundary, have open PR pressure, or have local changes that move them beyond the last release marker.";
    }

    private void ApplySigningStation(ReleaseQueueSession queueSession)
    {
        var manifest = _buildCheckpointReader.BuildSigningManifest(queueSession.Items);

        SigningArtifacts.Clear();
        foreach (var artifact in manifest.Take(12))
        {
            SigningArtifacts.Add(artifact);
        }

        var waitingItems = queueSession.Items.Count(item => item.Stage == ReleaseQueueStage.Sign && item.Status == ReleaseQueueItemStatus.WaitingApproval);
        if (waitingItems == 0)
        {
            SigningHeadline = "No signing batch waiting.";
            SigningDetails = "Build outputs that require the USB token will appear here once the queue reaches the signing gate.";
            return;
        }

        SigningHeadline = $"{waitingItems} repo(s) waiting for USB approval, {manifest.Count} artifact target(s) collected";
        SigningDetails = "This is the signing handoff surface: approve when the USB token is ready, then move the batch into publish.";
    }

    private void ApplySigningReceipts(IReadOnlyList<ReleaseSigningReceipt> receipts)
    {
        SigningReceipts.Clear();
        foreach (var receipt in receipts.Take(12))
        {
            SigningReceipts.Add(receipt);
        }

        if (receipts.Count == 0)
        {
            ReceiptHeadline = "No signing receipts yet.";
            ReceiptDetails = "Signing outcomes will be stored through DbaClientX.SQLite before publish unlocks.";
            return;
        }

        var signed = receipts.Count(receipt => receipt.Status == ReleaseSigningReceiptStatus.Signed);
        var skipped = receipts.Count(receipt => receipt.Status == ReleaseSigningReceiptStatus.Skipped);
        var failed = receipts.Count(receipt => receipt.Status == ReleaseSigningReceiptStatus.Failed);
        ReceiptHeadline = $"{signed} signed, {skipped} skipped, {failed} failed";
        ReceiptDetails = "The latest signing batch is persisted so publish does not unlock on memory alone.";
    }

    private void ApplyPublishStation(ReleaseQueueSession queueSession)
    {
        var manifest = _publishExecutionService.BuildPendingTargets(queueSession.Items);

        PublishTargets.Clear();
        foreach (var target in manifest.Take(12))
        {
            PublishTargets.Add(target);
        }

        if (manifest.Count == 0)
        {
            PublishHeadline = "No publish batch ready.";
            PublishDetails = "Publish targets will appear here after signing succeeds and before verify unlocks.";
            return;
        }

        PublishHeadline = $"{manifest.Count} publish target(s) ready";
        PublishDetails = "This station is the final external boundary: publish only runs when RELEASE_OPS_STUDIO_ENABLE_PUBLISH=true.";
    }

    private void ApplyPublishReceipts(IReadOnlyList<ReleasePublishReceipt> receipts)
    {
        PublishReceipts.Clear();
        foreach (var receipt in receipts.Take(12))
        {
            PublishReceipts.Add(receipt);
        }

        if (receipts.Count == 0)
        {
            PublishReceiptHeadline = "No publish receipts yet.";
            PublishReceiptDetails = "Publish outcomes will be stored through DbaClientX.SQLite before verification closes the queue item.";
            return;
        }

        var published = receipts.Count(receipt => receipt.Status == ReleasePublishReceiptStatus.Published);
        var skipped = receipts.Count(receipt => receipt.Status == ReleasePublishReceiptStatus.Skipped);
        var failed = receipts.Count(receipt => receipt.Status == ReleasePublishReceiptStatus.Failed);
        PublishReceiptHeadline = $"{published} published, {skipped} skipped, {failed} failed";
        PublishReceiptDetails = "The latest publish batch is persisted so verification is grounded in recorded external outcomes.";
    }

    private void ApplyVerificationStation(ReleaseQueueSession queueSession)
    {
        var manifest = _verificationExecutionService.BuildPendingTargets(queueSession.Items);

        VerificationTargets.Clear();
        foreach (var target in manifest.Take(12))
        {
            VerificationTargets.Add(target);
        }

        if (manifest.Count == 0)
        {
            VerificationHeadline = "No verification batch ready.";
            VerificationDetails = "Verification checks will appear here once publish succeeds and evidence is ready to confirm.";
            return;
        }

        VerificationHeadline = $"{manifest.Count} verification check(s) ready";
        VerificationDetails = "This station closes the queue on evidence: release URLs, package presence, and repository publish checks where we can confirm them.";
    }

    private void ApplyVerificationReceipts(IReadOnlyList<ReleaseVerificationReceipt> receipts)
    {
        VerificationReceipts.Clear();
        foreach (var receipt in receipts.Take(12))
        {
            VerificationReceipts.Add(receipt);
        }

        if (receipts.Count == 0)
        {
            VerificationReceiptHeadline = "No verification receipts yet.";
            VerificationReceiptDetails = "Verification outcomes will be stored through DbaClientX.SQLite before a queue item is marked completed.";
            return;
        }

        var verified = receipts.Count(receipt => receipt.Status == ReleaseVerificationReceiptStatus.Verified);
        var skipped = receipts.Count(receipt => receipt.Status == ReleaseVerificationReceiptStatus.Skipped);
        var failed = receipts.Count(receipt => receipt.Status == ReleaseVerificationReceiptStatus.Failed);
        VerificationReceiptHeadline = $"{verified} verified, {skipped} skipped, {failed} failed";
        VerificationReceiptDetails = "The latest verification batch is persisted so completion reflects recorded checks, not just queue advancement.";
    }

    private void RaiseCommandStates()
    {
        if (RefreshPortfolioCommand is AsyncDelegateCommand refresh)
        {
            refresh.RaiseCanExecuteChanged();
        }

        if (RunNextQueueStepCommand is AsyncDelegateCommand runNext)
        {
            runNext.RaiseCanExecuteChanged();
        }

        if (ApproveUsbCommand is AsyncDelegateCommand approveUsb)
        {
            approveUsb.RaiseCanExecuteChanged();
        }

        if (RetryFailedCommand is AsyncDelegateCommand retryFailed)
        {
            retryFailed.RaiseCanExecuteChanged();
        }

        if (PrepareSelectedFamilyQueueCommand is AsyncDelegateCommand prepareFamily)
        {
            prepareFamily.RaiseCanExecuteChanged();
        }

        if (RetrySelectedFamilyFailedCommand is AsyncDelegateCommand retryFamily)
        {
            retryFamily.RaiseCanExecuteChanged();
        }
    }

    private bool CanPrepareSelectedFamilyQueue()
        => !IsBusy && ResolveSelectedFamily() is not null;

    private bool CanRetrySelectedFamilyFailed()
    {
        if (IsBusy || _activeQueueSession is null)
        {
            return false;
        }

        var family = ResolveSelectedFamily();
        if (family is null)
        {
            return false;
        }

        var familyRootPaths = _portfolioSnapshot
            .Where(item => string.Equals(item.FamilyKey, family.FamilyKey, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.RootPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return _activeQueueSession.Items.Any(item =>
            item.Status == ReleaseQueueItemStatus.Failed
            && familyRootPaths.Contains(item.RootPath));
    }

    private RepositoryWorkspaceFamilySnapshot? ResolveSelectedFamily()
    {
        var familyKey = _selectedRepositoryFamilyKey ?? SelectedRepository?.FamilyKey;
        if (string.IsNullOrWhiteSpace(familyKey))
        {
            return null;
        }

        return RepositoryFamilies.FirstOrDefault(family =>
            !string.IsNullOrWhiteSpace(family.FamilyKey)
            && string.Equals(family.FamilyKey, familyKey, StringComparison.OrdinalIgnoreCase))
            ?? _workspaceFamilyService.BuildFamilies(_portfolioSnapshot, _activeQueueSession).FirstOrDefault(family =>
                string.Equals(family.FamilyKey, familyKey, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveWorkspaceRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("RELEASE_OPS_STUDIO_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot) && Directory.Exists(configuredRoot))
        {
            return configuredRoot!;
        }

        const string defaultWindowsRoot = @"C:\Support\GitHub";
        if (Directory.Exists(defaultWindowsRoot))
        {
            return defaultWindowsRoot;
        }

        return Environment.CurrentDirectory;
    }
}
