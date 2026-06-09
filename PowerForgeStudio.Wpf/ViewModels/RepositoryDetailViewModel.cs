using System.Collections.ObjectModel;
using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed class RepositoryDetailViewModel : ViewModelBase
{
    private string _headline = "No repository selected";
    private string _badge = "Selection pending";
    private string _readiness = "Unknown";
    private string _reason = "Select a managed repository to inspect its contract, queue state, and adapter evidence.";
    private string _path = "No repository path selected.";
    private string _branch = "Branch information unavailable";
    private string _gitDiagnostics = "No git diagnostics yet.";
    private string _gitDiagnosticsDetail = "Git preflight guidance will appear here once the shell inspects a repository.";
    private string _lastGitAction = "No git action recorded yet.";
    private string _lastGitActionSummary = "Quick-action results will appear here after you run a git action from the repository detail pane.";
    private string _lastGitActionOutput = "No output captured yet.";
    private string _lastGitActionError = "No error captured yet.";
    private string _buildContract = "No build contract selected.";
    private string _queueLane = "No queue state selected.";
    private string _queueCheckpoint = "No checkpoint selected.";
    private string _queueSummary = "Queue evidence will appear here after a repository is selected and the queue has been prepared.";
    private string _queuePayload = "No checkpoint payload captured yet.";
    private string _releaseDrift = "Unknown";
    private string _releaseDriftDetail = "Release drift will appear here once remote and local signals are available.";
    private string _engineDisplay = "Build engine not resolved yet.";
    private string _enginePath = "No engine path resolved yet.";
    private string _engineAdvisory = "Engine guidance will appear here once the shell resolves PSPublishModule.";

    public ObservableCollection<RepositoryAdapterEvidence> Evidence { get; } = [];

    public ObservableCollection<RepositoryGitRemediationStep> GitRemediationSteps { get; } = [];

    public ObservableCollection<RepositoryGitQuickAction> GitQuickActions { get; } = [];

    public string Headline
    {
        get => _headline;
        private set => SetProperty(ref _headline, value);
    }

    public string Badge
    {
        get => _badge;
        private set => SetProperty(ref _badge, value);
    }

    public string Readiness
    {
        get => _readiness;
        private set => SetProperty(ref _readiness, value);
    }

    public string Reason
    {
        get => _reason;
        private set => SetProperty(ref _reason, value);
    }

    public string Path
    {
        get => _path;
        private set => SetProperty(ref _path, value);
    }

    public string Branch
    {
        get => _branch;
        private set => SetProperty(ref _branch, value);
    }

    public string GitDiagnostics
    {
        get => _gitDiagnostics;
        private set => SetProperty(ref _gitDiagnostics, value);
    }

    public string GitDiagnosticsDetail
    {
        get => _gitDiagnosticsDetail;
        private set => SetProperty(ref _gitDiagnosticsDetail, value);
    }

    public string LastGitAction
    {
        get => _lastGitAction;
        private set => SetProperty(ref _lastGitAction, value);
    }

    public string LastGitActionSummary
    {
        get => _lastGitActionSummary;
        private set => SetProperty(ref _lastGitActionSummary, value);
    }

    public string LastGitActionOutput
    {
        get => _lastGitActionOutput;
        private set => SetProperty(ref _lastGitActionOutput, value);
    }

    public string LastGitActionError
    {
        get => _lastGitActionError;
        private set => SetProperty(ref _lastGitActionError, value);
    }

    public string BuildContract
    {
        get => _buildContract;
        private set => SetProperty(ref _buildContract, value);
    }

    public string QueueLane
    {
        get => _queueLane;
        private set => SetProperty(ref _queueLane, value);
    }

    public string QueueCheckpoint
    {
        get => _queueCheckpoint;
        private set => SetProperty(ref _queueCheckpoint, value);
    }

    public string QueueSummary
    {
        get => _queueSummary;
        private set => SetProperty(ref _queueSummary, value);
    }

    public string QueuePayload
    {
        get => _queuePayload;
        private set => SetProperty(ref _queuePayload, value);
    }

    public string ReleaseDrift
    {
        get => _releaseDrift;
        private set => SetProperty(ref _releaseDrift, value);
    }

    public string ReleaseDriftDetail
    {
        get => _releaseDriftDetail;
        private set => SetProperty(ref _releaseDriftDetail, value);
    }

    public string EngineDisplay
    {
        get => _engineDisplay;
        private set => SetProperty(ref _engineDisplay, value);
    }

    public string EnginePath
    {
        get => _enginePath;
        private set => SetProperty(ref _enginePath, value);
    }

    public string EngineAdvisory
    {
        get => _engineAdvisory;
        private set => SetProperty(ref _engineAdvisory, value);
    }

    public void ApplySnapshot(RepositoryDetailSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Headline = snapshot.RepositoryName;
        Badge = snapshot.RepositoryBadge;
        Readiness = snapshot.ReadinessDisplay;
        Reason = snapshot.ReadinessReason;
        Path = snapshot.RootPath;
        Branch = snapshot.BranchDisplay;
        GitDiagnostics = snapshot.GitDiagnosticsDisplay;
        GitDiagnosticsDetail = snapshot.GitDiagnosticsDetail;
        LastGitAction = snapshot.LastGitActionDisplay;
        LastGitActionSummary = snapshot.LastGitActionSummary;
        LastGitActionOutput = snapshot.LastGitActionOutput;
        LastGitActionError = snapshot.LastGitActionError;
        BuildContract = snapshot.BuildContractDisplay;
        QueueLane = snapshot.QueueLaneDisplay;
        QueueCheckpoint = snapshot.QueueCheckpointDisplay;
        QueueSummary = snapshot.QueueSummary;
        QueuePayload = snapshot.QueueCheckpointPayload;
        ReleaseDrift = snapshot.ReleaseDriftDisplay;
        ReleaseDriftDetail = snapshot.ReleaseDriftDetail;
        EngineDisplay = snapshot.BuildEngineDisplay;
        EnginePath = snapshot.BuildEnginePath;
        EngineAdvisory = snapshot.BuildEngineAdvisory;

        Evidence.Clear();
        foreach (var evidence in snapshot.AdapterEvidence)
        {
            Evidence.Add(evidence);
        }

        GitRemediationSteps.Clear();
        foreach (var step in snapshot.GitRemediationSteps)
        {
            GitRemediationSteps.Add(step);
        }

        GitQuickActions.Clear();
        foreach (var action in snapshot.GitQuickActions)
        {
            GitQuickActions.Add(action);
        }
    }
}
