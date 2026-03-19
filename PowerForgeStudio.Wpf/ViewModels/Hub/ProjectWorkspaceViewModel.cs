using System.Collections.ObjectModel;
using System.Diagnostics;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Orchestrator.Explorer;
using PowerForgeStudio.Orchestrator.Hub;
using PowerForgeStudio.Orchestrator.Terminal;

namespace PowerForgeStudio.Wpf.ViewModels.Hub;

public sealed class ProjectWorkspaceViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly ProjectEntry _entry;
    private readonly GitHubProjectService _gitHubService;
    private readonly ProjectBuildService _buildService;
    private readonly TerminalSessionService _terminalService;
    private readonly ProjectGitService _gitService;
    private int _selectedTabIndex;
    private bool _isLoadingIssues;
    private bool _isLoadingPrs;
    private bool _isBuilding;
    private string _buildOutput = string.Empty;
    private TerminalTabViewModel? _activeTerminal;
    private FileExplorerViewModel? _fileExplorer;

    public ProjectWorkspaceViewModel(
        ProjectEntry entry,
        GitHubProjectService gitHubService,
        ProjectBuildService buildService,
        ProjectGitService? gitService = null,
        TerminalSessionService? terminalService = null)
    {
        _entry = entry;
        _gitHubService = gitHubService;
        _buildService = buildService;
        _gitService = gitService ?? new ProjectGitService();
        _terminalService = terminalService ?? new TerminalSessionService();

        Issues = [];
        PullRequests = [];
        BuildResults = [];
        GitLog = [];

        // Load git log immediately for the Overview tab
        _ = LoadGitLogAsync();

        LoadIssuesCommand = new AsyncDelegateCommand(LoadIssuesAsync, () => !_isLoadingIssues && _entry.GitHubSlug is not null);
        LoadPullRequestsCommand = new AsyncDelegateCommand(LoadPullRequestsAsync, () => !_isLoadingPrs && _entry.GitHubSlug is not null);
        RunBuildCommand = new AsyncDelegateCommand(RunBuildAsync, () => !_isBuilding && _entry.IsReleaseManaged);

        OpenInExplorerCommand = new DelegateCommand<object?>(_ =>
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", _entry.RootPath)); } catch { }
        });

        OpenInVsCodeCommand = new DelegateCommand<object?>(_ =>
        {
            try { Process.Start(new ProcessStartInfo("code", _entry.RootPath) { UseShellExecute = true }); } catch { }
        });

        OpenInTerminalCommand = new DelegateCommand<object?>(_ =>
        {
            try { Process.Start(new ProcessStartInfo("wt", $"-d \"{_entry.RootPath}\"") { UseShellExecute = true }); } catch { }
        });
    }

    public string ProjectName => _entry.Name;

    public string RootPath => _entry.RootPath;

    public string CategoryDisplay => _entry.CategoryDisplay;

    public string BuildScriptDisplay => _entry.BuildScriptKind switch
    {
        BuildScriptKind.BuildModule => "Build-Module.ps1",
        BuildScriptKind.BuildProject => "Build-Project.ps1",
        BuildScriptKind.PowerForgeJson => "powerforge.json",
        BuildScriptKind.ProjectBuildJson => "project.build.json",
        BuildScriptKind.Hybrid => "Hybrid (multiple scripts)",
        _ => "No build script"
    };

    public string? GitHubSlug => _entry.GitHubSlug;

    public string? AzureDevOpsSlug => _entry.AzureDevOpsSlug;

    public bool HasGitHub => _entry.GitHubSlug is not null;

    public bool HasAzureDevOps => _entry.AzureDevOpsSlug is not null;

    public bool HasBuildScript => _entry.IsReleaseManaged;

    public ObservableCollection<GitHubIssue> Issues { get; }

    public ObservableCollection<GitHubPullRequest> PullRequests { get; }

    public ObservableCollection<ProjectBuildResult> BuildResults { get; }

    public ObservableCollection<GitLogEntry> GitLog { get; }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (SetProperty(ref _selectedTabIndex, value))
            {
                OnTabSelected(value);
            }
        }
    }

    public bool IsLoadingIssues
    {
        get => _isLoadingIssues;
        private set => SetProperty(ref _isLoadingIssues, value);
    }

    public bool IsLoadingPrs
    {
        get => _isLoadingPrs;
        private set => SetProperty(ref _isLoadingPrs, value);
    }

    public bool IsBuilding
    {
        get => _isBuilding;
        private set
        {
            if (SetProperty(ref _isBuilding, value))
            {
                RunBuildCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string BuildOutput
    {
        get => _buildOutput;
        private set => SetProperty(ref _buildOutput, value);
    }

    public TerminalTabViewModel? ActiveTerminal
    {
        get => _activeTerminal;
        private set => SetProperty(ref _activeTerminal, value);
    }

    public FileExplorerViewModel? FileExplorer
    {
        get => _fileExplorer;
        private set => SetProperty(ref _fileExplorer, value);
    }

    public AsyncDelegateCommand LoadIssuesCommand { get; }
    public AsyncDelegateCommand LoadPullRequestsCommand { get; }
    public AsyncDelegateCommand RunBuildCommand { get; }
    public DelegateCommand<object?> OpenInExplorerCommand { get; }
    public DelegateCommand<object?> OpenInVsCodeCommand { get; }
    public DelegateCommand<object?> OpenInTerminalCommand { get; }

    private void OnTabSelected(int tabIndex)
    {
        switch (tabIndex)
        {
            case 1 when Issues.Count == 0 && HasGitHub:
                _ = LoadIssuesAsync();
                break;
            case 2 when PullRequests.Count == 0 && HasGitHub:
                _ = LoadPullRequestsAsync();
                break;
            case 4 when ActiveTerminal is null:
                CreateTerminal();
                break;
            case 5 when FileExplorer is null:
                FileExplorer = new FileExplorerViewModel(_entry.RootPath, new FileExplorerService());
                break;
        }
    }

    private async Task LoadGitLogAsync()
    {
        try
        {
            var log = await _gitService.GetLogAsync(_entry.RootPath).ConfigureAwait(true);
            foreach (var entry in log)
            {
                GitLog.Add(entry);
            }
        }
        catch
        {
            // Non-fatal
        }
    }

    private void CreateTerminal()
    {
        // Session creation is deferred until WebView2 is ready (see TerminalControl)
        ActiveTerminal = new TerminalTabViewModel(_terminalService, _entry.RootPath);
    }

    public async ValueTask DisposeAsync()
    {
        if (_activeTerminal is not null)
        {
            await _activeTerminal.DisposeAsync().ConfigureAwait(false);
            _activeTerminal = null;
        }

        await _terminalService.DisposeAsync().ConfigureAwait(false);

        _fileExplorer?.Dispose();
        _fileExplorer = null;
    }

    private async Task LoadIssuesAsync()
    {
        if (_entry.GitHubSlug is null || _isLoadingIssues)
        {
            return;
        }

        IsLoadingIssues = true;
        try
        {
            var issues = await _gitHubService.FetchIssuesAsync(_entry.GitHubSlug).ConfigureAwait(true);
            Issues.Clear();
            foreach (var issue in issues)
            {
                Issues.Add(issue);
            }
        }
        catch
        {
            // Silently fail -- user can retry
        }
        finally
        {
            IsLoadingIssues = false;
        }
    }

    private async Task LoadPullRequestsAsync()
    {
        if (_entry.GitHubSlug is null || _isLoadingPrs)
        {
            return;
        }

        IsLoadingPrs = true;
        try
        {
            var prs = await _gitHubService.FetchPullRequestsAsync(_entry.GitHubSlug).ConfigureAwait(true);
            PullRequests.Clear();
            foreach (var pr in prs)
            {
                PullRequests.Add(pr);
            }
        }
        catch
        {
            // Silently fail -- user can retry
        }
        finally
        {
            IsLoadingPrs = false;
        }
    }

    private async Task RunBuildAsync()
    {
        IsBuilding = true;
        BuildOutput = string.Empty;
        try
        {
            var result = await _buildService.RunBuildStreamingAsync(
                _entry,
                line => BuildOutput += line + "\n").ConfigureAwait(true);
            BuildResults.Insert(0, result);
        }
        catch (Exception exception)
        {
            BuildOutput += $"\nBuild error: {exception.Message}";
        }
        finally
        {
            IsBuilding = false;
        }
    }
}
