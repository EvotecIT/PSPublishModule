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
    private int _selectionVersion;
    private bool _disposed;

    public WorkspaceViewModel(string root) => WorkspaceRoot = Path.GetFullPath(root);
    public ObservableCollection<ExplorerNode> Projects { get; } = [];
    public ObservableCollection<FileSystemEntry> Files { get; } = [];
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
            Projects.Add(new ExplorerNode(entry.Name, entry.RootPath, "project", entry.RootPath, LoadProjectAsync));
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
        node.Children.Clear();
        foreach (var entry in entries)
            node.Children.Add(new ExplorerNode(entry.Name, entry.FullPath, Kind(entry), node.RepositoryRoot, entry.IsDirectory ? LoadDirectoryAsync : null));
    }

    public async Task SelectAsync(ExplorerNode node)
    {
        if (string.IsNullOrEmpty(node.Path)) return;
        var version = ++_selectionVersion;
        try
        {
            SelectedPath = node.Path;
            ProjectName = Path.GetFileName(node.RepositoryRoot);
            Status = "Loading selection…";
            var entries = await _files.ListDirectoryAsync(Directory.Exists(node.Path) ? node.Path : Path.GetDirectoryName(node.Path)!, _lifetime.Token);
            if (_disposed || version != _selectionVersion) return;
            Files.Clear();
            foreach (var entry in entries) Files.Add(entry);
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
    }

    public Task OpenEntryAsync(FileSystemEntry entry)
    {
        var root = SelectedNode?.RepositoryRoot ?? SelectedPath;
        return SelectAsync(new ExplorerNode(entry.Name, entry.FullPath, Kind(entry), root));
    }

    private static string Kind(FileSystemEntry entry) => entry.IsDirectory ? "folder" : entry.Extension.ToLowerInvariant() switch
    {
        ".ps1" or ".psm1" => "script", ".json" => "json", ".md" => "markdown", ".sln" or ".slnx" => "solution", _ => "file"
    };

    private static bool SamePath(string first, string second) => string.Equals(Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar),
        Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private void Report(Exception error) { Status = error.Message; AppendOutput(error.Message); }
    private void AppendOutput(string line) => Output += $"\n[{DateTime.Now:HH:mm:ss}] {line}";
    public void Dispose() { if (_disposed) return; _disposed = true; _lifetime.Cancel(); _lifetime.Dispose(); }
}
