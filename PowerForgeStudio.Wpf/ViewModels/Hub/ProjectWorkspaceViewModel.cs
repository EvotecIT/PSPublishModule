using System.Collections.ObjectModel;
using System.Diagnostics;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Orchestrator.Hub;

namespace PowerForgeStudio.Wpf.ViewModels.Hub;

public sealed class ProjectWorkspaceViewModel : ViewModelBase
{
    private readonly ProjectEntry _entry;
    private readonly GitHubProjectService _gitHubService;
    private readonly ProjectBuildService _buildService;
    private int _selectedTabIndex;
    private bool _isLoadingIssues;
    private bool _isLoadingPrs;
    private bool _isBuilding;
    private string _buildOutput = string.Empty;

    public ProjectWorkspaceViewModel(
        ProjectEntry entry,
        GitHubProjectService gitHubService,
        ProjectBuildService buildService)
    {
        _entry = entry;
        _gitHubService = gitHubService;
        _buildService = buildService;

        Issues = [];
        PullRequests = [];
        BuildResults = [];

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
        }
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
        BuildOutput = "Starting build...\n";
        try
        {
            var progress = new Progress<string>(message => BuildOutput += message + "\n");
            var result = await _buildService.RunBuildAsync(_entry, progress).ConfigureAwait(true);
            BuildResults.Insert(0, result);
            BuildOutput += $"\n{result.StatusDisplay} ({result.DurationDisplay})";
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
