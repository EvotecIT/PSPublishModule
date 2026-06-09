using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.PowerShell;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Host;
using PowerForgeStudio.Orchestrator.Portfolio;
using PowerForgeStudio.Orchestrator.PowerShell;
using PowerForgeStudio.Orchestrator.Queue;
using PowerForgeStudio.Orchestrator.Storage;
using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed class ShellViewModel : ViewModelBase
{
    private readonly RepositoryWorkspaceFamilyService _workspaceFamilyService;
    private readonly RepositoryDetailService _repositoryDetailService;
    private readonly IShellWorkspaceProjectionService _workspaceProjectionService;
    private readonly IPortfolioInteractionService _portfolioInteractionService;
    private readonly IFamilyQueueActionService _familyQueueActionService;
    private readonly IRepositoryGitQuickActionWorkflowService _gitQuickActionWorkflowService;
    private readonly IWorkspaceSnapshotService _workspaceSnapshotService;
    private readonly ReleaseStationProjectionService _stationProjectionService;
    private readonly IReleaseQueueCommandService _queueCommandService;
    private readonly PortfolioViewStateService _portfolioViewStateService;
    private readonly PortfolioViewStateSaveScheduler _portfolioViewStateSaveScheduler;
    private readonly IWorkspaceRootCatalogService _workspaceRootCatalogService;
    private readonly ShellWorkspaceSessionState _workspaceState;
    private bool _isInitialized;
    private bool _isBusy;
    private bool _isRestoringPortfolioViewState;
    private string? _activeWorkspaceProfileId;
    private string _statusText = "Ready to scan the workspace.";
    private string _workspaceRoot = ResolveWorkspaceRoot();
    private string _databasePath = ReleaseStateDatabase.GetDefaultDatabasePath();
    private int _selectedStageIndex;

    public ShellViewModel()
        : this(ShellViewModelServices.CreateDefault())
    {
    }

    public ShellViewModel(ShellViewModelServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _workspaceFamilyService = services.WorkspaceFamilyService ?? throw new ArgumentNullException(nameof(services.WorkspaceFamilyService));
        _repositoryDetailService = services.RepositoryDetailService ?? throw new ArgumentNullException(nameof(services.RepositoryDetailService));
        _workspaceProjectionService = services.WorkspaceProjectionService ?? throw new ArgumentNullException(nameof(services.WorkspaceProjectionService));
        _portfolioInteractionService = services.PortfolioInteractionService ?? throw new ArgumentNullException(nameof(services.PortfolioInteractionService));
        _familyQueueActionService = services.FamilyQueueActionService ?? throw new ArgumentNullException(nameof(services.FamilyQueueActionService));
        _gitQuickActionWorkflowService = services.GitQuickActionWorkflowService ?? throw new ArgumentNullException(nameof(services.GitQuickActionWorkflowService));
        _workspaceSnapshotService = services.WorkspaceSnapshotService ?? throw new ArgumentNullException(nameof(services.WorkspaceSnapshotService));
        _stationProjectionService = services.StationProjectionService ?? throw new ArgumentNullException(nameof(services.StationProjectionService));
        _queueCommandService = services.QueueCommandService ?? throw new ArgumentNullException(nameof(services.QueueCommandService));
        _portfolioViewStateService = services.PortfolioViewStateService ?? throw new ArgumentNullException(nameof(services.PortfolioViewStateService));
        _portfolioViewStateSaveScheduler = new PortfolioViewStateSaveScheduler(
            services.PortfolioViewStatePersistenceService ?? throw new ArgumentNullException(nameof(services.PortfolioViewStatePersistenceService)),
            services.PortfolioViewStateSaveDelay);
        _workspaceRootCatalogService = services.WorkspaceRootCatalogService ?? throw new ArgumentNullException(nameof(services.WorkspaceRootCatalogService));
        _workspaceState = new ShellWorkspaceSessionState(PSPublishModuleLocator.Resolve());

        RefreshPortfolioCommand = new AsyncDelegateCommand(() => RefreshAsync(forceRefresh: true), () => !IsBusy);
        RunNextQueueStepCommand = new AsyncDelegateCommand(RunNextQueueStepAsync, () => !IsBusy && Stations.DraftQueue.Any(item => item.Status == ReleaseQueueItemStatus.ReadyToRun));
        ApproveUsbCommand = new AsyncDelegateCommand(ApproveUsbAsync, () => !IsBusy && Stations.DraftQueue.Any(item => item.Status == ReleaseQueueItemStatus.WaitingApproval && item.Stage == ReleaseQueueStage.Sign));
        RetryFailedCommand = new AsyncDelegateCommand(RetryFailedAsync, () => !IsBusy && Stations.DraftQueue.Any(item => item.Status == ReleaseQueueItemStatus.Failed));
        PrepareSelectedFamilyQueueCommand = new AsyncDelegateCommand(PrepareSelectedFamilyQueueAsync, CanPrepareSelectedFamilyQueue);
        RetrySelectedFamilyFailedCommand = new AsyncDelegateCommand(RetrySelectedFamilyFailedAsync, CanRetrySelectedFamilyFailed);
        SavePortfolioViewCommand = new AsyncDelegateCommand(SavePortfolioViewAsync, () => !IsBusy && _isInitialized);
        DeleteSavedPortfolioViewCommand = new AsyncDelegateCommand(DeleteSelectedSavedPortfolioViewAsync, () => !IsBusy && PortfolioOverview.SelectedSavedView is not null);
        SaveWorkspaceProfileCommand = new AsyncDelegateCommand(SaveWorkspaceProfileAsync, () => !IsBusy && _isInitialized);
        SaveWorkspaceProfileTemplateCommand = new AsyncDelegateCommand(SaveWorkspaceProfileTemplateAsync, () => !IsBusy && _isInitialized);
        ApplyWorkspaceProfileTemplateCommand = new DelegateCommand<object>(
            _ => ApplySelectedWorkspaceProfileTemplate(),
            _ => !IsBusy && PortfolioOverview.SelectedWorkspaceProfileTemplate is not null);
        CreateWorkspaceProfileFromTemplateCommand = new AsyncDelegateCommand(
            CreateWorkspaceProfileFromTemplateAsync,
            () => !IsBusy && _isInitialized && PortfolioOverview.SelectedWorkspaceProfileTemplate is not null);
        DeleteWorkspaceProfileTemplateCommand = new AsyncDelegateCommand(DeleteSelectedWorkspaceProfileTemplateAsync, CanDeleteSelectedWorkspaceProfileTemplate);
        ApplyWorkspaceProfileCommand = new AsyncDelegateCommand(ApplySelectedWorkspaceProfileAsync, () => !IsBusy && PortfolioOverview.SelectedWorkspaceProfile is not null);
        DeleteWorkspaceProfileCommand = new AsyncDelegateCommand(DeleteSelectedWorkspaceProfileAsync, () => !IsBusy && PortfolioOverview.SelectedWorkspaceProfile is not null);
        PrepareWorkspaceProfileQueueCommand = new AsyncDelegateCommand(PrepareSelectedWorkspaceProfileQueueAsync, CanPrepareSelectedWorkspaceProfileQueue);
        ApplyWorkspaceProfileCardCommand = new DelegateCommand<WorkspaceProfileHeroCard>(ApplyWorkspaceProfileCard, card => card is not null);
        ApplyWorkspaceProfileLaunchActionCommand = new DelegateCommand<WorkspaceProfileLaunchAction>(
            ExecuteWorkspaceProfileLaunchAction,
            action => action is { CanExecute: true } && !IsBusy);
        ApplyPortfolioPresetCommand = new DelegateCommand<PortfolioQuickPreset>(ApplyPortfolioPreset, preset => preset is not null);
        ApplySavedPortfolioViewCommand = new DelegateCommand<object>(_ => ApplySelectedSavedPortfolioView(), _ => PortfolioOverview.SelectedSavedView is not null);
        ApplyDashboardCardCommand = new DelegateCommand<PortfolioDashboardCard>(ApplyDashboardCard, card => card is not null);
        ApplyRepositoryFamilyCommand = new DelegateCommand<RepositoryWorkspaceFamilySnapshot>(ApplyRepositoryFamily, family => family is not null);
        ApplyRepositoryFamilyLaneItemCommand = new DelegateCommand<RepositoryWorkspaceFamilyLaneItem>(ApplyRepositoryFamilyLaneItem, item => item is not null);
        ApplyReleaseInboxItemCommand = new DelegateCommand<RepositoryReleaseInboxItem>(ApplyReleaseInboxItem, item => item is not null);
        ApplyWorkspaceExplorerNodeCommand = new DelegateCommand<WorkspaceExplorerNodeViewModel>(ApplyWorkspaceExplorerNode, node => node is not null);
        ApplyQueueItemCommand = new DelegateCommand<ReleaseQueueItem>(ApplyQueueItem, item => item is not null);
        ApplySigningArtifactCommand = new DelegateCommand<ReleaseSigningArtifact>(ApplySigningArtifact, item => item is not null);
        ApplyPublishTargetCommand = new DelegateCommand<ReleasePublishTarget>(ApplyPublishTarget, item => item is not null);
        ApplyVerificationTargetCommand = new DelegateCommand<ReleaseVerificationTarget>(ApplyVerificationTarget, item => item is not null);
        CopyGitRemediationCommand = new DelegateCommand<RepositoryGitRemediationStep>(CopyGitRemediation, step => step is not null && !string.IsNullOrWhiteSpace(step.CommandText));
        ExecuteGitQuickActionCommand = new DelegateCommand<RepositoryGitQuickAction>(ExecuteGitQuickAction, action => action is not null);

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

        PortfolioOverview.PropertyChanged += OnPortfolioOverviewPropertyChanged;
        ApplyWorkspaceRootCatalog(_workspaceRootCatalogService.Load(_workspaceRoot));
        ApplyBuildEngineCard(_workspaceState.BuildEngineResolution);
        ApplyRepositoryDetail();
    }

    public ObservableCollection<RepositoryPortfolioItem> Repositories { get; } = [];

    public ObservableCollection<WorkspaceExplorerNodeViewModel> WorkspaceExplorerNodes { get; } = [];

    public ObservableCollection<string> RecentWorkspaceRoots { get; } = [];

    public ReleaseStationsViewModel Stations { get; } = new();

    public RepositoryDetailViewModel RepositoryDetail { get; } = new();

    public PortfolioOverviewViewModel PortfolioOverview { get; } = new();

    public RepositoryFamilyViewModel RepositoryFamily { get; } = new();

    public ReleaseSignalsViewModel ReleaseSignals { get; } = new();

    public ICommand RefreshPortfolioCommand { get; }

    public ICommand RunNextQueueStepCommand { get; }

    public ICommand ApproveUsbCommand { get; }

    public ICommand RetryFailedCommand { get; }

    public ICommand PrepareSelectedFamilyQueueCommand { get; }

    public ICommand RetrySelectedFamilyFailedCommand { get; }

    public ICommand SavePortfolioViewCommand { get; }

    public ICommand DeleteSavedPortfolioViewCommand { get; }

    public ICommand SaveWorkspaceProfileCommand { get; }

    public ICommand SaveWorkspaceProfileTemplateCommand { get; }

    public ICommand ApplyWorkspaceProfileTemplateCommand { get; }

    public ICommand CreateWorkspaceProfileFromTemplateCommand { get; }

    public ICommand DeleteWorkspaceProfileTemplateCommand { get; }

    public ICommand ApplyWorkspaceProfileCommand { get; }

    public ICommand DeleteWorkspaceProfileCommand { get; }

    public ICommand PrepareWorkspaceProfileQueueCommand { get; }

    public ICommand ApplyWorkspaceProfileCardCommand { get; }

    public ICommand ApplyWorkspaceProfileLaunchActionCommand { get; }

    public ICommand ApplyPortfolioPresetCommand { get; }

    public ICommand ApplySavedPortfolioViewCommand { get; }

    public ICommand ApplyDashboardCardCommand { get; }

    public ICommand ApplyRepositoryFamilyCommand { get; }

    public ICommand ApplyRepositoryFamilyLaneItemCommand { get; }

    public ICommand ApplyReleaseInboxItemCommand { get; }

    public ICommand ApplyWorkspaceExplorerNodeCommand { get; }

    public ICommand ApplyQueueItemCommand { get; }

    public ICommand ApplySigningArtifactCommand { get; }

    public ICommand ApplyPublishTargetCommand { get; }

    public ICommand ApplyVerificationTargetCommand { get; }

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

    public int SelectedStageIndex
    {
        get => _selectedStageIndex;
        set => SetProperty(ref _selectedStageIndex, value);
    }

    public string WorkspaceRoot
    {
        get => _workspaceRoot;
        set
        {
            var normalizedRoot = NormalizeWorkspaceRootInput(value);
            if (SetProperty(ref _workspaceRoot, normalizedRoot))
            {
                DatabasePath = ResolveDatabasePath(normalizedRoot);
                HandleWorkspaceRootChanged();
            }
        }
    }

    public string DatabasePath
    {
        get => _databasePath;
        private set => SetProperty(ref _databasePath, value);
    }

    public RepositoryPortfolioItem? SelectedRepository
    {
        get => _workspaceState.SelectedRepository;
        set
        {
            if (!EqualityComparer<RepositoryPortfolioItem?>.Default.Equals(_workspaceState.SelectedRepository, value))
            {
                _workspaceState.SetSelectedRepository(value);
                RaisePropertyChanged();
                ApplyCurrentProjection();
            }
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var startupProfile = ResolveActiveWorkspaceProfile();
        if (startupProfile is not null)
        {
            StatusText = $"Reopening workspace profile '{startupProfile.DisplayName}'...";
        }
        else if (!string.IsNullOrWhiteSpace(WorkspaceRoot))
        {
            StatusText = $"Reopening workspace root '{WorkspaceRoot}'...";
        }

        await RefreshAsync(forceRefresh: false, cancellationToken);
    }

    public async Task RefreshAsync(bool forceRefresh, CancellationToken cancellationToken = default)
    {
        if (!TryValidateWorkspaceRoot(out var normalizedWorkspaceRoot))
        {
            return;
        }

        if (_isInitialized && !forceRefresh)
        {
            return;
        }

        if (!string.Equals(WorkspaceRoot, normalizedWorkspaceRoot, StringComparison.Ordinal))
        {
            WorkspaceRoot = normalizedWorkspaceRoot;
        }

        _isInitialized = true;
        IsBusy = true;
        try
        {
            StatusText = $"Scanning {WorkspaceRoot}...";

            StatusText = "Running plan preview across the workspace...";
            var snapshot = await Task.Run(
                () => _workspaceSnapshotService.RefreshAsync(
                    WorkspaceRoot,
                    DatabasePath,
                    new WorkspaceRefreshOptions(
                        MaxPlanRepositories: -1,
                        MaxGitHubRepositories: -1,
                        PersistState: true),
                    cancellationToken),
                cancellationToken).ConfigureAwait(true);

            _workspaceState.ApplyWorkspaceSnapshot(snapshot);
            ApplySavedPortfolioView(snapshot.SavedPortfolioView);
            ApplyCurrentProjection(snapshot.ReleaseInboxItems);
            await ReloadSavedPortfolioViewsAsync().ConfigureAwait(true);
            ApplyWorkspaceRootCatalog(_workspaceRootCatalogService.SaveActive(WorkspaceRoot, _activeWorkspaceProfileId));
            var profileStatus = ApplyActiveWorkspaceProfileContext();
            StatusText = profileStatus
                ?? $"Portfolio ready. Persisted {snapshot.PortfolioItems.Count} portfolio rows, remote inbox signals, plan previews, and draft queue state to {DatabasePath}.";
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
        IsBusy = true;
        try
        {
            var result = await _familyQueueActionService.PrepareQueueAsync(
                DatabasePath,
                WorkspaceRoot,
                _workspaceState.PortfolioSnapshot,
                ResolveSelectedFamily()).ConfigureAwait(true);
            ApplyFamilyQueueActionResult(result);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RetrySelectedFamilyFailedAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _familyQueueActionService.RetryFailedAsync(
                DatabasePath,
                _workspaceState.ActiveQueueSession,
                _workspaceState.PortfolioSnapshot,
                ResolveSelectedFamily()).ConfigureAwait(true);
            ApplyFamilyQueueActionResult(result);
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
            _workspaceState.ApplyQueueCommandResult(result, _stationProjectionService);
            ApplyCurrentProjection();
        }

        StatusText = result.Message;
    }

    private void ApplyFamilyQueueActionResult(FamilyQueueActionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.CommandResult is not null)
        {
            ApplyQueueCommandResult(result.CommandResult);
            return;
        }

        StatusText = result.StatusMessage;
    }

    private void ApplyCurrentProjection(
        IReadOnlyList<RepositoryReleaseInboxItem>? releaseInboxItemsOverride = null,
        string? selectedRepositoryRootPathOverride = null)
    {
        var projection = _workspaceProjectionService.ApplyProjection(new ShellWorkspaceProjectionRequest(
            WorkspaceRoot: WorkspaceRoot,
            Summary: _workspaceState.PortfolioSummary,
            PortfolioItems: _workspaceState.PortfolioSnapshot,
            QueueSession: _workspaceState.ActiveQueueSession,
            GitQuickActionReceiptLookup: _workspaceState.GitQuickActionReceiptLookup,
            BuildEngineResolution: _workspaceState.BuildEngineResolution,
            SelectedRepositoryRootPath: selectedRepositoryRootPathOverride ?? _workspaceState.SelectedRepositoryRootPath,
            SelectedRepositoryFamilyKey: _workspaceState.SelectedRepositoryFamilyKey,
            PortfolioOverview: PortfolioOverview,
            RepositoryFamily: RepositoryFamily,
            ReleaseSignals: ReleaseSignals,
            Stations: Stations,
            Repositories: Repositories,
            StationSnapshots: _workspaceState.StationSnapshots,
            ResolvePortfolioPreset: ResolvePortfolioPreset,
            ReleaseInboxItemsOverride: releaseInboxItemsOverride));

        _workspaceState.ApplyProjectionResult(projection);
        RefreshWorkspaceExplorer();
        RaisePropertyChanged(nameof(SelectedRepository));
        ApplyRepositoryDetail();
        RaiseCommandStates();
    }

    private void ApplySavedPortfolioView(RepositoryPortfolioViewState? viewState)
    {
        _isRestoringPortfolioViewState = true;
        try
        {
            var restoredState = _portfolioViewStateService.Restore(
                viewState,
                PortfolioOverview.FocusModes,
                ResolvePortfolioPreset);
            PortfolioOverview.SelectedFocus = restoredState.SelectedFocus;
            PortfolioOverview.SearchText = restoredState.SearchText;
            _workspaceState.RestoreSavedPortfolioView(viewState);
            PortfolioOverview.SelectedPreset = restoredState.SelectedPreset;
            PortfolioOverview.ViewMemory = restoredState.ViewMemory;
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

        var state = _portfolioViewStateService.CreateState(PortfolioOverview, _workspaceState.SelectedRepositoryFamilyKey);
        _portfolioViewStateSaveScheduler.Schedule(DatabasePath, state);
    }

    private async Task SavePortfolioViewAsync()
    {
        if (!_isInitialized)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var displayName = _portfolioViewStateService.BuildSuggestedDisplayName(PortfolioOverview);
            var viewId = _portfolioViewStateService.CreateSavedViewId(displayName);
            var state = _portfolioViewStateService.CreateState(PortfolioOverview, _workspaceState.SelectedRepositoryFamilyKey);
            await _portfolioViewStateSaveScheduler.FlushAsync().ConfigureAwait(true);
            await _portfolioViewStateSaveScheduler.PersistenceService.PersistAsync(
                DatabasePath,
                state,
                viewId,
                displayName).ConfigureAwait(true);

            await ReloadSavedPortfolioViewsAsync(viewId).ConfigureAwait(true);
            PortfolioOverview.SavedViewDraftName = displayName;
            PortfolioOverview.ViewMemory = $"Saved named view '{displayName}' for this workspace.";
            StatusText = $"Saved portfolio view '{displayName}'.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplySelectedSavedPortfolioView()
    {
        var savedView = PortfolioOverview.SelectedSavedView;
        if (savedView is null)
        {
            return;
        }

        PortfolioOverview.SavedViewDraftName = savedView.DisplayName;
        ApplySavedPortfolioView(savedView.State);
        ApplyCurrentProjection();
        SchedulePortfolioViewStateSave();
        PortfolioOverview.ViewMemory = $"Loaded saved view '{savedView.DisplayName}'.";
        StatusText = $"Applied saved portfolio view '{savedView.DisplayName}'.";
        RaiseCommandStates();
    }

    private bool ApplySavedViewFromProfile(WorkspaceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (string.IsNullOrWhiteSpace(profile.SavedViewId))
        {
            StatusText = $"Workspace profile '{profile.DisplayName}' does not define a pinned saved view yet.";
            return false;
        }

        var savedView = PortfolioOverview.SavedViews.FirstOrDefault(view =>
            string.Equals(view.ViewId, profile.SavedViewId, StringComparison.OrdinalIgnoreCase));
        if (savedView is null)
        {
            StatusText = $"Saved view '{profile.SavedViewId}' is not available for workspace profile '{profile.DisplayName}'.";
            return false;
        }

        PortfolioOverview.SelectedSavedView = savedView;
        ApplySelectedSavedPortfolioView();
        return true;
    }

    private async Task DeleteSelectedSavedPortfolioViewAsync()
    {
        var savedView = PortfolioOverview.SelectedSavedView;
        if (savedView is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _portfolioViewStateSaveScheduler.FlushAsync().ConfigureAwait(true);
            await _portfolioViewStateSaveScheduler.PersistenceService.DeleteAsync(DatabasePath, savedView.ViewId).ConfigureAwait(true);
            await ReloadSavedPortfolioViewsAsync().ConfigureAwait(true);
            PortfolioOverview.SavedViewDraftName = string.Empty;
            PortfolioOverview.ViewMemory = $"Deleted saved view '{savedView.DisplayName}'.";
            StatusText = $"Deleted portfolio view '{savedView.DisplayName}'.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveWorkspaceProfileAsync()
    {
        if (!_isInitialized)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var displayName = ResolveWorkspaceProfileDisplayName();
            var profileId = _portfolioViewStateService.CreateSavedViewId(displayName);
            var existingProfile = PortfolioOverview.WorkspaceProfiles.FirstOrDefault(existing =>
                string.Equals(existing.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));
            var profile = new WorkspaceProfile(
                ProfileId: profileId,
                DisplayName: displayName,
                Description: ResolveWorkspaceProfileDescription(),
                TodayNote: ResolveWorkspaceProfileTodayNote(),
                LastLaunchResult: existingProfile?.LastLaunchResult,
                LaunchHistory: existingProfile?.LaunchHistory ?? [],
                WorkspaceRoot: WorkspaceRoot,
                SavedViewId: PortfolioOverview.SelectedSavedView?.ViewId,
                QueueScopeKey: _workspaceState.SelectedRepositoryFamilyKey,
                QueueScopeDisplayName: ResolveSelectedFamily()?.DisplayName,
                PreferredActionKinds: ResolveWorkspaceProfilePreferredActionKinds(),
                PreferredStartupFocusMode: ResolveWorkspaceProfilePreferredStartupFocusMode(),
                PreferredStartupSearchText: ResolveWorkspaceProfilePreferredStartupSearchText(),
                PreferredStartupFamilyKey: ResolveWorkspaceProfilePreferredStartupFamilyKey(),
                PreferredStartupFamilyDisplayName: ResolveWorkspaceProfilePreferredStartupFamilyDisplayName(),
                ApplyStartupPreferenceAfterSavedView: ResolveWorkspaceProfileApplyStartupPreferenceAfterSavedView(),
                UpdatedAtUtc: DateTimeOffset.UtcNow);

            ApplyWorkspaceRootCatalog(_workspaceRootCatalogService.SaveProfile(profile, profile.ProfileId));
            PortfolioOverview.WorkspaceProfileDraftName = displayName;
            PortfolioOverview.ViewMemory = $"Saved workspace profile '{displayName}'.";
            StatusText = $"Saved workspace profile '{displayName}'.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyWorkspaceProfileCard(WorkspaceProfileHeroCard? card)
    {
        if (card is null)
        {
            return;
        }

        var profile = PortfolioOverview.WorkspaceProfiles.FirstOrDefault(existing =>
            string.Equals(existing.ProfileId, card.ProfileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            return;
        }

        PortfolioOverview.SelectedWorkspaceProfile = profile;
        _ = ApplyWorkspaceProfileCardRouteAsync(profile, card);
    }

    private async Task ApplyWorkspaceProfileCardRouteAsync(WorkspaceProfile profile, WorkspaceProfileHeroCard card)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(card);

        await ApplySelectedWorkspaceProfileAsync().ConfigureAwait(true);
        switch (card.RouteKind)
        {
            case WorkspaceProfileHeroRouteKind.ResumeDesk:
                ApplyResumeDeskContext(profile);
                break;
            case WorkspaceProfileHeroRouteKind.RefreshDesk:
                await RefreshAsync(forceRefresh: true).ConfigureAwait(true);
                var refreshedSummary = $"Refreshed workspace profile '{profile.DisplayName}' and restored its release context.";
                PersistWorkspaceProfileLaunchResult(
                    ResolveActiveWorkspaceProfile() ?? profile,
                    CreateRefreshWorkspaceLaunchAction(profile),
                    true,
                    refreshedSummary);
                break;
        }
    }

    private void ApplyResumeDeskContext(WorkspaceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var statusParts = new List<string>();
        var attentionPreset = ResolvePortfolioPreset("attention", RepositoryPortfolioFocusMode.Attention, string.Empty);
        if (attentionPreset is not null)
        {
            ApplyPortfolioPreset(attentionPreset);
            statusParts.Add("attention focus");
        }

        if (!string.IsNullOrWhiteSpace(profile.QueueScopeKey))
        {
            var family = ResolveFamilyByKey(profile.QueueScopeKey);
            if (family is not null)
            {
                ApplyRepositoryFamily(family);
            }
            else
            {
                _workspaceState.SetSelectedRepositoryFamilyKey(profile.QueueScopeKey);
                ApplyCurrentProjection();
            }

            statusParts.Add($"queue scope '{profile.QueueScopeDisplayName ?? profile.QueueScopeKey}'");
        }

        StatusText = statusParts.Count == 0
            ? $"Resumed workspace profile '{profile.DisplayName}'."
            : $"Resumed workspace profile '{profile.DisplayName}' with {string.Join(" and ", statusParts)}.";
    }

    private void PersistWorkspaceProfileLaunchResult(
        WorkspaceProfile profile,
        WorkspaceProfileLaunchAction action,
        bool succeeded,
        string summary)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(action);

        var normalizedSummary = string.IsNullOrWhiteSpace(summary)
            ? action.Title
            : summary.Trim();
        var updatedProfile = profile with {
            LastLaunchResult = new WorkspaceProfileLaunchResult(
                action.Kind,
                action.Title,
                succeeded,
                normalizedSummary,
                DateTimeOffset.UtcNow),
            LaunchHistory = BuildLaunchHistory(profile, action, succeeded, normalizedSummary),
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        ApplyWorkspaceRootCatalog(_workspaceRootCatalogService.SaveProfile(updatedProfile, updatedProfile.ProfileId));
        PortfolioOverview.SelectedWorkspaceProfile = PortfolioOverview.WorkspaceProfiles.FirstOrDefault(existing =>
            string.Equals(existing.ProfileId, updatedProfile.ProfileId, StringComparison.OrdinalIgnoreCase));
    }

    private void ExecuteWorkspaceProfileLaunchAction(WorkspaceProfileLaunchAction? action)
    {
        if (action is null)
        {
            return;
        }

        _ = ExecuteWorkspaceProfileLaunchActionAsync(action);
    }

    private async Task ExecuteWorkspaceProfileLaunchActionAsync(WorkspaceProfileLaunchAction action)
    {
        var profile = ResolveActiveWorkspaceProfile();
        if (profile is null || !string.Equals(profile.ProfileId, action.ProfileId, StringComparison.OrdinalIgnoreCase))
        {
            StatusText = "Apply a workspace profile before using its launch board actions.";
            return;
        }

        switch (action.Kind)
        {
            case WorkspaceProfileLaunchActionKind.RefreshWorkspace:
                await RefreshAsync(forceRefresh: true).ConfigureAwait(true);
                var refreshedWorkspaceSummary = string.IsNullOrWhiteSpace(StatusText)
                    ? $"Refreshed workspace profile '{profile.DisplayName}'."
                    : $"Refreshed workspace profile '{profile.DisplayName}'. {StatusText}";
                PersistWorkspaceProfileLaunchResult(ResolveActiveWorkspaceProfile() ?? profile, action, true, refreshedWorkspaceSummary);
                break;
            case WorkspaceProfileLaunchActionKind.ApplySavedView:
                var appliedSavedView = ApplySavedViewFromProfile(profile);
                PersistWorkspaceProfileLaunchResult(profile, action, appliedSavedView, StatusText);
                break;
            case WorkspaceProfileLaunchActionKind.PrepareQueue:
                PortfolioOverview.SelectedWorkspaceProfile = profile;
                var preparedQueue = await PrepareSelectedWorkspaceProfileQueueCoreAsync(profile).ConfigureAwait(true);
                PersistWorkspaceProfileLaunchResult(profile, action, preparedQueue, StatusText);
                break;
            case WorkspaceProfileLaunchActionKind.OpenAttentionView:
                var openedAttentionView = OpenAttentionViewForProfile(profile);
                PersistWorkspaceProfileLaunchResult(profile, action, openedAttentionView, StatusText);
                break;
            case WorkspaceProfileLaunchActionKind.RetryFailedFamily:
                PortfolioOverview.SelectedWorkspaceProfile = profile;
                var retriedFailedFamily = await RetrySelectedWorkspaceProfileFailedCoreAsync(profile).ConfigureAwait(true);
                PersistWorkspaceProfileLaunchResult(profile, action, retriedFailedFamily, StatusText);
                break;
            default:
                StatusText = $"Launch action '{action.Title}' is not supported yet.";
                break;
        }
    }

    private async Task ApplySelectedWorkspaceProfileAsync()
    {
        var profile = PortfolioOverview.SelectedWorkspaceProfile;
        if (profile is null)
        {
            return;
        }

        var switchingRoots = !string.Equals(profile.WorkspaceRoot, WorkspaceRoot, StringComparison.OrdinalIgnoreCase);
        _activeWorkspaceProfileId = profile.ProfileId;

        if (switchingRoots)
        {
            WorkspaceRoot = profile.WorkspaceRoot;
        }

        ApplyWorkspaceRootCatalog(_workspaceRootCatalogService.SaveActive(profile.WorkspaceRoot, profile.ProfileId));

        if (switchingRoots)
        {
            await RefreshAsync(forceRefresh: true).ConfigureAwait(true);
            return;
        }

        var profileStatus = ApplyActiveWorkspaceProfileContext();
        StatusText = profileStatus ?? $"Applied workspace profile '{profile.DisplayName}'.";
    }

    private async Task DeleteSelectedWorkspaceProfileAsync()
    {
        var profile = PortfolioOverview.SelectedWorkspaceProfile;
        if (profile is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var nextActiveProfileId = string.Equals(_activeWorkspaceProfileId, profile.ProfileId, StringComparison.OrdinalIgnoreCase)
                ? null
                : _activeWorkspaceProfileId;
            ApplyWorkspaceRootCatalog(_workspaceRootCatalogService.DeleteProfile(profile.ProfileId, WorkspaceRoot, nextActiveProfileId));
            PortfolioOverview.WorkspaceProfileDraftName = string.Empty;
            PortfolioOverview.WorkspaceProfileDraftDescription = string.Empty;
            PortfolioOverview.WorkspaceProfileDraftTodayNote = string.Empty;
            PortfolioOverview.ViewMemory = $"Deleted workspace profile '{profile.DisplayName}'.";
            StatusText = $"Deleted workspace profile '{profile.DisplayName}'.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PrepareSelectedWorkspaceProfileQueueAsync()
        => await PrepareSelectedWorkspaceProfileQueueCoreAsync().ConfigureAwait(true);

    private async Task<bool> PrepareSelectedWorkspaceProfileQueueCoreAsync(WorkspaceProfile? explicitProfile = null)
    {
        var profile = explicitProfile ?? PortfolioOverview.SelectedWorkspaceProfile;
        if (profile is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(profile.QueueScopeKey))
        {
            StatusText = $"Workspace profile '{profile.DisplayName}' does not define a preferred family queue scope yet.";
            return false;
        }

        IsBusy = true;
        try
        {
            await EnsureWorkspaceProfileContextAsync(profile).ConfigureAwait(true);

            var family = ResolveFamilyByKey(profile.QueueScopeKey);
            var result = await _familyQueueActionService.PrepareQueueAsync(
                DatabasePath,
                WorkspaceRoot,
                _workspaceState.PortfolioSnapshot,
                family).ConfigureAwait(true);
            ApplyFamilyQueueActionResult(result);
            return result.CommandResult is not null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> RetrySelectedWorkspaceProfileFailedCoreAsync(WorkspaceProfile? explicitProfile = null)
    {
        var profile = explicitProfile ?? PortfolioOverview.SelectedWorkspaceProfile;
        if (profile is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(profile.QueueScopeKey))
        {
            StatusText = $"Workspace profile '{profile.DisplayName}' does not define a preferred family queue scope yet.";
            return false;
        }

        IsBusy = true;
        try
        {
            await EnsureWorkspaceProfileContextAsync(profile).ConfigureAwait(true);

            var family = ResolveFamilyByKey(profile.QueueScopeKey);
            var result = await _familyQueueActionService.RetryFailedAsync(
                DatabasePath,
                _workspaceState.ActiveQueueSession,
                _workspaceState.PortfolioSnapshot,
                family).ConfigureAwait(true);
            ApplyFamilyQueueActionResult(result);
            return result.CommandResult is not null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool OpenAttentionViewForProfile(WorkspaceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var attentionPreset = ResolvePortfolioPreset("attention", RepositoryPortfolioFocusMode.Attention, string.Empty);
        if (attentionPreset is null)
        {
            StatusText = "The attention preset is not available for this workspace.";
            return false;
        }

        ApplyPortfolioPreset(attentionPreset);
        var statusParts = new List<string> { "attention focus" };
        if (!string.IsNullOrWhiteSpace(profile.QueueScopeKey))
        {
            var family = ResolveFamilyByKey(profile.QueueScopeKey);
            if (family is not null)
            {
                ApplyRepositoryFamily(family);
            }
            else
            {
                _workspaceState.SetSelectedRepositoryFamilyKey(profile.QueueScopeKey);
                ApplyCurrentProjection();
            }

            statusParts.Add($"queue scope '{profile.QueueScopeDisplayName ?? profile.QueueScopeKey}'");
        }

        StatusText = $"Opened attention view for workspace profile '{profile.DisplayName}' with {string.Join(" and ", statusParts)}.";
        return true;
    }

    private void ApplyPortfolioPreset(PortfolioQuickPreset? preset)
    {
        ApplyPortfolioInteraction(_portfolioInteractionService.ApplyPreset(PortfolioOverview, preset));
    }

    private void ApplyDashboardCard(PortfolioDashboardCard? card)
    {
        ApplyPortfolioInteraction(_portfolioInteractionService.ApplyDashboardCard(
            PortfolioOverview,
            card,
            ResolvePortfolioPreset));
    }

    private void ApplyRepositoryFamily(RepositoryWorkspaceFamilySnapshot? family)
    {
        ApplyPortfolioInteraction(_portfolioInteractionService.ApplyRepositoryFamily(PortfolioOverview, family));
    }

    private void ApplyRepositoryFamilyLaneItem(RepositoryWorkspaceFamilyLaneItem? item)
    {
        ApplyPortfolioInteraction(_portfolioInteractionService.ApplyFamilyLaneItem(
            PortfolioOverview,
            _workspaceState.PortfolioSnapshot,
            item));
    }

    private void ApplyReleaseInboxItem(RepositoryReleaseInboxItem? item)
    {
        ApplyPortfolioInteraction(_portfolioInteractionService.ApplyReleaseInboxItem(
            PortfolioOverview,
            _workspaceState.PortfolioSnapshot,
            item,
            ResolvePortfolioPreset));
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
        IsBusy = true;
        try
        {
            var result = await _gitQuickActionWorkflowService.ExecuteAsync(
                DatabasePath,
                SelectedRepository,
                action).ConfigureAwait(true);

            if (result.Receipt is not null)
            {
                _workspaceState.ApplyGitQuickActionReceipt(result.Receipt);
                ApplyCurrentProjection();
            }

            StatusText = result.StatusMessage;

            if (result.ShouldRefresh)
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
            var byKey = PortfolioOverview.QuickPresets.FirstOrDefault(preset => string.Equals(preset.Key, presetKey, StringComparison.OrdinalIgnoreCase));
            if (byKey is not null)
            {
                return byKey;
            }
        }

        return PortfolioOverview.QuickPresets.FirstOrDefault(preset =>
            preset.FocusMode == focusMode
            && string.Equals(preset.SearchText, searchText ?? string.Empty, StringComparison.Ordinal));
    }

    private void ApplyBuildEngineCard(PSPublishModuleResolution resolution)
    {
        PortfolioOverview.BuildEngineStatus = resolution.StatusDisplay;
        PortfolioOverview.BuildEngineHeadline = $"{resolution.SourceDisplay} ({resolution.VersionDisplay})";
        PortfolioOverview.BuildEngineDetails = resolution.IsUsable
            ? resolution.ManifestPath
            : $"{resolution.ManifestPath} is the current fallback target, but the expected module files are not present yet.";
        PortfolioOverview.BuildEngineAdvisory = resolution.Warning ?? resolution.Source switch
        {
            PSPublishModuleResolutionSource.EnvironmentOverride => "Environment override is active, so the shell will prefer that engine until the variable is removed or changed.",
            PSPublishModuleResolutionSource.RepositoryManifest => "The local PSPublishModule repo manifest is active, which is the safest path when iterating on unpublished engine changes.",
            PSPublishModuleResolutionSource.InstalledModule => "No immediate compatibility warning was detected, but this is still coming from the installed module cache.",
            _ => "No immediate engine compatibility warning was detected."
        };
    }

    private void OnPortfolioOverviewPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PortfolioOverviewViewModel.SelectedFocus) or nameof(PortfolioOverviewViewModel.SearchText))
        {
            if (_isRestoringPortfolioViewState)
            {
                return;
            }

            ApplyCurrentProjection();
            SchedulePortfolioViewStateSave();
        }

        if (e.PropertyName is nameof(PortfolioOverviewViewModel.SelectedSavedView) or nameof(PortfolioOverviewViewModel.SavedViewDraftName))
        {
            RaiseCommandStates();
        }

        if (e.PropertyName is nameof(PortfolioOverviewViewModel.SelectedWorkspaceProfile)
            or nameof(PortfolioOverviewViewModel.SelectedWorkspaceProfileTemplate)
            or nameof(PortfolioOverviewViewModel.WorkspaceProfileDraftName))
        {
            if (e.PropertyName == nameof(PortfolioOverviewViewModel.SelectedWorkspaceProfile))
            {
                ApplyWorkspaceProfileDraft(PortfolioOverview.SelectedWorkspaceProfile);
            }

            RaiseCommandStates();
        }
    }

    private void ApplyRepositoryDetail()
    {
        var quickActionReceipt = SelectedRepository is null
            ? null
            : _workspaceState.GitQuickActionReceiptLookup.GetValueOrDefault(SelectedRepository.RootPath);
        var snapshot = _repositoryDetailService.CreateDetail(SelectedRepository, _workspaceState.ActiveQueueSession, _workspaceState.BuildEngineResolution, quickActionReceipt);
        RepositoryDetail.ApplySnapshot(snapshot);
    }

    private void ApplyWorkspaceExplorerNode(WorkspaceExplorerNodeViewModel? node)
    {
        if (node is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(node.RootPath))
        {
            SelectRepositoryByRootPath(node.RootPath);
            return;
        }

        if (string.IsNullOrWhiteSpace(node.FamilyKey))
        {
            return;
        }

        var family = RepositoryFamily.Families.FirstOrDefault(candidate =>
            string.Equals(candidate.FamilyKey, node.FamilyKey, StringComparison.OrdinalIgnoreCase));
        if (family is not null)
        {
            ApplyRepositoryFamily(family);
        }
    }

    private void ApplyQueueItem(ReleaseQueueItem? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedStageIndex = 0;
        SelectRepositoryByRootPath(item.RootPath);
    }

    private void ApplySigningArtifact(ReleaseSigningArtifact? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedStageIndex = 1;
        SelectRepositoryByName(item.RepositoryName);
    }

    private void ApplyPublishTarget(ReleasePublishTarget? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedStageIndex = 2;
        SelectRepositoryByRootPath(item.RootPath);
    }

    private void ApplyVerificationTarget(ReleaseVerificationTarget? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedStageIndex = 3;
        SelectRepositoryByRootPath(item.RootPath);
    }

    private void SelectRepositoryByRootPath(string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return;
        }

        var repository = _workspaceState.PortfolioSnapshot.FirstOrDefault(candidate =>
            string.Equals(candidate.RootPath, rootPath, StringComparison.OrdinalIgnoreCase));
        if (repository is null)
        {
            return;
        }

        _workspaceState.SetSelectedRepositoryFamilyKey(repository.FamilyKey);
        SelectedRepository = repository;
        StatusText = $"Inspecting repository '{repository.Name}'.";
    }

    private void SelectRepositoryByName(string? repositoryName)
    {
        if (string.IsNullOrWhiteSpace(repositoryName))
        {
            return;
        }

        var repository = _workspaceState.PortfolioSnapshot.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, repositoryName, StringComparison.OrdinalIgnoreCase));
        if (repository is null)
        {
            return;
        }

        SelectRepositoryByRootPath(repository.RootPath);
    }

    private void RefreshWorkspaceExplorer()
    {
        WorkspaceExplorerNodes.Clear();

        var groupedRepositories = Repositories
            .GroupBy(repository => repository.FamilyKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        foreach (var family in RepositoryFamily.Families)
        {
            groupedRepositories.TryGetValue(family.FamilyKey, out var familyRepositories);
            familyRepositories ??= [];

            var familyNode = new WorkspaceExplorerNodeViewModel(
                displayName: family.DisplayName,
                badge: family.CountDisplay,
                summary: family.StatusDisplay,
                detail: family.MemberSummary,
                familyKey: family.FamilyKey,
                rootPath: null,
                isFamily: true)
            {
                IsActive = string.Equals(_workspaceState.SelectedRepositoryFamilyKey, family.FamilyKey, StringComparison.OrdinalIgnoreCase),
                IsExpanded = true
            };

            foreach (var repository in familyRepositories
                         .OrderBy(GetExplorerPriority)
                         .ThenBy(repository => repository.Name, StringComparer.OrdinalIgnoreCase))
            {
                familyNode.Children.Add(new WorkspaceExplorerNodeViewModel(
                    displayName: repository.Name,
                    badge: repository.ReadinessKind.ToString(),
                    summary: $"Plan {repository.PlanStatus} | GitHub {repository.GitHubStatus}",
                    detail: repository.ReadinessReason,
                    familyKey: repository.FamilyKey,
                    rootPath: repository.RootPath,
                    isFamily: false)
                {
                    IsActive = string.Equals(_workspaceState.SelectedRepositoryRootPath, repository.RootPath, StringComparison.OrdinalIgnoreCase)
                });
            }

            WorkspaceExplorerNodes.Add(familyNode);
        }
    }

    private static int GetExplorerPriority(RepositoryPortfolioItem repository)
        => repository.ReadinessKind switch
        {
            RepositoryReadinessKind.Attention => 0,
            RepositoryReadinessKind.Ready => 1,
            _ => 2
        };

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

        if (SavePortfolioViewCommand is AsyncDelegateCommand saveView)
        {
            saveView.RaiseCanExecuteChanged();
        }

        if (DeleteSavedPortfolioViewCommand is AsyncDelegateCommand deleteView)
        {
            deleteView.RaiseCanExecuteChanged();
        }

        if (ApplySavedPortfolioViewCommand is DelegateCommand<object> applySavedView)
        {
            applySavedView.RaiseCanExecuteChanged();
        }

        if (SaveWorkspaceProfileTemplateCommand is AsyncDelegateCommand saveWorkspaceProfileTemplate)
        {
            saveWorkspaceProfileTemplate.RaiseCanExecuteChanged();
        }

        if (ApplyWorkspaceProfileTemplateCommand is DelegateCommand<object> applyWorkspaceProfileTemplate)
        {
            applyWorkspaceProfileTemplate.RaiseCanExecuteChanged();
        }

        if (CreateWorkspaceProfileFromTemplateCommand is AsyncDelegateCommand createWorkspaceProfileFromTemplate)
        {
            createWorkspaceProfileFromTemplate.RaiseCanExecuteChanged();
        }

        if (DeleteWorkspaceProfileTemplateCommand is AsyncDelegateCommand deleteWorkspaceProfileTemplate)
        {
            deleteWorkspaceProfileTemplate.RaiseCanExecuteChanged();
        }

        if (SaveWorkspaceProfileCommand is AsyncDelegateCommand saveWorkspaceProfile)
        {
            saveWorkspaceProfile.RaiseCanExecuteChanged();
        }

        if (ApplyWorkspaceProfileCommand is AsyncDelegateCommand applyWorkspaceProfile)
        {
            applyWorkspaceProfile.RaiseCanExecuteChanged();
        }

        if (DeleteWorkspaceProfileCommand is AsyncDelegateCommand deleteWorkspaceProfile)
        {
            deleteWorkspaceProfile.RaiseCanExecuteChanged();
        }

        if (PrepareWorkspaceProfileQueueCommand is AsyncDelegateCommand prepareWorkspaceProfileQueue)
        {
            prepareWorkspaceProfileQueue.RaiseCanExecuteChanged();
        }

        if (ApplyWorkspaceProfileLaunchActionCommand is DelegateCommand<WorkspaceProfileLaunchAction> applyWorkspaceProfileLaunchAction)
        {
            applyWorkspaceProfileLaunchAction.RaiseCanExecuteChanged();
        }
    }

    private void ApplyWorkspaceRootCatalog(WorkspaceRootCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _activeWorkspaceProfileId = catalog.ActiveProfileId;

        var activeRoot = NormalizeWorkspaceRootInput(catalog.ActiveWorkspaceRoot);
        if (!string.Equals(_workspaceRoot, activeRoot, StringComparison.Ordinal))
        {
            _workspaceRoot = activeRoot;
            RaisePropertyChanged(nameof(WorkspaceRoot));
        }

        var databasePath = ResolveDatabasePath(activeRoot);
        if (!string.Equals(_databasePath, databasePath, StringComparison.Ordinal))
        {
            _databasePath = databasePath;
            RaisePropertyChanged(nameof(DatabasePath));
        }

        RecentWorkspaceRoots.Clear();
        foreach (var recentRoot in catalog.RecentWorkspaceRoots.Select(NormalizeWorkspaceRootInput))
        {
            if (!RecentWorkspaceRoots.Contains(recentRoot, StringComparer.OrdinalIgnoreCase))
            {
                RecentWorkspaceRoots.Add(recentRoot);
            }
        }

        PortfolioOverview.WorkspaceProfileTemplates.Clear();
        foreach (var template in WorkspaceProfileTemplateCatalog.CreateDefaultTemplates()
                     .Concat(catalog.Templates ?? [])
                     .GroupBy(template => template.TemplateId, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First())
                     .OrderBy(template => !template.IsBuiltIn)
                     .ThenBy(template => template.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            PortfolioOverview.WorkspaceProfileTemplates.Add(template);
        }

        PortfolioOverview.WorkspaceProfiles.Clear();
        foreach (var profile in catalog.Profiles)
        {
            PortfolioOverview.WorkspaceProfiles.Add(profile);
        }

        PortfolioOverview.SelectedWorkspaceProfile = PortfolioOverview.WorkspaceProfiles.FirstOrDefault(profile =>
            string.Equals(profile.ProfileId, catalog.ActiveProfileId, StringComparison.OrdinalIgnoreCase));
        PortfolioOverview.WorkspaceProfilesHeadline = PortfolioOverview.WorkspaceProfiles.Count == 0
            ? "No workspace profiles yet."
            : $"{PortfolioOverview.WorkspaceProfiles.Count} workspace profile(s) saved.";
        PortfolioOverview.WorkspaceProfilesDetails = PortfolioOverview.WorkspaceProfiles.Count == 0
            ? "Save a profile to bind together a workspace root, saved view, and preferred family queue scope."
            : "Profiles let you jump back to a specific root with its preferred saved view and queue family.";
        PortfolioOverview.SelectedWorkspaceProfileTemplate = PortfolioOverview.WorkspaceProfileTemplates.FirstOrDefault(existing =>
            string.Equals(existing.TemplateId, PortfolioOverview.SelectedWorkspaceProfileTemplate?.TemplateId, StringComparison.OrdinalIgnoreCase));
        UpdateWorkspaceProfileCards();
        UpdateActiveWorkspaceContext();
    }

    private void HandleWorkspaceRootChanged()
    {
        _isInitialized = false;
        _activeWorkspaceProfileId = null;
        _workspaceState.ResetWorkspaceContext();
        ApplyCurrentProjection();
        ClearSavedPortfolioViews();
        ClearWorkspaceProfilesSelection();

        StatusText = string.IsNullOrWhiteSpace(WorkspaceRoot)
            ? "Enter a workspace root to scan."
            : $"Workspace root updated to {WorkspaceRoot}. Run Prepare Queue to scan this root.";
        UpdateActiveWorkspaceContext();
        RaiseCommandStates();
    }

    private bool TryValidateWorkspaceRoot(out string normalizedWorkspaceRoot)
    {
        normalizedWorkspaceRoot = NormalizeWorkspaceRootInput(WorkspaceRoot);
        if (string.IsNullOrWhiteSpace(normalizedWorkspaceRoot))
        {
            StatusText = "Enter a workspace root to scan.";
            return false;
        }

        if (!Directory.Exists(normalizedWorkspaceRoot))
        {
            StatusText = $"Workspace root not found: {normalizedWorkspaceRoot}.";
            return false;
        }

        return true;
    }

    private bool CanPrepareSelectedFamilyQueue()
        => _familyQueueActionService.CanPrepare(IsBusy, ResolveSelectedFamily());

    private bool CanRetrySelectedFamilyFailed()
        => _familyQueueActionService.CanRetryFailed(IsBusy, _workspaceState.ActiveQueueSession, _workspaceState.PortfolioSnapshot, ResolveSelectedFamily());

    private RepositoryWorkspaceFamilySnapshot? ResolveSelectedFamily()
    {
        var familyKey = _workspaceState.SelectedRepositoryFamilyKey ?? SelectedRepository?.FamilyKey;
        if (string.IsNullOrWhiteSpace(familyKey))
        {
            return null;
        }

        return RepositoryFamily.Families.FirstOrDefault(family =>
            !string.IsNullOrWhiteSpace(family.FamilyKey)
            && string.Equals(family.FamilyKey, familyKey, StringComparison.OrdinalIgnoreCase))
            ?? _workspaceFamilyService.BuildFamilies(_workspaceState.PortfolioSnapshot, _workspaceState.ActiveQueueSession, _workspaceState.GitQuickActionReceiptLookup).FirstOrDefault(family =>
                string.Equals(family.FamilyKey, familyKey, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyPortfolioInteraction(PortfolioInteractionResult result)
    {
        if (!result.Handled)
        {
            return;
        }

        _workspaceState.ApplyPortfolioInteraction(result);
        ApplyCurrentProjection(selectedRepositoryRootPathOverride: result.SelectedRepositoryRootPath);

        if (result.ShouldScheduleSave)
        {
            SchedulePortfolioViewStateSave();
        }
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

    private static string NormalizeWorkspaceRootInput(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return PowerForgeStudioHostPaths.NormalizeWorkspaceRoot(value);
    }

    private static string ResolveDatabasePath(string workspaceRoot)
        => string.IsNullOrWhiteSpace(workspaceRoot)
            ? ReleaseStateDatabase.GetDefaultDatabasePath()
            : PowerForgeStudioHostPaths.GetWorkspaceDatabasePath(workspaceRoot);

    private async Task ReloadSavedPortfolioViewsAsync(string? selectedViewId = null)
    {
        var savedViews = await _portfolioViewStateSaveScheduler.PersistenceService.ListSavedViewsAsync(DatabasePath).ConfigureAwait(true);
        var previousViewId = selectedViewId ?? PortfolioOverview.SelectedSavedView?.ViewId;
        PortfolioOverview.SavedViews.Clear();

        foreach (var savedView in savedViews.Select(view => _portfolioViewStateService.CreateSavedViewItem(view, PortfolioOverview.FocusModes)))
        {
            PortfolioOverview.SavedViews.Add(savedView);
        }

        PortfolioOverview.SelectedSavedView = PortfolioOverview.SavedViews.FirstOrDefault(view =>
            string.Equals(view.ViewId, previousViewId, StringComparison.OrdinalIgnoreCase));
        PortfolioOverview.SavedViewsHeadline = PortfolioOverview.SavedViews.Count == 0
            ? "No saved views yet."
            : $"{PortfolioOverview.SavedViews.Count} saved view(s) for this workspace.";
        PortfolioOverview.SavedViewsDetails = PortfolioOverview.SavedViews.Count == 0
            ? "Save the current focus, search, and family filter as a reusable view."
            : "Saved views capture focus mode, search text, and family selection for this workspace root.";
        UpdateWorkspaceProfileCards();
        UpdateActiveWorkspaceContext();
        RaiseCommandStates();
    }

    private void ClearSavedPortfolioViews()
    {
        PortfolioOverview.SavedViews.Clear();
        PortfolioOverview.SelectedSavedView = null;
        PortfolioOverview.SavedViewDraftName = string.Empty;
        PortfolioOverview.SavedViewsHeadline = "No saved views loaded yet.";
        PortfolioOverview.SavedViewsDetails = "Named portfolio views are stored per workspace root.";
        UpdateWorkspaceProfileCards();
        UpdateActiveWorkspaceContext();
    }

    private void ClearWorkspaceProfilesSelection()
    {
        PortfolioOverview.SelectedWorkspaceProfile = null;
        PortfolioOverview.SelectedWorkspaceProfileTemplate = null;
        PortfolioOverview.WorkspaceProfileDraftName = string.Empty;
        PortfolioOverview.WorkspaceProfileDraftDescription = string.Empty;
        PortfolioOverview.WorkspaceProfileDraftTodayNote = string.Empty;
        PortfolioOverview.WorkspaceProfileDraftActionChain = string.Empty;
        PortfolioOverview.WorkspaceProfileDraftStartupFocus = string.Empty;
        PortfolioOverview.WorkspaceProfileDraftStartupSearch = string.Empty;
        PortfolioOverview.WorkspaceProfileDraftStartupFamily = string.Empty;
        PortfolioOverview.WorkspaceProfileDraftApplyStartupPreferenceAfterSavedView = false;
        UpdateWorkspaceProfileCards();
    }

    private void ApplySelectedWorkspaceProfileTemplate()
    {
        var template = PortfolioOverview.SelectedWorkspaceProfileTemplate;
        if (template is null)
        {
            return;
        }

        var templateFamily = ResolveWorkspaceProfileTemplateFamily(template);
        if (string.IsNullOrWhiteSpace(PortfolioOverview.WorkspaceProfileDraftName))
        {
            PortfolioOverview.WorkspaceProfileDraftName = template.DisplayName;
        }

        PortfolioOverview.WorkspaceProfileDraftDescription = template.Description;
        PortfolioOverview.WorkspaceProfileDraftTodayNote = template.TodayNote;
        PortfolioOverview.WorkspaceProfileDraftActionChain = FormatWorkspaceProfilePreferredActionKinds(template.PreferredActionKinds);
        PortfolioOverview.WorkspaceProfileDraftStartupFocus = template.PreferredStartupFocusMode is null
            ? string.Empty
            : WorkspaceProfileStartupPreferenceFormatting.FormatFocusMode(template.PreferredStartupFocusMode.Value);
        PortfolioOverview.WorkspaceProfileDraftStartupSearch = template.PreferredStartupSearchText ?? string.Empty;
        PortfolioOverview.WorkspaceProfileDraftStartupFamily = templateFamily?.DisplayName ?? template.PreferredStartupFamily ?? string.Empty;
        PortfolioOverview.WorkspaceProfileDraftApplyStartupPreferenceAfterSavedView = template.ApplyStartupPreferenceAfterSavedView;

        if (templateFamily is not null)
        {
            ApplyRepositoryFamily(templateFamily);
            StatusText = $"Applied profile template '{template.DisplayName}' and aligned the desk to family '{templateFamily.DisplayName}'.";
            return;
        }

        StatusText = $"Applied profile template '{template.DisplayName}'.";
    }

    private async Task SaveWorkspaceProfileTemplateAsync()
    {
        if (!_isInitialized)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var displayName = ResolveWorkspaceProfileTemplateDisplayName();
            var template = new WorkspaceProfileTemplate(
                TemplateId: $"template-{_portfolioViewStateService.CreateSavedViewId(displayName)}",
                DisplayName: displayName,
                Summary: ResolveWorkspaceProfileTemplateSummary(),
                Description: ResolveWorkspaceProfileDescription() ?? displayName,
                TodayNote: ResolveWorkspaceProfileTodayNote() ?? "Open the desk and continue the release flow.",
                PreferredActionKinds: ResolveWorkspaceProfilePreferredActionKinds(),
                PreferredStartupFocusMode: ResolveWorkspaceProfilePreferredStartupFocusMode(),
                PreferredStartupSearchText: ResolveWorkspaceProfilePreferredStartupSearchText(),
                PreferredStartupFamily: ResolveWorkspaceProfilePreferredStartupFamilyDisplayName(),
                ApplyStartupPreferenceAfterSavedView: ResolveWorkspaceProfileApplyStartupPreferenceAfterSavedView(),
                PreferCurrentFamilyForQueueScope: ResolveSelectedFamily() is not null);

            ApplyWorkspaceRootCatalog(_workspaceRootCatalogService.SaveTemplate(template, WorkspaceRoot, _activeWorkspaceProfileId));
            PortfolioOverview.SelectedWorkspaceProfileTemplate = PortfolioOverview.WorkspaceProfileTemplates.FirstOrDefault(existing =>
                string.Equals(existing.TemplateId, template.TemplateId, StringComparison.OrdinalIgnoreCase));
            StatusText = $"Saved custom template '{displayName}'.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CreateWorkspaceProfileFromTemplateAsync()
    {
        var template = PortfolioOverview.SelectedWorkspaceProfileTemplate;
        if (template is null || !_isInitialized)
        {
            return;
        }

        ApplySelectedWorkspaceProfileTemplate();
        var templateDisplayName = template.DisplayName;
        await SaveWorkspaceProfileAsync().ConfigureAwait(true);
        var profileDisplayName = PortfolioOverview.SelectedWorkspaceProfile?.DisplayName ?? ResolveWorkspaceProfileDisplayName();
        StatusText = $"Created workspace profile '{profileDisplayName}' from template '{templateDisplayName}'.";
    }

    private async Task DeleteSelectedWorkspaceProfileTemplateAsync()
    {
        var template = PortfolioOverview.SelectedWorkspaceProfileTemplate;
        if (template is null || template.IsBuiltIn)
        {
            return;
        }

        IsBusy = true;
        try
        {
            ApplyWorkspaceRootCatalog(_workspaceRootCatalogService.DeleteTemplate(template.TemplateId, WorkspaceRoot, _activeWorkspaceProfileId));
            PortfolioOverview.SelectedWorkspaceProfileTemplate = null;
            StatusText = $"Deleted custom template '{template.DisplayName}'.";
            await Task.CompletedTask.ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyWorkspaceProfileDraft(WorkspaceProfile? profile)
    {
        if (profile is null)
        {
            return;
        }

        PortfolioOverview.WorkspaceProfileDraftName = profile.DisplayName;
        PortfolioOverview.WorkspaceProfileDraftDescription = profile.Description ?? string.Empty;
        PortfolioOverview.WorkspaceProfileDraftTodayNote = profile.TodayNote ?? string.Empty;
        PortfolioOverview.WorkspaceProfileDraftActionChain = FormatWorkspaceProfilePreferredActionKinds(profile.PreferredActionKinds);
        PortfolioOverview.WorkspaceProfileDraftStartupFocus = profile.PreferredStartupFocusMode is null
            ? string.Empty
            : WorkspaceProfileStartupPreferenceFormatting.FormatFocusMode(profile.PreferredStartupFocusMode.Value);
        PortfolioOverview.WorkspaceProfileDraftStartupSearch = profile.PreferredStartupSearchText ?? string.Empty;
        PortfolioOverview.WorkspaceProfileDraftStartupFamily = profile.PreferredStartupFamilyDisplayName ?? profile.PreferredStartupFamilyKey ?? string.Empty;
        PortfolioOverview.WorkspaceProfileDraftApplyStartupPreferenceAfterSavedView = profile.ApplyStartupPreferenceAfterSavedView;
    }

    private string ResolveWorkspaceProfileDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(PortfolioOverview.WorkspaceProfileDraftName))
        {
            return PortfolioOverview.WorkspaceProfileDraftName.Trim();
        }

        if (PortfolioOverview.SelectedSavedView is not null)
        {
            return PortfolioOverview.SelectedSavedView.DisplayName;
        }

        var workspaceName = Path.GetFileName(WorkspaceRoot);
        return string.IsNullOrWhiteSpace(workspaceName)
            ? "Workspace Profile"
            : workspaceName;
    }

    private string ResolveWorkspaceProfileTemplateDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(PortfolioOverview.WorkspaceProfileDraftName))
        {
            return PortfolioOverview.WorkspaceProfileDraftName.Trim();
        }

        var selectedTemplate = PortfolioOverview.SelectedWorkspaceProfileTemplate;
        if (selectedTemplate is not null && !selectedTemplate.IsBuiltIn)
        {
            return selectedTemplate.DisplayName;
        }

        var workspaceName = Path.GetFileName(WorkspaceRoot);
        return string.IsNullOrWhiteSpace(workspaceName)
            ? "Custom Template"
            : $"{workspaceName} Template";
    }

    private string? ResolveWorkspaceProfileDescription()
    {
        if (!string.IsNullOrWhiteSpace(PortfolioOverview.WorkspaceProfileDraftDescription))
        {
            return PortfolioOverview.WorkspaceProfileDraftDescription.Trim();
        }

        if (PortfolioOverview.SelectedSavedView is not null)
        {
            return $"Pinned to {PortfolioOverview.SelectedSavedView.DisplayName} for {WorkspaceRoot}.";
        }

        return $"Pinned to {WorkspaceRoot}.";
    }

    private string ResolveWorkspaceProfileTemplateSummary()
    {
        if (!string.IsNullOrWhiteSpace(PortfolioOverview.WorkspaceProfileDraftDescription))
        {
            return PortfolioOverview.WorkspaceProfileDraftDescription.Trim();
        }

        var focusLabel = ResolveWorkspaceProfilePreferredStartupFocusMode() is { } focusMode
            ? WorkspaceProfileStartupPreferenceFormatting.FormatFocusMode(focusMode)
            : "custom";
        var familyLabel = ResolveWorkspaceProfilePreferredStartupFamilyDisplayName() ?? ResolveSelectedFamily()?.DisplayName;
        return string.IsNullOrWhiteSpace(familyLabel)
            ? $"{focusLabel} release desk template."
            : $"{focusLabel} release desk for {familyLabel}.";
    }

    private string? ResolveWorkspaceProfileTodayNote()
    {
        if (!string.IsNullOrWhiteSpace(PortfolioOverview.WorkspaceProfileDraftTodayNote))
        {
            return PortfolioOverview.WorkspaceProfileDraftTodayNote.Trim();
        }

        return null;
    }

    private IReadOnlyList<WorkspaceProfileLaunchActionKind>? ResolveWorkspaceProfilePreferredActionKinds()
    {
        return ParseWorkspaceProfilePreferredActionKinds(PortfolioOverview.WorkspaceProfileDraftActionChain);
    }

    private RepositoryPortfolioFocusMode? ResolveWorkspaceProfilePreferredStartupFocusMode()
        => ParseWorkspaceProfileStartupFocusMode(PortfolioOverview.WorkspaceProfileDraftStartupFocus);

    private string? ResolveWorkspaceProfilePreferredStartupSearchText()
        => string.IsNullOrWhiteSpace(PortfolioOverview.WorkspaceProfileDraftStartupSearch)
            ? null
            : PortfolioOverview.WorkspaceProfileDraftStartupSearch.Trim();

    private string? ResolveWorkspaceProfilePreferredStartupFamilyKey()
    {
        if (string.IsNullOrWhiteSpace(PortfolioOverview.WorkspaceProfileDraftStartupFamily))
        {
            return null;
        }

        var draft = PortfolioOverview.WorkspaceProfileDraftStartupFamily.Trim();
        var matchedFamily = ResolveFamilyByDisplayOrKey(draft);
        return matchedFamily?.FamilyKey ?? draft;
    }

    private string? ResolveWorkspaceProfilePreferredStartupFamilyDisplayName()
    {
        if (string.IsNullOrWhiteSpace(PortfolioOverview.WorkspaceProfileDraftStartupFamily))
        {
            return null;
        }

        var draft = PortfolioOverview.WorkspaceProfileDraftStartupFamily.Trim();
        var matchedFamily = ResolveFamilyByDisplayOrKey(draft);
        return matchedFamily?.DisplayName ?? draft;
    }

    private bool ResolveWorkspaceProfileApplyStartupPreferenceAfterSavedView()
        => PortfolioOverview.WorkspaceProfileDraftApplyStartupPreferenceAfterSavedView;

    private bool CanDeleteSelectedWorkspaceProfileTemplate()
        => !IsBusy && PortfolioOverview.SelectedWorkspaceProfileTemplate is { IsBuiltIn: false };

    private RepositoryWorkspaceFamilySnapshot? ResolveWorkspaceProfileTemplateFamily(WorkspaceProfileTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        if (template.PreferCurrentFamilyForQueueScope)
        {
            return ResolveSelectedFamily()
                ?? ResolveFamilyByDisplayOrKey(template.PreferredStartupFamily);
        }

        return ResolveFamilyByDisplayOrKey(template.PreferredStartupFamily);
    }

    private RepositoryWorkspaceFamilySnapshot? ResolveFamilyByDisplayOrKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return RepositoryFamily.Families.FirstOrDefault(family =>
                   string.Equals(family.DisplayName, normalized, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(family.FamilyKey, normalized, StringComparison.OrdinalIgnoreCase))
               ?? _workspaceFamilyService.BuildFamilies(_workspaceState.PortfolioSnapshot, _workspaceState.ActiveQueueSession, _workspaceState.GitQuickActionReceiptLookup)
                   .FirstOrDefault(family =>
                       string.Equals(family.DisplayName, normalized, StringComparison.OrdinalIgnoreCase)
                       || string.Equals(family.FamilyKey, normalized, StringComparison.OrdinalIgnoreCase))
               ?? _workspaceState.PortfolioSnapshot
                   .Where(repository =>
                       string.Equals(repository.FamilyDisplayName, normalized, StringComparison.OrdinalIgnoreCase)
                       || string.Equals(repository.FamilyKey, normalized, StringComparison.OrdinalIgnoreCase))
                   .GroupBy(repository => repository.FamilyKey, StringComparer.OrdinalIgnoreCase)
                   .Select(group => new RepositoryWorkspaceFamilySnapshot(
                       group.Key,
                       group.First().FamilyDisplayName,
                       group.First().RootPath,
                       group.Count(),
                       group.Count(item => item.WorkspaceKind == ReleaseWorkspaceKind.Worktree),
                       group.Count(item => item.ReadinessKind == RepositoryReadinessKind.Attention),
                       group.Count(item => item.ReadinessKind == RepositoryReadinessKind.Ready),
                       0,
                       $"{group.Count()} member(s) resolved from portfolio snapshot."))
                   .FirstOrDefault();
    }

    private static IReadOnlyList<WorkspaceProfileLaunchActionKind>? ParseWorkspaceProfilePreferredActionKinds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (string.Equals(trimmed, "auto", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "automatic", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "default", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var actionKinds = trimmed
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseWorkspaceProfilePreferredActionKind)
            .Where(kind => kind is not null)
            .Cast<WorkspaceProfileLaunchActionKind>()
            .Distinct()
            .ToArray();

        return actionKinds.Length == 0 ? null : actionKinds;
    }

    private static WorkspaceProfileLaunchActionKind? ParseWorkspaceProfilePreferredActionKind(string token)
    {
        var normalized = token.Trim().ToLowerInvariant();
        return normalized switch
        {
            "refresh" or "refresh workspace" => WorkspaceProfileLaunchActionKind.RefreshWorkspace,
            "apply view" or "apply saved view" or "saved view" => WorkspaceProfileLaunchActionKind.ApplySavedView,
            "prepare" or "prepare queue" or "queue" => WorkspaceProfileLaunchActionKind.PrepareQueue,
            "attention" or "open attention" or "open attention view" => WorkspaceProfileLaunchActionKind.OpenAttentionView,
            "retry" or "retry failed" or "retry failed family" => WorkspaceProfileLaunchActionKind.RetryFailedFamily,
            _ => null
        };
    }

    private static RepositoryPortfolioFocusMode? ParseWorkspaceProfileStartupFocusMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "all" => RepositoryPortfolioFocusMode.All,
            "attention" => RepositoryPortfolioFocusMode.Attention,
            "ready" or "ready today" => RepositoryPortfolioFocusMode.Ready,
            "queue active" => RepositoryPortfolioFocusMode.QueueActive,
            "blocked" => RepositoryPortfolioFocusMode.Blocked,
            "usb waiting" or "waiting usb" => RepositoryPortfolioFocusMode.WaitingUsb,
            "publish ready" => RepositoryPortfolioFocusMode.PublishReady,
            "verify ready" => RepositoryPortfolioFocusMode.VerifyReady,
            "failed" => RepositoryPortfolioFocusMode.Failed,
            _ => null
        };
    }

    private static string FormatWorkspaceProfilePreferredActionKinds(IReadOnlyList<WorkspaceProfileLaunchActionKind>? actionKinds)
    {
        if (actionKinds is null || actionKinds.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(", ", actionKinds.Select(WorkspaceProfileActionChainFormatting.FormatKind));
    }

    private string? ApplyActiveWorkspaceProfileContext()
    {
        var profile = PortfolioOverview.SelectedWorkspaceProfile;
        if (profile is null || !string.Equals(profile.WorkspaceRoot, WorkspaceRoot, StringComparison.OrdinalIgnoreCase))
        {
            UpdateActiveWorkspaceContext();
            return null;
        }

        var statusParts = new List<string>();
        var appliedSavedView = false;
        if (!string.IsNullOrWhiteSpace(profile.SavedViewId))
        {
            var savedView = PortfolioOverview.SavedViews.FirstOrDefault(view =>
                string.Equals(view.ViewId, profile.SavedViewId, StringComparison.OrdinalIgnoreCase));
            if (savedView is not null)
            {
                PortfolioOverview.SelectedSavedView = savedView;
                ApplySelectedSavedPortfolioView();
                statusParts.Add($"saved view '{savedView.DisplayName}'");
                appliedSavedView = true;
            }
        }

        if (ApplyProfileStartupPreference(profile, out var startupPreferenceSummary))
        {
            if (!appliedSavedView)
            {
                statusParts.Add(startupPreferenceSummary);
            }
            else if (profile.ApplyStartupPreferenceAfterSavedView)
            {
                statusParts.Add($"startup emphasis ({startupPreferenceSummary})");
            }
            else
            {
                statusParts.Add($"startup fallback ({startupPreferenceSummary})");
            }
        }

        if (!string.IsNullOrWhiteSpace(profile.QueueScopeKey))
        {
            _workspaceState.SetSelectedRepositoryFamilyKey(profile.QueueScopeKey);
            ApplyCurrentProjection();
            statusParts.Add($"queue scope '{profile.QueueScopeDisplayName ?? profile.QueueScopeKey}'");
        }

        UpdateActiveWorkspaceContext();
        return statusParts.Count == 0
            ? $"Applied workspace profile '{profile.DisplayName}'."
            : $"Applied workspace profile '{profile.DisplayName}' with {string.Join(" and ", statusParts)}.";
    }

    private bool ApplyProfileStartupPreference(WorkspaceProfile profile, out string summary)
    {
        ArgumentNullException.ThrowIfNull(profile);

        summary = string.Empty;
        if (profile.PreferredStartupFocusMode is null
            && string.IsNullOrWhiteSpace(profile.PreferredStartupSearchText)
            && string.IsNullOrWhiteSpace(profile.PreferredStartupFamilyKey))
        {
            return false;
        }

        var hasSavedView = !string.IsNullOrWhiteSpace(profile.SavedViewId);
        if (hasSavedView && !profile.ApplyStartupPreferenceAfterSavedView)
        {
            var savedViewAvailable = PortfolioOverview.SavedViews.Any(view =>
                string.Equals(view.ViewId, profile.SavedViewId, StringComparison.OrdinalIgnoreCase));
            if (savedViewAvailable)
            {
                return false;
            }
        }

        _isRestoringPortfolioViewState = true;
        try
        {
            if (profile.PreferredStartupFocusMode is not null)
            {
                PortfolioOverview.SelectedFocus = PortfolioOverview.FocusModes.FirstOrDefault(option =>
                        option.Mode == profile.PreferredStartupFocusMode.Value)
                    ?? PortfolioOverview.FocusModes[0];
            }

            PortfolioOverview.SearchText = profile.PreferredStartupSearchText ?? string.Empty;
            var startupFamily = ResolveFamilyByDisplayOrKey(profile.PreferredStartupFamilyKey ?? profile.PreferredStartupFamilyDisplayName);
            _workspaceState.SetSelectedRepositoryFamilyKey(startupFamily?.FamilyKey ?? profile.PreferredStartupFamilyKey);
            PortfolioOverview.SelectedPreset = ResolvePortfolioPreset(null, PortfolioOverview.SelectedFocus.Mode, PortfolioOverview.SearchText);
            ApplyCurrentProjection();
        }
        finally
        {
            _isRestoringPortfolioViewState = false;
        }

        summary = WorkspaceProfileStartupPreferenceFormatting.FormatDetails(
            profile.PreferredStartupFocusMode,
            profile.PreferredStartupSearchText,
            profile.PreferredStartupFamilyDisplayName ?? profile.PreferredStartupFamilyKey);
        return true;
    }

    private async Task EnsureWorkspaceProfileContextAsync(WorkspaceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var switchingRoots = !string.Equals(profile.WorkspaceRoot, WorkspaceRoot, StringComparison.OrdinalIgnoreCase);
        _activeWorkspaceProfileId = profile.ProfileId;

        if (switchingRoots)
        {
            WorkspaceRoot = profile.WorkspaceRoot;
        }

        ApplyWorkspaceRootCatalog(_workspaceRootCatalogService.SaveActive(profile.WorkspaceRoot, profile.ProfileId));

        if (switchingRoots)
        {
            await RefreshAsync(forceRefresh: true).ConfigureAwait(true);
            return;
        }

        ApplyActiveWorkspaceProfileContext();
    }

    private bool CanPrepareSelectedWorkspaceProfileQueue()
    {
        var profile = PortfolioOverview.SelectedWorkspaceProfile;
        return !IsBusy
            && profile is not null
            && !string.IsNullOrWhiteSpace(profile.QueueScopeKey);
    }

    private RepositoryWorkspaceFamilySnapshot? ResolveFamilyByKey(string? familyKey)
    {
        if (string.IsNullOrWhiteSpace(familyKey))
        {
            return null;
        }

        return RepositoryFamily.Families.FirstOrDefault(family =>
            !string.IsNullOrWhiteSpace(family.FamilyKey)
            && string.Equals(family.FamilyKey, familyKey, StringComparison.OrdinalIgnoreCase))
            ?? _workspaceFamilyService.BuildFamilies(_workspaceState.PortfolioSnapshot, _workspaceState.ActiveQueueSession, _workspaceState.GitQuickActionReceiptLookup).FirstOrDefault(family =>
                string.Equals(family.FamilyKey, familyKey, StringComparison.OrdinalIgnoreCase));
    }

    private WorkspaceProfile? ResolveActiveWorkspaceProfile()
        => PortfolioOverview.WorkspaceProfiles.FirstOrDefault(profile =>
            string.Equals(profile.ProfileId, _activeWorkspaceProfileId, StringComparison.OrdinalIgnoreCase));

    private void UpdateActiveWorkspaceContext()
    {
        var activeProfile = ResolveActiveWorkspaceProfile();
        UpdateWorkspaceProfileCards();
        UpdateActiveWorkspaceLaunchActions(activeProfile);
        UpdateActiveWorkspaceLaunchTimeline(activeProfile);
        if (activeProfile is null)
        {
            var workspaceLabel = string.IsNullOrWhiteSpace(WorkspaceRoot)
                ? "No workspace root selected."
                : WorkspaceRoot;
            PortfolioOverview.ActiveWorkspaceContextHeadline = "Root-only workspace context";
            PortfolioOverview.ActiveWorkspaceContextDetails = $"{workspaceLabel}. Save and apply a workspace profile to pin a saved view and family queue scope for startup.";
            PortfolioOverview.ActiveWorkspaceAgendaHeadline = "Today's agenda";
            PortfolioOverview.ActiveWorkspaceAgendaDetails = "No profile agenda yet. Save a short note or checklist with a workspace profile so startup restores today's release intent.";
            PortfolioOverview.ActiveWorkspaceHealthHeadline = "Desk health unavailable";
            PortfolioOverview.ActiveWorkspaceHealthDetails = "Apply a workspace profile and run launch-board actions to build today's estate health signal.";
            PortfolioOverview.ActiveWorkspaceReceiptHeadline = "No desk receipt yet";
            PortfolioOverview.ActiveWorkspaceReceiptDetails = "Apply a profile route or launch-board action to leave a latest-action receipt for this estate.";
            PortfolioOverview.ActiveWorkspaceReceiptActions.Clear();
            PortfolioOverview.ActiveWorkspaceReceiptAction = null;
            PortfolioOverview.ActiveWorkspaceTimelineHeadline = "Today's activity";
            PortfolioOverview.ActiveWorkspaceTimelineDetails = "No activity yet. Launch-board actions will start building the timeline for this profile.";
            return;
        }

        var savedViewName = string.IsNullOrWhiteSpace(activeProfile.SavedViewId)
            ? "No saved view pinned"
            : PortfolioOverview.SavedViews.FirstOrDefault(view =>
                string.Equals(view.ViewId, activeProfile.SavedViewId, StringComparison.OrdinalIgnoreCase))?.DisplayName
                ?? activeProfile.SavedViewId;
        var queueScopeName = string.IsNullOrWhiteSpace(activeProfile.QueueScopeDisplayName)
            ? activeProfile.QueueScopeKey ?? "No queue scope pinned"
            : activeProfile.QueueScopeDisplayName;
        var startupStrategy = WorkspaceProfileStartupPreferenceFormatting.FormatBehavior(
            hasSavedView: !string.IsNullOrWhiteSpace(activeProfile.SavedViewId),
            applyAfterSavedView: activeProfile.ApplyStartupPreferenceAfterSavedView,
            activeProfile.PreferredStartupFocusMode,
            activeProfile.PreferredStartupSearchText,
            activeProfile.PreferredStartupFamilyDisplayName ?? activeProfile.PreferredStartupFamilyKey);

        PortfolioOverview.ActiveWorkspaceContextHeadline = $"Active profile: {activeProfile.DisplayName}";
        PortfolioOverview.ActiveWorkspaceContextDetails = $"{activeProfile.WorkspaceRoot}. Saved view: {savedViewName}. {startupStrategy}. Queue scope: {queueScopeName}.";
        PortfolioOverview.ActiveWorkspaceAgendaHeadline = $"Today's agenda for {activeProfile.DisplayName}";
        PortfolioOverview.ActiveWorkspaceAgendaDetails = string.IsNullOrWhiteSpace(activeProfile.TodayNote)
            ? "No launch agenda saved yet. Add a short today note to this profile to capture the intended release checklist."
            : activeProfile.TodayNote;
        var (healthLabel, healthDetails) = CalculateWorkspaceProfileHealth(activeProfile);
        PortfolioOverview.ActiveWorkspaceHealthHeadline = $"Desk health: {healthLabel}";
        PortfolioOverview.ActiveWorkspaceHealthDetails = healthDetails;
        var (receiptHeadline, receiptDetails) = FormatWorkspaceProfileReceipt(activeProfile.LastLaunchResult);
        PortfolioOverview.ActiveWorkspaceReceiptHeadline = receiptHeadline;
        PortfolioOverview.ActiveWorkspaceReceiptDetails = receiptDetails;
        var receiptActionChain = ResolveWorkspaceProfileReceiptActionChain(activeProfile);
        PortfolioOverview.ActiveWorkspaceReceiptActions.Clear();
        foreach (var receiptAction in receiptActionChain)
        {
            PortfolioOverview.ActiveWorkspaceReceiptActions.Add(receiptAction);
        }

        PortfolioOverview.ActiveWorkspaceReceiptAction = receiptActionChain.Count > 1
            ? receiptActionChain[^1]
            : null;
    }

    private void UpdateActiveWorkspaceLaunchActions(WorkspaceProfile? activeProfile)
    {
        PortfolioOverview.ActiveWorkspaceLaunchActions.Clear();
        if (activeProfile is null)
        {
            PortfolioOverview.ActiveWorkspaceLaunchBoardHeadline = "Launch board unavailable";
            PortfolioOverview.ActiveWorkspaceLaunchBoardDetails = "Apply a workspace profile to restore saved-view and queue actions for this estate.";
            return;
        }

        var savedViewName = string.IsNullOrWhiteSpace(activeProfile.SavedViewId)
            ? null
            : PortfolioOverview.SavedViews.FirstOrDefault(view =>
                string.Equals(view.ViewId, activeProfile.SavedViewId, StringComparison.OrdinalIgnoreCase))?.DisplayName
                ?? activeProfile.SavedViewId;
        var queueScopeName = string.IsNullOrWhiteSpace(activeProfile.QueueScopeDisplayName)
            ? activeProfile.QueueScopeKey
            : activeProfile.QueueScopeDisplayName;

        PortfolioOverview.ActiveWorkspaceLaunchActions.Add(new WorkspaceProfileLaunchAction(
            ProfileId: activeProfile.ProfileId,
            Kind: WorkspaceProfileLaunchActionKind.RefreshWorkspace,
            Title: "Refresh Workspace",
            Summary: $"Rescan {activeProfile.WorkspaceRoot} and refresh the active release desk.",
            ExecuteLabel: "Refresh",
            IsPrimary: false,
            LastRunLabel: FormatLastLaunchLabel(activeProfile.LastLaunchResult, WorkspaceProfileLaunchActionKind.RefreshWorkspace),
            IsLastRun: activeProfile.LastLaunchResult?.ActionKind == WorkspaceProfileLaunchActionKind.RefreshWorkspace));

        if (!string.IsNullOrWhiteSpace(savedViewName))
        {
            PortfolioOverview.ActiveWorkspaceLaunchActions.Add(new WorkspaceProfileLaunchAction(
                ProfileId: activeProfile.ProfileId,
                Kind: WorkspaceProfileLaunchActionKind.ApplySavedView,
                Title: "Apply Saved View",
                Summary: $"Reload the pinned '{savedViewName}' triage view for this profile.",
                ExecuteLabel: "Apply View",
                IsPrimary: false,
                LastRunLabel: FormatLastLaunchLabel(activeProfile.LastLaunchResult, WorkspaceProfileLaunchActionKind.ApplySavedView),
                IsLastRun: activeProfile.LastLaunchResult?.ActionKind == WorkspaceProfileLaunchActionKind.ApplySavedView));
        }

        if (!string.IsNullOrWhiteSpace(queueScopeName))
        {
            PortfolioOverview.ActiveWorkspaceLaunchActions.Add(new WorkspaceProfileLaunchAction(
                ProfileId: activeProfile.ProfileId,
                Kind: WorkspaceProfileLaunchActionKind.PrepareQueue,
                Title: "Prepare Queue",
                Summary: $"Prepare the pinned '{queueScopeName}' family queue for this workspace profile.",
                ExecuteLabel: "Prepare",
                IsPrimary: true,
                LastRunLabel: FormatLastLaunchLabel(activeProfile.LastLaunchResult, WorkspaceProfileLaunchActionKind.PrepareQueue),
                IsLastRun: activeProfile.LastLaunchResult?.ActionKind == WorkspaceProfileLaunchActionKind.PrepareQueue));
        }

        PortfolioOverview.ActiveWorkspaceLaunchBoardHeadline = $"{PortfolioOverview.ActiveWorkspaceLaunchActions.Count} launch action(s) ready";
        PortfolioOverview.ActiveWorkspaceLaunchBoardDetails = FormatLaunchBoardDetails(activeProfile);
    }

    private static string FormatLaunchBoardDetails(WorkspaceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.LastLaunchResult is null)
        {
            return "Use the launch board to move from restored profile context straight into the next concrete release step.";
        }

        var timestamp = profile.LastLaunchResult.ExecutedAtUtc.ToLocalTime().ToString("g");
        var status = profile.LastLaunchResult.Succeeded ? "Succeeded" : "Needs attention";
        return $"Last run: {profile.LastLaunchResult.ActionTitle} ({status}) at {timestamp}. {profile.LastLaunchResult.Summary}";
    }

    private static (string Headline, string Details) FormatWorkspaceProfileReceipt(WorkspaceProfileLaunchResult? launchResult)
    {
        if (launchResult is null)
        {
            return (
                "No desk receipt yet",
                "Use a hero-card route or launch-board action to stamp the latest desk transition for this profile.");
        }

        var timestamp = launchResult.ExecutedAtUtc.ToLocalTime().ToString("g");
        if (!launchResult.Succeeded)
        {
            return (
                $"Needs attention since {timestamp}",
                launchResult.Summary);
        }

        return launchResult.ActionKind switch
        {
            WorkspaceProfileLaunchActionKind.RefreshWorkspace => ($"Fresh as of {timestamp}", launchResult.Summary),
            WorkspaceProfileLaunchActionKind.ApplySavedView => ($"View restored at {timestamp}", launchResult.Summary),
            WorkspaceProfileLaunchActionKind.PrepareQueue => ($"Queue staged at {timestamp}", launchResult.Summary),
            WorkspaceProfileLaunchActionKind.OpenAttentionView => ($"Attention view opened at {timestamp}", launchResult.Summary),
            WorkspaceProfileLaunchActionKind.RetryFailedFamily => ($"Retry started at {timestamp}", launchResult.Summary),
            _ => ($"Latest action at {timestamp}", launchResult.Summary)
        };
    }

    private WorkspaceProfileLaunchAction? ResolveWorkspaceProfileReceiptAction(WorkspaceProfile activeProfile)
    {
        ArgumentNullException.ThrowIfNull(activeProfile);

        var receiptActionChain = ResolveWorkspaceProfileReceiptActionChain(activeProfile);
        return receiptActionChain.Count > 1
            ? receiptActionChain[^1]
            : null;
    }

    private IReadOnlyList<WorkspaceProfileLaunchAction> ResolveWorkspaceProfileReceiptActionChain(WorkspaceProfile activeProfile)
    {
        ArgumentNullException.ThrowIfNull(activeProfile);

        var receiptAnchor = ResolveWorkspaceProfileReceiptAnchor(activeProfile);
        if (receiptAnchor is null)
        {
            return [];
        }

        var queueScopeName = string.IsNullOrWhiteSpace(activeProfile.QueueScopeDisplayName)
            ? activeProfile.QueueScopeKey
            : activeProfile.QueueScopeDisplayName;
        var family = ResolveFamilyByKey(activeProfile.QueueScopeKey);
        var canRetryFailedFamily = !string.IsNullOrWhiteSpace(activeProfile.QueueScopeKey)
            && _familyQueueActionService.CanRetryFailed(false, _workspaceState.ActiveQueueSession, _workspaceState.PortfolioSnapshot, family);

        var chain = new List<WorkspaceProfileLaunchAction> {
            CreateReceiptAnchorAction(activeProfile.ProfileId, receiptAnchor)
        };

        var hasExplicitPreferredActionKinds = activeProfile.PreferredActionKinds is { Count: > 0 };
        var preferredActionKinds = ResolvePreferredReceiptActionKinds(activeProfile);

        if (!receiptAnchor.Succeeded)
        {
            if (!hasExplicitPreferredActionKinds)
            {
                var retryCompletion = ResolveWorkspaceProfileReceiptCompletion(activeProfile, receiptAnchor, WorkspaceProfileLaunchActionKind.RetryFailedFamily);
                if (canRetryFailedFamily || retryCompletion is not null)
                {
                    var retryAction = new WorkspaceProfileLaunchAction(
                        ProfileId: activeProfile.ProfileId,
                        Kind: WorkspaceProfileLaunchActionKind.RetryFailedFamily,
                        Title: "Retry Failed Family",
                        Summary: $"Retry the failed queue items for the pinned '{queueScopeName}' family.",
                        ExecuteLabel: "Retry",
                        IsPrimary: true);
                    chain.Add(CreateReceiptFollowUpAction(retryAction, retryCompletion));
                }
                else
                {
                    var attentionCompletion = ResolveWorkspaceProfileReceiptCompletion(activeProfile, receiptAnchor, WorkspaceProfileLaunchActionKind.OpenAttentionView);
                    chain.Add(CreateReceiptFollowUpAction(CreateOpenAttentionViewAction(activeProfile), attentionCompletion));
                }

                return chain;
            }

            foreach (var preferredActionKind in preferredActionKinds)
            {
                var candidate = CreatePreferredReceiptAction(activeProfile, preferredActionKind, queueScopeName, canRetryFailedFamily);
                if (candidate is null)
                {
                    continue;
                }

                var completion = ResolveWorkspaceProfileReceiptCompletion(activeProfile, receiptAnchor, candidate.Kind);
                if (preferredActionKind == WorkspaceProfileLaunchActionKind.RetryFailedFamily
                    && !canRetryFailedFamily
                    && completion is null)
                {
                    continue;
                }

                chain.Add(CreateReceiptFollowUpAction(candidate, completion));
            }

            return chain;
        }

        var cursor = receiptAnchor;
        if (!hasExplicitPreferredActionKinds)
        {
            if (receiptAnchor.ActionKind is WorkspaceProfileLaunchActionKind.RefreshWorkspace or WorkspaceProfileLaunchActionKind.ApplySavedView)
            {
                WorkspaceProfileLaunchAction nextAction;
                if (!string.IsNullOrWhiteSpace(activeProfile.QueueScopeKey))
                {
                    nextAction = new WorkspaceProfileLaunchAction(
                        ProfileId: activeProfile.ProfileId,
                        Kind: WorkspaceProfileLaunchActionKind.PrepareQueue,
                        Title: "Prepare Queue",
                        Summary: $"Prepare the pinned '{queueScopeName}' family queue for this workspace profile.",
                        ExecuteLabel: "Prepare",
                        IsPrimary: true);
                }
                else
                {
                    nextAction = CreateOpenAttentionViewAction(activeProfile);
                }

                var nextCompletion = ResolveWorkspaceProfileReceiptCompletion(activeProfile, cursor, nextAction.Kind);
                chain.Add(CreateReceiptFollowUpAction(nextAction, nextCompletion));
                if (nextCompletion is not null)
                {
                    cursor = nextCompletion;
                }
            }

            var retryCompletionAfterCursor = ResolveWorkspaceProfileReceiptCompletion(activeProfile, cursor, WorkspaceProfileLaunchActionKind.RetryFailedFamily);
            if ((receiptAnchor.ActionKind == WorkspaceProfileLaunchActionKind.PrepareQueue || chain.Any(step => step.Kind == WorkspaceProfileLaunchActionKind.PrepareQueue))
                && (canRetryFailedFamily || retryCompletionAfterCursor is not null))
            {
                var retryAction = new WorkspaceProfileLaunchAction(
                    ProfileId: activeProfile.ProfileId,
                    Kind: WorkspaceProfileLaunchActionKind.RetryFailedFamily,
                    Title: "Retry Failed Family",
                    Summary: $"Retry the failed queue items for the pinned '{queueScopeName}' family.",
                    ExecuteLabel: "Retry",
                    IsPrimary: true);
                chain.Add(CreateReceiptFollowUpAction(retryAction, retryCompletionAfterCursor));
            }

            return chain;
        }

        foreach (var preferredActionKind in preferredActionKinds)
        {
            var candidate = CreatePreferredReceiptAction(activeProfile, preferredActionKind, queueScopeName, canRetryFailedFamily);
            if (candidate is null)
            {
                continue;
            }

            if (candidate.Kind == receiptAnchor.ActionKind)
            {
                continue;
            }

            var completion = ResolveWorkspaceProfileReceiptCompletion(activeProfile, cursor, candidate.Kind);
            if (candidate.Kind == WorkspaceProfileLaunchActionKind.RetryFailedFamily
                && !canRetryFailedFamily
                && completion is null)
            {
                continue;
            }

            chain.Add(CreateReceiptFollowUpAction(candidate, completion));
            if (completion is not null)
            {
                cursor = completion;
            }
        }

        return chain;
    }

    private static IReadOnlyList<WorkspaceProfileLaunchActionKind> ResolvePreferredReceiptActionKinds(WorkspaceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.PreferredActionKinds is { Count: > 0 })
        {
            return profile.PreferredActionKinds
                .Distinct()
                .ToArray();
        }

        return [];
    }

    private static WorkspaceProfileLaunchResult? ResolveWorkspaceProfileReceiptAnchor(WorkspaceProfile activeProfile)
    {
        ArgumentNullException.ThrowIfNull(activeProfile);

        return (activeProfile.LaunchHistory ?? [])
            .OrderByDescending(entry => entry.ExecutedAtUtc)
            .FirstOrDefault(entry =>
                !entry.Succeeded
                || entry.ActionKind is WorkspaceProfileLaunchActionKind.RefreshWorkspace
                    or WorkspaceProfileLaunchActionKind.ApplySavedView);
    }

    private static WorkspaceProfileLaunchResult? ResolveWorkspaceProfileReceiptCompletion(
        WorkspaceProfile activeProfile,
        WorkspaceProfileLaunchResult receiptAnchor,
        WorkspaceProfileLaunchActionKind candidateKind)
    {
        ArgumentNullException.ThrowIfNull(activeProfile);
        ArgumentNullException.ThrowIfNull(receiptAnchor);

        return (activeProfile.LaunchHistory ?? [])
            .Where(entry =>
                entry.Succeeded
                && entry.ActionKind == candidateKind
                && entry.ExecutedAtUtc > receiptAnchor.ExecutedAtUtc)
            .OrderByDescending(entry => entry.ExecutedAtUtc)
            .FirstOrDefault();
    }

    private static WorkspaceProfileLaunchAction CreateReceiptAnchorAction(
        string profileId,
        WorkspaceProfileLaunchResult receiptAnchor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(receiptAnchor);

        var timestamp = receiptAnchor.ExecutedAtUtc.ToLocalTime().ToString("g");
        return new WorkspaceProfileLaunchAction(
            ProfileId: profileId,
            Kind: receiptAnchor.ActionKind,
            Title: receiptAnchor.ActionTitle,
            Summary: receiptAnchor.Summary,
            ExecuteLabel: receiptAnchor.ActionTitle,
            IsPrimary: false,
            LastRunLabel: receiptAnchor.Succeeded
                ? $"Completed {timestamp}"
                : $"Needs attention {timestamp}",
            IsLastRun: true,
            CanExecute: false);
    }

    private static WorkspaceProfileLaunchAction CreateReceiptFollowUpAction(
        WorkspaceProfileLaunchAction action,
        WorkspaceProfileLaunchResult? completion)
    {
        ArgumentNullException.ThrowIfNull(action);

        return action with {
            LastRunLabel = completion is null
                ? "Pending"
                : $"Completed {completion.ExecutedAtUtc.ToLocalTime():g}",
            CanExecute = completion is null
        };
    }

    private static WorkspaceProfileLaunchAction? CreatePreferredReceiptAction(
        WorkspaceProfile activeProfile,
        WorkspaceProfileLaunchActionKind actionKind,
        string? queueScopeName,
        bool canRetryFailedFamily)
    {
        ArgumentNullException.ThrowIfNull(activeProfile);

        return actionKind switch
        {
            WorkspaceProfileLaunchActionKind.PrepareQueue when !string.IsNullOrWhiteSpace(activeProfile.QueueScopeKey)
                => new WorkspaceProfileLaunchAction(
                    ProfileId: activeProfile.ProfileId,
                    Kind: WorkspaceProfileLaunchActionKind.PrepareQueue,
                    Title: "Prepare Queue",
                    Summary: $"Prepare the pinned '{queueScopeName}' family queue for this workspace profile.",
                    ExecuteLabel: "Prepare",
                    IsPrimary: true),
            WorkspaceProfileLaunchActionKind.RetryFailedFamily when !string.IsNullOrWhiteSpace(activeProfile.QueueScopeKey) || canRetryFailedFamily
                => new WorkspaceProfileLaunchAction(
                    ProfileId: activeProfile.ProfileId,
                    Kind: WorkspaceProfileLaunchActionKind.RetryFailedFamily,
                    Title: "Retry Failed Family",
                    Summary: $"Retry the failed queue items for the pinned '{queueScopeName}' family.",
                    ExecuteLabel: "Retry",
                    IsPrimary: true),
            WorkspaceProfileLaunchActionKind.OpenAttentionView
                => CreateOpenAttentionViewAction(activeProfile),
            _ => null
        };
    }

    private static string? FormatLastLaunchLabel(
        WorkspaceProfileLaunchResult? launchResult,
        WorkspaceProfileLaunchActionKind actionKind)
    {
        if (launchResult is null || launchResult.ActionKind != actionKind)
        {
            return null;
        }

        var timestamp = launchResult.ExecutedAtUtc.ToLocalTime().ToString("g");
        var status = launchResult.Succeeded ? "Succeeded" : "Needs attention";
        return $"Last run {timestamp}: {status}";
    }

    private void UpdateActiveWorkspaceLaunchTimeline(WorkspaceProfile? activeProfile)
    {
        PortfolioOverview.ActiveWorkspaceLaunchTimeline.Clear();
        if (activeProfile is null)
        {
            PortfolioOverview.ActiveWorkspaceTimelineHeadline = "Today's activity";
            PortfolioOverview.ActiveWorkspaceTimelineDetails = "Apply a workspace profile to see the recent launch timeline for this estate.";
            return;
        }

        var today = DateTimeOffset.Now.Date;
        var timelineEntries = (activeProfile.LaunchHistory ?? [])
            .Where(entry => entry.ExecutedAtUtc.ToLocalTime().Date == today)
            .OrderByDescending(entry => entry.ExecutedAtUtc)
            .Take(6)
            .ToArray();

        foreach (var entry in timelineEntries)
        {
            PortfolioOverview.ActiveWorkspaceLaunchTimeline.Add(new WorkspaceProfileLaunchTimelineItem(
                Title: entry.ActionTitle,
                StatusLabel: entry.Succeeded ? "Succeeded" : "Needs attention",
                Summary: entry.Summary,
                ExecutedLabel: entry.ExecutedAtUtc.ToLocalTime().ToString("g"),
                IsLatest: activeProfile.LastLaunchResult is not null
                    && entry.ActionKind == activeProfile.LastLaunchResult.ActionKind
                    && entry.ExecutedAtUtc == activeProfile.LastLaunchResult.ExecutedAtUtc));
        }

        PortfolioOverview.ActiveWorkspaceTimelineHeadline = timelineEntries.Length == 0
            ? "Today's activity"
            : $"{timelineEntries.Length} activity item(s) today";
        PortfolioOverview.ActiveWorkspaceTimelineDetails = timelineEntries.Length == 0
            ? "No launch actions have been recorded for today yet."
            : "The latest launch-board actions for this profile are listed newest-first.";
    }

    private static IReadOnlyList<WorkspaceProfileLaunchResult> BuildLaunchHistory(
        WorkspaceProfile profile,
        WorkspaceProfileLaunchAction action,
        bool succeeded,
        string summary)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(action);

        var launchResult = new WorkspaceProfileLaunchResult(
            action.Kind,
            action.Title,
            succeeded,
            summary,
            DateTimeOffset.UtcNow);

        return profile.LaunchHistory
            .Prepend(launchResult)
            .OrderByDescending(result => result.ExecutedAtUtc)
            .Take(8)
            .ToArray();
    }

    private void UpdateWorkspaceProfileCards()
    {
        PortfolioOverview.WorkspaceProfileCards.Clear();
        foreach (var profile in PortfolioOverview.WorkspaceProfiles
                     .OrderByDescending(profile => string.Equals(profile.ProfileId, _activeWorkspaceProfileId, StringComparison.OrdinalIgnoreCase))
                     .ThenByDescending(profile => profile.UpdatedAtUtc))
        {
            var savedViewLabel = string.IsNullOrWhiteSpace(profile.SavedViewId)
                ? "Saved view: none"
                : $"Saved view: {PortfolioOverview.SavedViews.FirstOrDefault(view =>
                    string.Equals(view.ViewId, profile.SavedViewId, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? profile.SavedViewId}";
            var queueScopeLabel = string.IsNullOrWhiteSpace(profile.QueueScopeDisplayName)
                ? $"Queue scope: {profile.QueueScopeKey ?? "none"}"
                : $"Queue scope: {profile.QueueScopeDisplayName}";
            var (healthLabel, healthDetails) = CalculateWorkspaceProfileHealth(profile);
            var (routeKind, routeLabel, routeDetails) = ResolveWorkspaceProfileHeroRoute(profile, healthLabel);
            var startupStrategyLabel = WorkspaceProfileStartupPreferenceFormatting.FormatBehavior(
                hasSavedView: !string.IsNullOrWhiteSpace(profile.SavedViewId),
                applyAfterSavedView: profile.ApplyStartupPreferenceAfterSavedView,
                profile.PreferredStartupFocusMode,
                profile.PreferredStartupSearchText,
                profile.PreferredStartupFamilyDisplayName ?? profile.PreferredStartupFamilyKey);

            PortfolioOverview.WorkspaceProfileCards.Add(new WorkspaceProfileHeroCard(
                ProfileId: profile.ProfileId,
                DisplayName: profile.DisplayName,
                Description: string.IsNullOrWhiteSpace(profile.Description)
                    ? "No profile description yet."
                    : profile.Description,
                TodayNote: string.IsNullOrWhiteSpace(profile.TodayNote)
                    ? "No today note saved yet."
                    : profile.TodayNote,
                HealthLabel: healthLabel,
                HealthDetails: healthDetails,
                RouteKind: routeKind,
                RouteLabel: routeLabel,
                RouteDetails: routeDetails,
                WorkspaceLabel: profile.WorkspaceRoot,
                SavedViewLabel: savedViewLabel,
                StartupStrategyLabel: startupStrategyLabel,
                QueueScopeLabel: queueScopeLabel,
                StatusLabel: string.Equals(profile.ProfileId, _activeWorkspaceProfileId, StringComparison.OrdinalIgnoreCase)
                    ? "Active"
                    : "Profile",
                IsActive: string.Equals(profile.ProfileId, _activeWorkspaceProfileId, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private static (string HealthLabel, string HealthDetails) CalculateWorkspaceProfileHealth(WorkspaceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var today = DateTimeOffset.Now.Date;
        var todayEntries = (profile.LaunchHistory ?? [])
            .Where(entry => entry.ExecutedAtUtc.ToLocalTime().Date == today)
            .OrderByDescending(entry => entry.ExecutedAtUtc)
            .ToArray();

        if (todayEntries.Length == 0)
        {
            return ("No Activity", "No launch-board actions have been recorded for this profile today.");
        }

        var latest = todayEntries[0];
        if (todayEntries.Any(entry => !entry.Succeeded))
        {
            return (
                "Needs Attention",
                $"At least one launch action failed today. Latest: {latest.ActionTitle} at {latest.ExecutedAtUtc.ToLocalTime():g}.");
        }

        var latestLocal = latest.ExecutedAtUtc.ToLocalTime();
        if (latestLocal < DateTimeOffset.Now.AddHours(-4))
        {
            return (
                "Stale Today",
                $"All recorded actions succeeded, but the latest run was {latestLocal:g}, so this desk may need a refresh.");
        }

        return (
            "Green Today",
            $"Today's recorded actions are succeeding. Latest: {latest.ActionTitle} at {latestLocal:g}.");
    }

    private static (WorkspaceProfileHeroRouteKind RouteKind, string RouteLabel, string RouteDetails) ResolveWorkspaceProfileHeroRoute(
        WorkspaceProfile profile,
        string healthLabel)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(healthLabel);

        return healthLabel switch
        {
            "Needs Attention" => (
                WorkspaceProfileHeroRouteKind.ResumeDesk,
                "Resume Desk",
                "Open the profile with its saved view and queue scope so attention items are front and center."),
            "Stale Today" or "No Activity" => (
                WorkspaceProfileHeroRouteKind.RefreshDesk,
                "Refresh Desk",
                "Apply the profile and immediately rescan the workspace so today's release context is current."),
            _ => (
                WorkspaceProfileHeroRouteKind.OpenDesk,
                "Open Desk",
                "Apply the profile and reopen its current operating context without forcing a refresh.")
        };
    }

    private static WorkspaceProfileLaunchAction CreateRefreshWorkspaceLaunchAction(WorkspaceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return new WorkspaceProfileLaunchAction(
            ProfileId: profile.ProfileId,
            Kind: WorkspaceProfileLaunchActionKind.RefreshWorkspace,
            Title: "Refresh Workspace",
            Summary: $"Rescan {profile.WorkspaceRoot} and refresh the active release desk.",
            ExecuteLabel: "Refresh",
            IsPrimary: false,
            LastRunLabel: null,
            IsLastRun: false);
    }

    private static WorkspaceProfileLaunchAction CreateOpenAttentionViewAction(WorkspaceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var queueScopeName = string.IsNullOrWhiteSpace(profile.QueueScopeDisplayName)
            ? profile.QueueScopeKey
            : profile.QueueScopeDisplayName;
        var summary = string.IsNullOrWhiteSpace(queueScopeName)
            ? "Open the attention preset so issues across this estate come back into focus."
            : $"Open the attention preset and keep the pinned '{queueScopeName}' family scope in view.";

        return new WorkspaceProfileLaunchAction(
            ProfileId: profile.ProfileId,
            Kind: WorkspaceProfileLaunchActionKind.OpenAttentionView,
            Title: "Open Attention View",
            Summary: summary,
            ExecuteLabel: "Open Attention",
            IsPrimary: true);
    }
}
