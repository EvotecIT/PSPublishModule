using System.Collections.ObjectModel;
using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed class PortfolioOverviewViewModel : ViewModelBase
{
    private string _summaryHeadline = "Release cockpit foundation";
    private string _summaryDetails = "Scan the workspace, persist a catalog snapshot, and shape the shell before queue automation.";
    private string _planCoverageText = "Plan preview has not run yet.";
    private PortfolioFocusOption _selectedFocus;
    private string _focusHeadline = "All managed repositories are visible.";
    private string _focusDetails = "Use the focus selector to narrow the portfolio to the repos that matter right now.";
    private string _presetHeadline = "Preset buttons are ready once the portfolio is loaded.";
    private string _viewMemory = "This triage view is saved automatically to local state once a portfolio snapshot exists.";
    private string _searchText = string.Empty;
    private string _buildEngineStatus = "Unknown";
    private string _buildEngineHeadline = "PSPublishModule engine not resolved yet.";
    private string _buildEngineDetails = "Prepare Queue will capture the module manifest path used by build, publish, and signing adapters.";
    private string _buildEngineAdvisory = "Use this card to understand whether the shell is using the repo manifest, an override, or an installed module.";
    private string _activeWorkspaceContextHeadline = "Active context will appear after the first scan.";
    private string _activeWorkspaceContextDetails = "Profiles can pin a workspace root, saved view, and queue scope so startup returns you to the same operating context.";
    private string _savedViewsHeadline = "No saved views loaded yet.";
    private string _savedViewsDetails = "Named portfolio views are stored per workspace root.";
    private string _savedViewDraftName = string.Empty;
    private string _workspaceProfilesHeadline = "No workspace profiles loaded yet.";
    private string _workspaceProfilesDetails = "Profiles combine a workspace root with a preferred saved view and queue scope.";
    private WorkspaceProfileTemplate? _selectedWorkspaceProfileTemplate;
    private string _workspaceProfileDraftName = string.Empty;
    private string _workspaceProfileDraftDescription = string.Empty;
    private string _workspaceProfileDraftTodayNote = string.Empty;
    private string _workspaceProfileDraftActionChain = string.Empty;
    private string _workspaceProfileDraftStartupFocus = string.Empty;
    private string _workspaceProfileDraftStartupSearch = string.Empty;
    private string _workspaceProfileDraftStartupFamily = string.Empty;
    private bool _workspaceProfileDraftApplyStartupPreferenceAfterSavedView;
    private string _activeWorkspaceAgendaHeadline = "Today's agenda will appear after a profile is active.";
    private string _activeWorkspaceAgendaDetails = "Profiles can carry a short release note or checklist so the same launch intent comes back on startup.";
    private string _activeWorkspaceHealthHeadline = "Desk health will appear after a profile is active.";
    private string _activeWorkspaceHealthDetails = "Today's launch outcomes will roll up into a quick health signal for the active estate.";
    private string _activeWorkspaceReceiptHeadline = "Latest desk receipt will appear after a profile action.";
    private string _activeWorkspaceReceiptDetails = "Hero-card routes and launch-board actions leave a short receipt here so you can see the latest desk transition at a glance.";
    private WorkspaceProfileLaunchAction? _activeWorkspaceReceiptAction;
    private string _activeWorkspaceLaunchBoardHeadline = "Launch board will appear after a profile is active.";
    private string _activeWorkspaceLaunchBoardDetails = "Launch actions turn the restored profile context into the next concrete steps.";
    private string _activeWorkspaceTimelineHeadline = "Activity timeline will appear after a profile is active.";
    private string _activeWorkspaceTimelineDetails = "Launch actions will build a short same-day history for this workspace profile.";
    private PortfolioQuickPreset? _selectedPreset;
    private PortfolioSavedViewItem? _selectedSavedView;
    private WorkspaceProfile? _selectedWorkspaceProfile;

    public PortfolioOverviewViewModel()
    {
        FocusModes = new ObservableCollection<PortfolioFocusOption> {
            new(RepositoryPortfolioFocusMode.All, "All", "Show every managed repository in the current portfolio snapshot."),
            new(RepositoryPortfolioFocusMode.Attention, "Attention", "Focus on repos with local readiness issues, GitHub pressure, release drift, or failed queue states."),
            new(RepositoryPortfolioFocusMode.Ready, "Ready Today", "Focus on repos that are locally ready and already have successful plan previews."),
            new(RepositoryPortfolioFocusMode.QueueActive, "Queue Active", "Focus on repos already past prepare or holding an actionable queue state."),
            new(RepositoryPortfolioFocusMode.Blocked, "Blocked", "Focus on repos blocked by readiness or failed queue transitions."),
            new(RepositoryPortfolioFocusMode.WaitingUsb, "USB Waiting", "Focus on repos paused at the signing gate and waiting for the USB token.")
        };
        QuickPresets = new ObservableCollection<PortfolioQuickPreset> {
            new("ready-today", "Ready Today", RepositoryPortfolioFocusMode.Ready, string.Empty, "Repos that are locally ready with successful plan previews."),
            new("attention", "Attention", RepositoryPortfolioFocusMode.Attention, string.Empty, "Repos that need action because of readiness, GitHub, drift, or queue pressure."),
            new("usb-waiting", "USB Waiting", RepositoryPortfolioFocusMode.WaitingUsb, string.Empty, "Repos currently paused at the signing checkpoint."),
            new("queue-active", "Queue Active", RepositoryPortfolioFocusMode.QueueActive, string.Empty, "Repos already moving through build, sign, publish, or verify."),
            new("all", "Reset", RepositoryPortfolioFocusMode.All, string.Empty, "Return to the full managed portfolio.")
        };
        WorkspaceProfileTemplates = new ObservableCollection<WorkspaceProfileTemplate>(WorkspaceProfileTemplateCatalog.CreateDefaultTemplates());

        _selectedFocus = FocusModes[0];
    }

    public ObservableCollection<PortfolioFocusOption> FocusModes { get; }

    public ObservableCollection<PortfolioQuickPreset> QuickPresets { get; }

    public ObservableCollection<PortfolioDashboardCard> DashboardCards { get; } = [];

    public ObservableCollection<PortfolioSavedViewItem> SavedViews { get; } = [];

    public ObservableCollection<WorkspaceProfile> WorkspaceProfiles { get; } = [];

    public ObservableCollection<WorkspaceProfileTemplate> WorkspaceProfileTemplates { get; }

    public ObservableCollection<WorkspaceProfileHeroCard> WorkspaceProfileCards { get; } = [];

    public ObservableCollection<WorkspaceProfileLaunchAction> ActiveWorkspaceLaunchActions { get; } = [];

    public ObservableCollection<WorkspaceProfileLaunchAction> ActiveWorkspaceReceiptActions { get; } = [];

    public ObservableCollection<WorkspaceProfileLaunchTimelineItem> ActiveWorkspaceLaunchTimeline { get; } = [];

    public string SummaryHeadline
    {
        get => _summaryHeadline;
        set => SetProperty(ref _summaryHeadline, value);
    }

    public string SummaryDetails
    {
        get => _summaryDetails;
        set => SetProperty(ref _summaryDetails, value);
    }

    public string PlanCoverageText
    {
        get => _planCoverageText;
        set => SetProperty(ref _planCoverageText, value);
    }

    public PortfolioFocusOption SelectedFocus
    {
        get => _selectedFocus;
        set => SetProperty(ref _selectedFocus, value);
    }

    public string FocusHeadline
    {
        get => _focusHeadline;
        set => SetProperty(ref _focusHeadline, value);
    }

    public string FocusDetails
    {
        get => _focusDetails;
        set => SetProperty(ref _focusDetails, value);
    }

    public string PresetHeadline
    {
        get => _presetHeadline;
        set => SetProperty(ref _presetHeadline, value);
    }

    public string ViewMemory
    {
        get => _viewMemory;
        set => SetProperty(ref _viewMemory, value);
    }

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public string BuildEngineStatus
    {
        get => _buildEngineStatus;
        set => SetProperty(ref _buildEngineStatus, value);
    }

    public string BuildEngineHeadline
    {
        get => _buildEngineHeadline;
        set => SetProperty(ref _buildEngineHeadline, value);
    }

    public string BuildEngineDetails
    {
        get => _buildEngineDetails;
        set => SetProperty(ref _buildEngineDetails, value);
    }

    public string BuildEngineAdvisory
    {
        get => _buildEngineAdvisory;
        set => SetProperty(ref _buildEngineAdvisory, value);
    }

    public string ActiveWorkspaceContextHeadline
    {
        get => _activeWorkspaceContextHeadline;
        set => SetProperty(ref _activeWorkspaceContextHeadline, value);
    }

    public string ActiveWorkspaceContextDetails
    {
        get => _activeWorkspaceContextDetails;
        set => SetProperty(ref _activeWorkspaceContextDetails, value);
    }

    public string SavedViewsHeadline
    {
        get => _savedViewsHeadline;
        set => SetProperty(ref _savedViewsHeadline, value);
    }

    public string SavedViewsDetails
    {
        get => _savedViewsDetails;
        set => SetProperty(ref _savedViewsDetails, value);
    }

    public string SavedViewDraftName
    {
        get => _savedViewDraftName;
        set => SetProperty(ref _savedViewDraftName, value);
    }

    public string WorkspaceProfilesHeadline
    {
        get => _workspaceProfilesHeadline;
        set => SetProperty(ref _workspaceProfilesHeadline, value);
    }

    public string WorkspaceProfilesDetails
    {
        get => _workspaceProfilesDetails;
        set => SetProperty(ref _workspaceProfilesDetails, value);
    }

    public string WorkspaceProfileDraftName
    {
        get => _workspaceProfileDraftName;
        set => SetProperty(ref _workspaceProfileDraftName, value);
    }

    public string WorkspaceProfileDraftDescription
    {
        get => _workspaceProfileDraftDescription;
        set => SetProperty(ref _workspaceProfileDraftDescription, value);
    }

    public string WorkspaceProfileDraftTodayNote
    {
        get => _workspaceProfileDraftTodayNote;
        set => SetProperty(ref _workspaceProfileDraftTodayNote, value);
    }

    public string WorkspaceProfileDraftActionChain
    {
        get => _workspaceProfileDraftActionChain;
        set => SetProperty(ref _workspaceProfileDraftActionChain, value);
    }

    public string WorkspaceProfileDraftStartupFocus
    {
        get => _workspaceProfileDraftStartupFocus;
        set => SetProperty(ref _workspaceProfileDraftStartupFocus, value);
    }

    public string WorkspaceProfileDraftStartupSearch
    {
        get => _workspaceProfileDraftStartupSearch;
        set => SetProperty(ref _workspaceProfileDraftStartupSearch, value);
    }

    public string WorkspaceProfileDraftStartupFamily
    {
        get => _workspaceProfileDraftStartupFamily;
        set => SetProperty(ref _workspaceProfileDraftStartupFamily, value);
    }

    public bool WorkspaceProfileDraftApplyStartupPreferenceAfterSavedView
    {
        get => _workspaceProfileDraftApplyStartupPreferenceAfterSavedView;
        set => SetProperty(ref _workspaceProfileDraftApplyStartupPreferenceAfterSavedView, value);
    }

    public string ActiveWorkspaceAgendaHeadline
    {
        get => _activeWorkspaceAgendaHeadline;
        set => SetProperty(ref _activeWorkspaceAgendaHeadline, value);
    }

    public string ActiveWorkspaceAgendaDetails
    {
        get => _activeWorkspaceAgendaDetails;
        set => SetProperty(ref _activeWorkspaceAgendaDetails, value);
    }

    public string ActiveWorkspaceHealthHeadline
    {
        get => _activeWorkspaceHealthHeadline;
        set => SetProperty(ref _activeWorkspaceHealthHeadline, value);
    }

    public string ActiveWorkspaceHealthDetails
    {
        get => _activeWorkspaceHealthDetails;
        set => SetProperty(ref _activeWorkspaceHealthDetails, value);
    }

    public string ActiveWorkspaceReceiptHeadline
    {
        get => _activeWorkspaceReceiptHeadline;
        set => SetProperty(ref _activeWorkspaceReceiptHeadline, value);
    }

    public string ActiveWorkspaceReceiptDetails
    {
        get => _activeWorkspaceReceiptDetails;
        set => SetProperty(ref _activeWorkspaceReceiptDetails, value);
    }

    public WorkspaceProfileLaunchAction? ActiveWorkspaceReceiptAction
    {
        get => _activeWorkspaceReceiptAction;
        set
        {
            if (SetProperty(ref _activeWorkspaceReceiptAction, value))
            {
                RaisePropertyChanged(nameof(HasActiveWorkspaceReceiptAction));
            }
        }
    }

    public bool HasActiveWorkspaceReceiptAction => ActiveWorkspaceReceiptAction is not null;

    public string ActiveWorkspaceLaunchBoardHeadline
    {
        get => _activeWorkspaceLaunchBoardHeadline;
        set => SetProperty(ref _activeWorkspaceLaunchBoardHeadline, value);
    }

    public string ActiveWorkspaceLaunchBoardDetails
    {
        get => _activeWorkspaceLaunchBoardDetails;
        set => SetProperty(ref _activeWorkspaceLaunchBoardDetails, value);
    }

    public string ActiveWorkspaceTimelineHeadline
    {
        get => _activeWorkspaceTimelineHeadline;
        set => SetProperty(ref _activeWorkspaceTimelineHeadline, value);
    }

    public string ActiveWorkspaceTimelineDetails
    {
        get => _activeWorkspaceTimelineDetails;
        set => SetProperty(ref _activeWorkspaceTimelineDetails, value);
    }

    public PortfolioQuickPreset? SelectedPreset
    {
        get => _selectedPreset;
        set => SetProperty(ref _selectedPreset, value);
    }

    public PortfolioSavedViewItem? SelectedSavedView
    {
        get => _selectedSavedView;
        set => SetProperty(ref _selectedSavedView, value);
    }

    public WorkspaceProfile? SelectedWorkspaceProfile
    {
        get => _selectedWorkspaceProfile;
        set => SetProperty(ref _selectedWorkspaceProfile, value);
    }

    public WorkspaceProfileTemplate? SelectedWorkspaceProfileTemplate
    {
        get => _selectedWorkspaceProfileTemplate;
        set => SetProperty(ref _selectedWorkspaceProfileTemplate, value);
    }
}
