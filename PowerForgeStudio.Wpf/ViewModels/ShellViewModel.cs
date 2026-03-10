using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.PowerShell;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Portfolio;
using PowerForgeStudio.Orchestrator.PowerShell;
using PowerForgeStudio.Orchestrator.Queue;
using PowerForgeStudio.Orchestrator.Storage;
using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed class ShellViewModel : ViewModelBase
{
    private readonly RepositoryCatalogScanner _catalogScanner = new();
    private readonly RepositoryPortfolioService _portfolioService = new();
    private readonly RepositoryPortfolioFocusService _portfolioFocusService = new();
    private readonly RepositoryWorkspaceFamilyService _workspaceFamilyService = new();
    private readonly RepositoryReleaseInboxService _releaseInboxService = new();
    private readonly RepositoryDetailService _repositoryDetailService = new();
    private readonly RepositoryGitQuickActionExecutionService _gitQuickActionExecutionService = new();
    private readonly IWorkspaceSnapshotService _workspaceSnapshotService = new WorkspaceSnapshotService();
    private readonly GitHubInboxService _gitHubInboxService = new();
    private readonly RepositoryReleaseDriftService _releaseDriftService = new();
    private readonly RepositoryPlanPreviewService _planPreviewService = new();
    private readonly ReleaseStationProjectionService _stationProjectionService = new();
    private readonly IReleaseQueueCommandService _queueCommandService = new ReleaseQueueCommandService();
    private CancellationTokenSource? _portfolioViewSaveCts;
    private bool _isInitialized;
    private bool _isBusy;
    private bool _isRestoringPortfolioViewState;
    private ReleaseQueueSession? _activeQueueSession;
    private IReadOnlyList<RepositoryPortfolioItem> _portfolioSnapshot = [];
    private IReadOnlyDictionary<string, RepositoryGitQuickActionReceipt> _gitQuickActionReceiptLookup = new Dictionary<string, RepositoryGitQuickActionReceipt>(StringComparer.OrdinalIgnoreCase);
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
    private string _repositoryDetailGitDiagnostics = "No git diagnostics yet.";
    private string _repositoryDetailGitDiagnosticsDetail = "Git preflight guidance will appear here once the shell inspects a repository.";
    private string _repositoryDetailLastGitAction = "No git action recorded yet.";
    private string _repositoryDetailLastGitActionSummary = "Quick-action results will appear here after you run a git action from the repository detail pane.";
    private string _repositoryDetailLastGitActionOutput = "No output captured yet.";
    private string _repositoryDetailLastGitActionError = "No error captured yet.";
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
        CopyGitRemediationCommand = new DelegateCommand<RepositoryGitRemediationStep>(CopyGitRemediation, step => step is not null && !string.IsNullOrWhiteSpace(step.CommandText));
        ExecuteGitQuickActionCommand = new DelegateCommand<RepositoryGitQuickAction>(ExecuteGitQuickAction, action => action is not null);

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
            "Add remote branch-governance probes so GitHub API signals can confirm branch protection and default-branch policy instead of relying only on local heuristics.",
            "Add git remediation actions and guidance for common cases like creating a PR branch, setting upstream, or recovering from detached HEAD."
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

    public ObservableCollection<RepositoryGitRemediationStep> SelectedRepositoryGitRemediationSteps { get; } = [];

    public ObservableCollection<RepositoryGitQuickAction> SelectedRepositoryGitQuickActions { get; } = [];

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

    public ICommand CopyGitRemediationCommand { get; }

    public ICommand ExecuteGitQuickActionCommand { get; }

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

    public string RepositoryDetailGitDiagnostics
    {
        get => _repositoryDetailGitDiagnostics;
        private set => SetProperty(ref _repositoryDetailGitDiagnostics, value);
    }

    public string RepositoryDetailGitDiagnosticsDetail
    {
        get => _repositoryDetailGitDiagnosticsDetail;
        private set => SetProperty(ref _repositoryDetailGitDiagnosticsDetail, value);
    }

    public string RepositoryDetailLastGitAction
    {
        get => _repositoryDetailLastGitAction;
        private set => SetProperty(ref _repositoryDetailLastGitAction, value);
    }

    public string RepositoryDetailLastGitActionSummary
    {
        get => _repositoryDetailLastGitActionSummary;
        private set => SetProperty(ref _repositoryDetailLastGitActionSummary, value);
    }

    public string RepositoryDetailLastGitActionOutput
    {
        get => _repositoryDetailLastGitActionOutput;
        private set => SetProperty(ref _repositoryDetailLastGitActionOutput, value);
    }

    public string RepositoryDetailLastGitActionError
    {
        get => _repositoryDetailLastGitActionError;
        private set => SetProperty(ref _repositoryDetailLastGitActionError, value);
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
            StatusText = $"Scanning {WorkspaceRoot}...";

            StatusText = $"Running plan preview for up to 12 repositories...";
            var snapshot = await _workspaceSnapshotService.RefreshAsync(
                WorkspaceRoot,
                DatabasePath,
                new WorkspaceRefreshOptions(
                    MaxPlanRepositories: 12,
                    MaxGitHubRepositories: 15,
                    PersistState: true),
                cancellationToken).ConfigureAwait(true);

            ApplySavedPortfolioView(snapshot.SavedPortfolioView);

            var persistedPortfolio = snapshot.PortfolioItems;
            var summary = snapshot.Summary;
            var persistedQueue = snapshot.QueueSession;
            _gitQuickActionReceiptLookup = snapshot.GitQuickActionReceipts
                .GroupBy(receipt => receipt.RootPath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(receipt => receipt.ExecutedAtUtc).First(),
                    StringComparer.OrdinalIgnoreCase);

            _buildEngineResolution = snapshot.BuildEngineResolution;
            _activeQueueSession = persistedQueue;
            ApplyPortfolio(persistedPortfolio, summary);
            ApplyReleaseInboxItems(snapshot.ReleaseInboxItems);
            ApplyGitHubInbox(persistedPortfolio, summary);
            ApplyReleaseDrift(persistedPortfolio, summary);
            ApplyBuildEngine(snapshot.BuildEngineResolution);
            ApplyQueue(persistedQueue);
            ApplyPortfolioDashboardSnapshots(snapshot.DashboardCards);
            ApplyRepositoryFamiliesSnapshot(snapshot.RepositoryFamilies);
            ApplyRepositoryFamilyLaneSnapshots(snapshot.RepositoryFamilyLanes);
            ApplySigningStationSnapshot(snapshot.SigningStation);
            ApplySigningReceiptBatch(snapshot.SigningReceiptBatch);
            ApplyPublishStationSnapshot(snapshot.PublishStation);
            ApplyPublishReceiptBatch(snapshot.PublishReceiptBatch);
            ApplyVerificationStationSnapshot(snapshot.VerificationStation);
            ApplyVerificationReceiptBatch(snapshot.VerificationReceiptBatch);
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
            StatusText = "Running the next reusable queue step...";
            var result = await _queueCommandService.RunNextReadyItemAsync(DatabasePath).ConfigureAwait(true);
            ApplyQueueCommandResult(result);
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
            StatusText = "Approving the reusable USB signing step...";
            var result = await _queueCommandService.ApproveUsbAsync(DatabasePath).ConfigureAwait(true);
            ApplyQueueCommandResult(result);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RetryFailedAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _queueCommandService.RetryFailedAsync(DatabasePath).ConfigureAwait(true);
            ApplyQueueCommandResult(result);
        }
        finally
        {
            IsBusy = false;
        }
    }

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
            var result = await _queueCommandService.PrepareQueueAsync(
                DatabasePath,
                WorkspaceRoot,
                familyItems,
                scopeKey: family.FamilyKey,
                scopeDisplayName: family.DisplayName).ConfigureAwait(true);
            ApplyQueueCommandResult(result);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RetrySelectedFamilyFailedAsync()
    {
        var family = ResolveSelectedFamily();
        if (family is null)
        {
            StatusText = "Select a repository family first, then retry failed items for that family.";
            return;
        }

        var familyRootPaths = _portfolioSnapshot
            .Where(item => string.Equals(item.FamilyKey, family.FamilyKey, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.RootPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        IsBusy = true;
        try
        {
            var result = await _queueCommandService.RetryFailedAsync(
                DatabasePath,
                item => familyRootPaths.Contains(item.RootPath)).ConfigureAwait(true);
            ApplyQueueCommandResult(result);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyQueueCommandResult(ReleaseQueueCommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.QueueSession is not null)
        {
            _activeQueueSession = result.QueueSession;
            ApplyQueue(result.QueueSession);
            ApplySigningStationSnapshot(_stationProjectionService.BuildSigningStation(result.QueueSession));
            ApplySigningReceiptBatch(_stationProjectionService.BuildSigningReceipts(result.SigningReceipts));
            ApplyPublishStationSnapshot(_stationProjectionService.BuildPublishStation(result.QueueSession));
            ApplyPublishReceiptBatch(_stationProjectionService.BuildPublishReceipts(result.PublishReceipts));
            ApplyVerificationStationSnapshot(_stationProjectionService.BuildVerificationStation(result.QueueSession));
            ApplyVerificationReceiptBatch(_stationProjectionService.BuildVerificationReceipts(result.VerificationReceipts));
            ApplyReleaseInbox(_portfolioSnapshot, result.QueueSession);
            ApplyRepositoryFamilies();
            ApplyPortfolioFocus();
            ApplyPortfolioDashboardCards();
        }

        StatusText = result.Message;
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
        var inboxItems = _releaseInboxService.BuildInbox(portfolioItems, queueSession, _gitQuickActionReceiptLookup);
        ApplyReleaseInboxItems(inboxItems);
    }

    private void ApplyReleaseInboxItems(IReadOnlyList<RepositoryReleaseInboxItem> inboxItems)
    {
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
        var gitActionFailedCount = inboxItems.Count(item => item.Badge == "Git Action Failed");
        var usbCount = inboxItems.Count(item => item.Badge == "USB Waiting");
        ReleaseInboxHeadline = $"{inboxItems.Count} action item(s), {failedCount} queue failed, {gitActionFailedCount} git-action failed, {usbCount} USB waiting";
        ReleaseInboxDetails = "This inbox ranks queue failures, failed git quick actions, git guard issues, USB pauses, and release-ready work first, so you can start at the top instead of scanning the whole shell.";
    }

    private void ApplyRepositoryFamilies()
    {
        var families = _workspaceFamilyService.BuildFamilies(_portfolioSnapshot, _activeQueueSession, _gitQuickActionReceiptLookup);
        ApplyRepositoryFamiliesSnapshot(families);
    }

    private void ApplyRepositoryFamiliesSnapshot(IReadOnlyList<RepositoryWorkspaceFamilySnapshot> families)
    {
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
        ApplyRepositoryFamilyLane(_workspaceFamilyService.BuildFamilyLane(_portfolioSnapshot, _activeQueueSession, selectedFamily.FamilyKey, _gitQuickActionReceiptLookup));
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

    private void ApplyRepositoryFamilyLaneSnapshots(IReadOnlyList<RepositoryWorkspaceFamilyLaneSnapshot> lanes)
    {
        var familyKey = _selectedRepositoryFamilyKey ?? SelectedRepository?.FamilyKey;
        var selectedLane = string.IsNullOrWhiteSpace(familyKey)
            ? null
            : lanes.FirstOrDefault(lane => string.Equals(lane.FamilyKey, familyKey, StringComparison.OrdinalIgnoreCase));
        ApplyRepositoryFamilyLane(selectedLane);
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
        ApplyPortfolioDashboardSnapshots(dashboardCards.Select(card =>
            new PortfolioDashboardSnapshot(
                card.Key,
                card.Title,
                card.CountDisplay,
                card.Detail,
                card.FocusMode,
                card.SearchText,
                card.PresetKey)).ToArray());
    }

    private void ApplyPortfolioDashboardSnapshots(IReadOnlyList<PortfolioDashboardSnapshot> dashboardCards)
    {
        PortfolioDashboardCards.Clear();
        foreach (var card in dashboardCards)
        {
            PortfolioDashboardCards.Add(new PortfolioDashboardCard(
                card.Key,
                card.Title,
                card.CountDisplay,
                card.Detail,
                card.FocusMode,
                card.SearchText,
                card.PresetKey));
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

    private void CopyGitRemediation(RepositoryGitRemediationStep? step)
    {
        if (step is null || string.IsNullOrWhiteSpace(step.CommandText))
        {
            return;
        }

        Clipboard.SetText(step.CommandText);
        StatusText = $"Copied git command: {step.Title}.";
    }

    private void ExecuteGitQuickAction(RepositoryGitQuickAction? action)
    {
        if (action is null)
        {
            return;
        }

        _ = ExecuteGitQuickActionAsync(action);
    }

    private async Task ExecuteGitQuickActionAsync(RepositoryGitQuickAction action)
    {
        var repository = SelectedRepository;
        if (repository is null)
        {
            StatusText = "Select a repository first.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _gitQuickActionExecutionService.ExecuteAsync(repository.RootPath, action).ConfigureAwait(true);
            var receipt = new RepositoryGitQuickActionReceipt(
                RootPath: repository.RootPath,
                ActionTitle: action.Title,
                ActionKind: action.Kind,
                Payload: action.Payload,
                Succeeded: result.Succeeded,
                Summary: result.Summary,
                OutputTail: result.OutputTail,
                ErrorTail: result.ErrorTail,
                ExecutedAtUtc: DateTimeOffset.UtcNow);
            var stateDatabase = new ReleaseStateDatabase(DatabasePath);
            await stateDatabase.InitializeAsync().ConfigureAwait(true);
            await stateDatabase.PersistGitQuickActionReceiptAsync(receipt).ConfigureAwait(true);
            var updatedLookup = new Dictionary<string, RepositoryGitQuickActionReceipt>(_gitQuickActionReceiptLookup, StringComparer.OrdinalIgnoreCase) {
                [repository.RootPath] = receipt
            };
            _gitQuickActionReceiptLookup = updatedLookup;
            var tail = string.IsNullOrWhiteSpace(result.ErrorTail)
                ? result.OutputTail
                : result.ErrorTail;
            StatusText = string.IsNullOrWhiteSpace(tail)
                ? result.Summary
                : $"{result.Summary} {tail}";
            ApplyRepositoryDetail();

            if (result.Succeeded && action.Kind == RepositoryGitQuickActionKind.GitCommand)
            {
                await RefreshAsync(forceRefresh: true).ConfigureAwait(true);
            }
        }
        finally
        {
            IsBusy = false;
        }
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
        ApplySigningStationSnapshot(_stationProjectionService.BuildSigningStation(queueSession));
        ApplyPublishStationSnapshot(_stationProjectionService.BuildPublishStation(queueSession));
        ApplyVerificationStationSnapshot(_stationProjectionService.BuildVerificationStation(queueSession));
        ApplyRepositoryDetail();
        RaiseCommandStates();
    }

    private void ApplyRepositoryDetail()
    {
        var quickActionReceipt = SelectedRepository is null
            ? null
            : _gitQuickActionReceiptLookup.GetValueOrDefault(SelectedRepository.RootPath);
        var snapshot = _repositoryDetailService.CreateDetail(SelectedRepository, _activeQueueSession, _buildEngineResolution, quickActionReceipt);

        RepositoryDetailHeadline = snapshot.RepositoryName;
        RepositoryDetailBadge = snapshot.RepositoryBadge;
        RepositoryDetailReadiness = snapshot.ReadinessDisplay;
        RepositoryDetailReason = snapshot.ReadinessReason;
        RepositoryDetailPath = snapshot.RootPath;
        RepositoryDetailBranch = snapshot.BranchDisplay;
        RepositoryDetailGitDiagnostics = snapshot.GitDiagnosticsDisplay;
        RepositoryDetailGitDiagnosticsDetail = snapshot.GitDiagnosticsDetail;
        RepositoryDetailLastGitAction = snapshot.LastGitActionDisplay;
        RepositoryDetailLastGitActionSummary = snapshot.LastGitActionSummary;
        RepositoryDetailLastGitActionOutput = snapshot.LastGitActionOutput;
        RepositoryDetailLastGitActionError = snapshot.LastGitActionError;
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

        SelectedRepositoryGitRemediationSteps.Clear();
        foreach (var step in snapshot.GitRemediationSteps)
        {
            SelectedRepositoryGitRemediationSteps.Add(step);
        }

        SelectedRepositoryGitQuickActions.Clear();
        foreach (var action in snapshot.GitQuickActions)
        {
            SelectedRepositoryGitQuickActions.Add(action);
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

    private void ApplySigningStationSnapshot(StationSnapshot<ReleaseSigningArtifact> snapshot)
    {
        SigningArtifacts.Clear();
        foreach (var artifact in snapshot.Items.Take(12))
        {
            SigningArtifacts.Add(artifact);
        }
        SigningHeadline = snapshot.Headline;
        SigningDetails = snapshot.Details;
    }

    private void ApplySigningReceiptBatch(ReceiptBatchSnapshot<ReleaseSigningReceipt> batch)
    {
        SigningReceipts.Clear();
        foreach (var receipt in batch.Items.Take(12))
        {
            SigningReceipts.Add(receipt);
        }
        ReceiptHeadline = batch.Headline;
        ReceiptDetails = batch.Details;
    }

    private void ApplyPublishStationSnapshot(StationSnapshot<ReleasePublishTarget> snapshot)
    {
        PublishTargets.Clear();
        foreach (var target in snapshot.Items.Take(12))
        {
            PublishTargets.Add(target);
        }
        PublishHeadline = snapshot.Headline;
        PublishDetails = snapshot.Details;
    }

    private void ApplyPublishReceiptBatch(ReceiptBatchSnapshot<ReleasePublishReceipt> batch)
    {
        PublishReceipts.Clear();
        foreach (var receipt in batch.Items.Take(12))
        {
            PublishReceipts.Add(receipt);
        }
        PublishReceiptHeadline = batch.Headline;
        PublishReceiptDetails = batch.Details;
    }

    private void ApplyVerificationStationSnapshot(StationSnapshot<ReleaseVerificationTarget> snapshot)
    {
        VerificationTargets.Clear();
        foreach (var target in snapshot.Items.Take(12))
        {
            VerificationTargets.Add(target);
        }
        VerificationHeadline = snapshot.Headline;
        VerificationDetails = snapshot.Details;
    }

    private void ApplyVerificationReceiptBatch(ReceiptBatchSnapshot<ReleaseVerificationReceipt> batch)
    {
        VerificationReceipts.Clear();
        foreach (var receipt in batch.Items.Take(12))
        {
            VerificationReceipts.Add(receipt);
        }
        VerificationReceiptHeadline = batch.Headline;
        VerificationReceiptDetails = batch.Details;
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
            ?? _workspaceFamilyService.BuildFamilies(_portfolioSnapshot, _activeQueueSession, _gitQuickActionReceiptLookup).FirstOrDefault(family =>
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
