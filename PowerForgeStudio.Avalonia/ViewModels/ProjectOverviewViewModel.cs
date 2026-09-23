using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Domain.Projects;
using PowerForgeStudio.Orchestrator.Host;
using PowerForgeStudio.Orchestrator.Projects;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class ProjectOverviewViewModel : ObservableObject, IDisposable
{
    private readonly IProjectOverviewService _service;
    private readonly Func<ProjectOverviewItem, Task>? _openEntryPoint;
    private RepositoryCatalogEntry? _repository;
    private ProjectGitStatus? _git;
    private CancellationTokenSource? _inspection;
    private int _version;

    public ProjectOverviewViewModel(IProjectOverviewService? service = null, Func<ProjectOverviewItem, Task>? openEntryPoint = null)
    {
        _service = service ?? new ProjectOverviewService();
        _openEntryPoint = openEntryPoint;
    }

    public ObservableCollection<ProjectOverviewItem> Products { get; } = [];
    public ObservableCollection<ProjectOverviewItem> EntryPoints { get; } = [];
    public ObservableCollection<ProjectOverviewItem> Prerequisites { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];
    [ObservableProperty] private string _workingCopyRoot = "";
    [ObservableProperty] private string _projectName = "Select a project";
    [ObservableProperty] private string _projectKind = "Unclassified";
    [ObservableProperty] private string _workspaceKind = "No working copy selected";
    [ObservableProperty] private string _purpose = "Select a project or working copy in the tree to inspect it.";
    [ObservableProperty] private string _readmePath = "";
    [ObservableProperty] private string _branch = "-";
    [ObservableProperty] private string _gitState = "Not inspected";
    [ObservableProperty] private string _aheadBehind = "+0 / -0";
    [ObservableProperty] private int _workingCopyCount;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _status = "Select a project to open its overview.";
    [ObservableProperty] private string _output = "Project overview has not run.";
    public bool HasProject => _repository is not null;
    public bool HasGitWorkingCopy => _git?.IsGitRepository == true;
    public string WorkingCopyLabel => HasGitWorkingCopy ? "Working copy" : "Workspace type";
    public bool HasReadme => !string.IsNullOrWhiteSpace(ReadmePath);
    public bool HasWarnings => Warnings.Count > 0;

    partial void OnReadmePathChanged(string value)
    {
        OnPropertyChanged(nameof(HasReadme));
        OpenReadmeCommand.NotifyCanExecuteChanged();
    }

    public void SetProject(RepositoryCatalogEntry repository, string workingCopyRoot, ProjectGitStatus git)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingCopyRoot);
        ArgumentNullException.ThrowIfNull(git);
        var root = Path.GetFullPath(workingCopyRoot);
        if (_repository == repository && SamePath(root, WorkingCopyRoot))
        {
            _git = git;
            ApplyGit(git);
            return;
        }
        ++_version;
        _inspection?.Cancel();
        _repository = repository;
        _git = git;
        WorkingCopyRoot = root;
        ProjectName = repository.Name;
        ProjectKind = repository.RepositoryKind.ToString();
        WorkspaceKind = git.IsGitRepository ? repository.WorkspaceKind.ToString() : "Local project";
        ApplyGit(git);
        Purpose = "Refresh the overview to read bounded project metadata.";
        ReadmePath = "";
        Products.Clear(); EntryPoints.Clear(); Prerequisites.Clear(); Warnings.Clear();
        OnPropertyChanged(nameof(HasWarnings));
        IsLoading = false;
        Status = "Ready to inspect the selected project.";
        Output = "No project file, build script or configuration has been executed.";
        OnPropertyChanged(nameof(HasProject));
    }

    public void Clear()
    {
        ++_version;
        _inspection?.Cancel();
        _repository = null;
        _git = null;
        NotifyGitContext();
        WorkingCopyRoot = "";
        ProjectName = "Select a project";
        ProjectKind = "Unclassified";
        WorkspaceKind = "No working copy selected";
        Purpose = "Select a project or working copy in the tree to inspect it.";
        ReadmePath = "";
        Branch = "-"; GitState = "Not inspected"; AheadBehind = "+0 / -0"; WorkingCopyCount = 0;
        Products.Clear(); EntryPoints.Clear(); Prerequisites.Clear(); Warnings.Clear();
        OnPropertyChanged(nameof(HasWarnings));
        IsLoading = false;
        Status = "Select a project to open its overview.";
        Output = "Project overview has not run.";
        OnPropertyChanged(nameof(HasProject));
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsLoading || _repository is null || _git is null || string.IsNullOrWhiteSpace(WorkingCopyRoot)) return;
        var version = ++_version;
        var repository = _repository;
        var git = _git;
        var root = WorkingCopyRoot;
        _inspection?.Dispose();
        _inspection = new CancellationTokenSource();
        IsLoading = true;
        Status = "Reading bounded project metadata…";
        try
        {
            var snapshot = await _service.InspectAsync(repository, root, git, _inspection.Token);
            if (version != _version || !SamePath(root, WorkingCopyRoot)) return;
            Apply(snapshot);
        }
        catch (OperationCanceledException)
        {
            if (version == _version) Status = "Project overview inspection cancelled.";
        }
        catch (Exception ex)
        {
            if (version == _version)
            {
                Status = "Could not inspect the selected project.";
                Output = StudioDisplayError.From(ex);
            }
        }
        finally
        {
            if (version == _version) IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasReadme))]
    private void OpenReadme()
    {
        try
        {
            if (!IsSafeReadme(ReadmePath)) return;
            Process.Start(new ProcessStartInfo(ReadmePath) { UseShellExecute = true });
            Output = "Opened README.md with the configured desktop application.";
        }
        catch (Exception ex) { Output = "Could not open README.md: " + StudioDisplayError.From(ex); }
    }

    [RelayCommand]
    private async Task OpenEntryPointAsync(ProjectOverviewItem? item)
    {
        if (item?.SourcePath is null || _openEntryPoint is null ||
            !EntryPoints.Contains(item) || string.IsNullOrWhiteSpace(WorkingCopyRoot)) return;
        try { await _openEntryPoint(item); }
        catch (Exception ex) { Output = "Could not open project entrypoint: " + StudioDisplayError.From(ex); }
    }

    private void Apply(ProjectOverviewSnapshot snapshot)
    {
        ProjectName = snapshot.ProjectName;
        ProjectKind = snapshot.ProjectKind;
        WorkspaceKind = snapshot.WorkspaceKind;
        Purpose = snapshot.Purpose;
        ReadmePath = snapshot.ReadmePath ?? "";
        if (_git is { } currentGit) ApplyGit(currentGit);
        else
        {
            Branch = snapshot.Branch;
            GitState = snapshot.GitState;
            AheadBehind = snapshot.AheadBehind;
            WorkingCopyCount = snapshot.WorkingCopyCount;
        }
        Products.Clear(); foreach (var item in snapshot.Products) Products.Add(item);
        EntryPoints.Clear(); foreach (var item in snapshot.EntryPoints) EntryPoints.Add(item);
        Prerequisites.Clear(); foreach (var item in snapshot.Prerequisites) Prerequisites.Add(item);
        Warnings.Clear(); foreach (var warning in snapshot.Warnings) Warnings.Add(StudioOutputSanitizer.Sanitize(warning));
        OnPropertyChanged(nameof(HasWarnings));
        Status = $"Overview observed at {snapshot.InspectedAtUtc.LocalDateTime:t}.";
        Output = $"Observed {Products.Count} product signal(s), {EntryPoints.Count} entrypoint(s) and {Prerequisites.Count} prerequisite(s).\n" +
                 "No project file, build script or configuration was executed.";
    }

    private bool IsSafeReadme(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(WorkingCopyRoot) || !File.Exists(path)) return false;
        var root = Path.GetFullPath(WorkingCopyRoot);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return false;
        var relative = Path.GetRelativePath(root, Path.GetFullPath(path));
        if (relative == "." || Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return false;
        var current = root;
        foreach (var segment in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
        }
        return true;
    }

    private static bool SamePath(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private void ApplyGit(ProjectGitStatus git)
    {
        Branch = git.IsGitRepository ? git.BranchDisplay : "Local project";
        GitState = git.IsGitRepository ? git.StatusSummary : "No Git working copy";
        AheadBehind = git.AheadBehindDisplay;
        WorkingCopyCount = Math.Max(1, git.Worktrees.Count);
        NotifyGitContext();
    }

    private void NotifyGitContext()
    {
        OnPropertyChanged(nameof(HasGitWorkingCopy));
        OnPropertyChanged(nameof(WorkingCopyLabel));
    }

    public void Dispose()
    {
        ++_version;
        _inspection?.Cancel();
        _inspection?.Dispose();
    }
}
