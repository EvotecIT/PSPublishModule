using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Explorer;
using PowerForgeStudio.Orchestrator.Hub;

namespace PowerForgeStudio.Avalonia.ViewModels;

/// <summary>Coordinates workspace presentation over shared Studio services.</summary>
public sealed partial class WorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly FileExplorerService _files = new();
    private readonly ProjectGitService _git = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<RepositoryCatalogEntry> _catalog = [];
    private readonly Dictionary<string, ExplorerNode> _projectNodes = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private int _selectionVersion;
    private bool _disposed;
    private bool _updatingFileList;

    public WorkspaceViewModel(string root)
    {
        WorkspaceRoot = Path.GetFullPath(root);
        Changes.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(GitChangesViewModel.LastOperation)) OnPropertyChanged(nameof(DisplayedOutput));
            if (args.PropertyName == nameof(GitChangesViewModel.Snapshot) && Changes.Snapshot is { } snapshot)
            {
                Branch = snapshot.BranchDisplay;
                GitSummary = !snapshot.IsGitRepository ? "Not a Git working copy" : snapshot.HasConflicts ? "Merge conflicts need resolution" : snapshot.StatusSummary;
            }
        };
        Build.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(BuildViewModel.BuildOutput) or nameof(BuildViewModel.HasBuild))
                OnPropertyChanged(nameof(DisplayedOutput));
        };
    }
    public BuildViewModel Build { get; } = new();
    public GitChangesViewModel Changes { get; } = new();
    [ObservableProperty] private bool _isChangesPage;
    partial void OnIsChangesPageChanged(bool value) { OnPropertyChanged(nameof(IsFilesPage)); OnPropertyChanged(nameof(DisplayedOutput)); }
    [ObservableProperty] private bool _isBuildPage;
    public bool IsFilesPage => !IsBuildPage && !IsChangesPage;
    partial void OnIsBuildPageChanged(bool value)
    {
        OnPropertyChanged(nameof(IsFilesPage));
        OnPropertyChanged(nameof(DisplayedOutput));
    }
    public string DisplayedOutput => IsChangesPage ? Changes.LastOperation : IsBuildPage && Build.HasBuild ? Build.BuildOutput : Output;
    partial void OnOutputChanged(string value) => OnPropertyChanged(nameof(DisplayedOutput));
    [RelayCommand] private void ShowFiles() { IsBuildPage = false; IsChangesPage = false; }
    [RelayCommand] private void ShowBuild() { IsChangesPage = false; IsBuildPage = true; }
    [RelayCommand] private void ShowChanges() { IsBuildPage = false; IsChangesPage = true; }
    public ObservableCollection<ExplorerNode> Projects { get; } = [];
    public ObservableCollection<FileItemViewModel> Files { get; } = [];
    [ObservableProperty] private string _workspaceRoot;
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private string _status = "Ready";
    [ObservableProperty] private string _projectName = "Project workspace";
    [ObservableProperty] private string _selectedPath = "";
    [ObservableProperty] private string _branch = "";
    [ObservableProperty] private string _gitSummary = "Select a repository";
    [ObservableProperty] private string _preview = "Select a project to browse its files and working copies.";
    [ObservableProperty] private string _previewTitle = "Workspace";
    [ObservableProperty] private string _output = "PowerForge Studio · Avalonia\nNo commands executed.";
    [ObservableProperty] private string _repositoryCount = "No projects loaded";
    [ObservableProperty] private ExplorerNode? _selectedNode;
    [ObservableProperty] private string _activeWorkingCopyRoot = "";
    [ObservableProperty] private string _currentDirectory = "";
    [ObservableProperty] private FileItemViewModel? _selectedFile;
    [ObservableProperty] private bool _isFileOperationRunning;
    [ObservableProperty] private bool _isSelectionLoading;
    public bool HasWorkingCopy => !string.IsNullOrEmpty(ActiveWorkingCopyRoot);
    public bool HasSelectedFile => SelectedFile is not null;
    public bool CanManageFiles => HasWorkingCopy && !IsSelectionLoading && !IsFileOperationRunning;

    partial void OnActiveWorkingCopyRootChanged(string value)
    {
        Build.SetWorkingCopy(value);
        Changes.SetWorkingCopy(value);
        OnPropertyChanged(nameof(HasWorkingCopy));
        OnPropertyChanged(nameof(CanManageFiles));
    }
    partial void OnIsFileOperationRunningChanged(bool value) => OnPropertyChanged(nameof(CanManageFiles));
    partial void OnIsSelectionLoadingChanged(bool value) => OnPropertyChanged(nameof(CanManageFiles));
    partial void OnSelectedFileChanged(FileItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedFile));
        if (!_updatingFileList && value is not null && !value.IsDirectory) _ = OpenEntryAsync(value);
    }

    partial void OnFilterChanged(string value) => ApplyFilter();
    partial void OnSelectedNodeChanged(ExplorerNode? value)
    {
        if (value is not null) _ = SelectAsync(value);
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        try
        {
            Status = "Discovering local repositories…";
            ++_selectionVersion;
            SelectedNode = null;
            SelectedFile = null;
            ActiveWorkingCopyRoot = "";
            CurrentDirectory = "";
            SelectedPath = "";
            IsSelectionLoading = false;
            Files.Clear();
            PreviewTitle = "Workspace";
            Preview = "Select a project to browse its files and working copies.";
            var root = WorkspaceRoot;
            var found = await Task.Run(() =>
            {
                if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
                var scanner = new RepositoryCatalogScanner();
                return WorktreeDetector.IsGitRepository(root)
                    ? new[] { scanner.InspectRepository(root) }
                    : scanner.Scan(root).Where(x => WorktreeDetector.IsGitRepository(x.RootPath)).ToArray();
            }, _lifetime.Token);
            if (_disposed) return;
            _catalog.Clear();
            _catalog.AddRange(found);
            _projectNodes.Clear();
            ApplyFilter();
            RepositoryCount = $"{found.Length} repositories";
            Status = "Local discovery complete";
            AppendOutput($"Discovered {found.Length} repositories in {root}.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Report(ex); }
    }

    private void ApplyFilter()
    {
        Projects.Clear();
        foreach (var entry in _catalog.Where(x => x.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase)))
        {
            if (!_projectNodes.TryGetValue(entry.RootPath, out var node))
                _projectNodes[entry.RootPath] = node = new ExplorerNode(entry.Name, entry.RootPath, "project", entry.RootPath, LoadProjectAsync);
            Projects.Add(node);
        }
    }

    private async Task LoadProjectAsync(ExplorerNode node)
    {
        var git = await _git.GetStatusAsync(node.Path, _lifetime.Token);
        _lifetime.Token.ThrowIfCancellationRequested();
        node.Children.Clear();
        var primary = new ExplorerNode(git.BranchDisplay, node.Path, "branch", node.Path, LoadDirectoryAsync) { Detail = "primary" };
        node.Children.Add(primary);
        var linked = git.Worktrees.Where(x => !SamePath(x.Path, node.Path) && !x.IsBare).ToArray();
        if (linked.Length > 0)
        {
            var group = new ExplorerNode($"Worktrees ({linked.Length})", "", "branch", node.Path) { IsExpanded = true };
            foreach (var wt in linked)
                group.Children.Add(new ExplorerNode(wt.BranchDisplay, wt.Path, "branch", wt.Path, LoadDirectoryAsync) { Detail = wt.IsLocked ? "locked" : "" });
            node.Children.Add(group);
        }
        await primary.EnsureLoadedAsync();
        primary.IsExpanded = true;
    }

    private async Task LoadDirectoryAsync(ExplorerNode node)
    {
        var entries = await _files.ListDirectoryAsync(node.Path, _lifetime.Token);
        _lifetime.Token.ThrowIfCancellationRequested();
        var existing = node.Children.Where(x => x.Path.Length > 0)
            .ToDictionary(x => x.Path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        node.Children.Clear();
        foreach (var entry in entries)
            node.Children.Add(existing.TryGetValue(entry.FullPath, out var child) && child.Kind == Kind(entry) ? child
                : new ExplorerNode(entry.Name, entry.FullPath, Kind(entry), node.RepositoryRoot, entry.IsDirectory ? LoadDirectoryAsync : null));
    }

    public async Task SelectAsync(ExplorerNode node)
    {
        if (string.IsNullOrEmpty(node.Path)) return;
        var version = ++_selectionVersion;
        try
        {
            IsSelectionLoading = true;
            var directory = Directory.Exists(node.Path) ? node.Path : Path.GetDirectoryName(node.Path)!;
            if (string.IsNullOrEmpty(CurrentDirectory) || !SamePath(CurrentDirectory, directory) ||
                string.IsNullOrEmpty(ActiveWorkingCopyRoot) || !SamePath(ActiveWorkingCopyRoot, node.RepositoryRoot))
            {
                SelectedFile = null;
                Files.Clear();
            }
            SelectedPath = node.Path;
            ActiveWorkingCopyRoot = node.RepositoryRoot;
            ProjectName = Path.GetFileName(node.RepositoryRoot);
            Status = "Loading selection…";
            var entries = await _files.ListDirectoryAsync(directory, _lifetime.Token);
            if (_disposed || version != _selectionVersion) return;
            ReplaceFiles(entries);
            CurrentDirectory = directory;
            PreviewTitle = node.Name;
            var preview = Directory.Exists(node.Path) ? $"{entries.Count} visible entries\n\n{node.Path}" : await _files.ReadTextPreviewAsync(node.Path, _lifetime.Token);
            if (_disposed || version != _selectionVersion) return;
            Preview = preview;
            var git = await _git.GetStatusAsync(node.RepositoryRoot, _lifetime.Token);
            if (_disposed || version != _selectionVersion) return;
            Branch = git.BranchDisplay;
            GitSummary = git.IsGitRepository ? git.StatusSummary : "Git status unavailable";
            Status = "Ready";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (version == _selectionVersion) Report(ex); }
        finally { if (version == _selectionVersion) IsSelectionLoading = false; }
    }

    public Task OpenEntryAsync(FileItemViewModel entry)
    {
        var root = ActiveWorkingCopyRoot;
        if (string.IsNullOrEmpty(root)) return Task.CompletedTask;
        return SelectAsync(new ExplorerNode(entry.Name, entry.FullPath, entry.IconKind, root));
    }

    private void ReplaceFiles(IReadOnlyList<FileSystemEntry> entries)
    {
        var selected = SelectedFile?.FullPath;
        _updatingFileList = true;
        try
        {
            Files.Clear();
            foreach (var entry in entries) Files.Add(new FileItemViewModel(entry));
            SelectedFile = selected is null ? null : Files.FirstOrDefault(x => SamePath(x.FullPath, selected));
        }
        finally { _updatingFileList = false; }
    }

    private static string Kind(FileSystemEntry entry) => entry.IsDirectory ? "folder" : entry.Extension.ToLowerInvariant() switch
    {
        ".ps1" or ".psm1" => "script", ".json" => "json", ".md" => "markdown", ".sln" or ".slnx" => "solution", _ => "file"
    };

    private static bool SamePath(string first, string second) => string.Equals(Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar),
        Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private void Report(Exception error) { Status = error.Message; AppendOutput(error.Message); }
    private void AppendOutput(string line)
    {
        var text = Output + $"\n[{DateTime.Now:HH:mm:ss}] {line}";
        Output = text.Length > 128 * 1024 ? text[^(128 * 1024)..] : text;
    }
    public void Dispose() { if (_disposed) return; _disposed = true; Build.Dispose(); Changes.Dispose(); _lifetime.Cancel(); _lifetime.Dispose(); }
}
