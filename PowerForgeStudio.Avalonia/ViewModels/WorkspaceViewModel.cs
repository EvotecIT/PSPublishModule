using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Orchestrator.Activity;
using PowerForgeStudio.Orchestrator.Automation;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Connections;
using PowerForgeStudio.Orchestrator.Explorer;
using PowerForgeStudio.Orchestrator.Hub;
using PowerForgeStudio.Orchestrator.Packages;
using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Avalonia.ViewModels;

/// <summary>Coordinates workspace presentation over shared Studio services.</summary>
public sealed partial class WorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly IFileExplorerService _files;
    private readonly IFileRecoveryService _recovery;
    private readonly IWorkspaceRepositorySource _repositories;
    private readonly ProjectGitService _git = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<RepositoryCatalogEntry> _catalog = [];
    private readonly Dictionary<string, ExplorerNode> _projectNodes = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private int _selectionVersion;
    private bool _disposed;
    private bool _updatingFileList;
    private bool _updatingTreeSelection;
    private int _refreshVersion;

    public WorkspaceViewModel(string root, IWorkspaceExplorerStateStore? stateStore = null, IFileExplorerService? files = null, IWorkspaceRepositorySource? repositories = null, IGitHubProjectService? gitHub = null, ReleaseViewModel? release = null, IFileRecoveryService? recovery = null, IWorkspaceStorageInspectionService? storage = null, IWorkspaceStorageRemovalService? storageRemoval = null, IWorkspaceAutomationInventoryService? automations = null, IWorkspaceConnectionInventoryService? connections = null, IWorkspaceActivityInventoryService? activity = null, PowerForgeStudio.Orchestrator.Projects.IProjectOverviewService? overview = null, IProjectHistoryService? history = null, IGitHubProjectActionService? gitHubActions = null, IWorkspacePackageCatalogService? packages = null)
    {
        Release = release ?? new ReleaseViewModel(openPublicPackage: OpenPublicPackageAsync);
        Storage = new StorageViewModel(storage, storageRemoval, GetProtectedWorkingCopies);
        Automations = new AutomationsViewModel(automations);
        Connections = new ConnectionsViewModel(connections);
        Packages = new PackagesViewModel(packages);
        Activity = new ActivityViewModel(activity, OpenActivityReleaseAsync);
        Overview = new ProjectOverviewViewModel(overview);
        History = new ProjectHistoryViewModel(history);
        Settings = new SettingsViewModel(stateStore as IWorkspaceRootCatalogService, stateStore as IWorkspacePreferenceService);
        GitHub = new GitHubViewModel(gitHub, gitHubActions);
        WorkspaceRoot = Path.GetFullPath(root);
        _stateStore = stateStore;
        _files = files ?? new FileExplorerService();
        _recovery = recovery ?? new FileRecoveryService();
        _repositories = repositories ?? new WorkspaceRepositorySource();
        Changes.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(GitChangesViewModel.LastOperation)) OnPropertyChanged(nameof(DisplayedOutput));
            if (args.PropertyName == nameof(GitChangesViewModel.Snapshot) && Changes.Snapshot is { } snapshot)
            {
                UpdateGitDecorations(Changes.Root, snapshot);
                Branch = snapshot.BranchDisplay;
                GitSummary = !snapshot.IsGitRepository ? "Not a Git working copy" : snapshot.HasConflicts ? "Merge conflicts need resolution" : snapshot.StatusSummary;
                var selectedProject = _projectNodes.Values.FirstOrDefault(project => project.IsContextProject);
                var catalogEntry = selectedProject is null
                    ? null
                    : _catalog.FirstOrDefault(entry => SamePath(entry.RootPath, selectedProject.Path));
                if (catalogEntry is not null && SamePath(Changes.Root, ActiveWorkingCopyRoot))
                {
                    Overview.SetProject(catalogEntry, ActiveWorkingCopyRoot, snapshot);
                    if (IsOverviewPage) _ = Overview.RefreshAsync();
                }
            }
        };
        Release.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ReleaseViewModel.HasProtectedReleaseWork)) Build.IsReleaseRunning = Release.HasProtectedReleaseWork;
        };
        Build.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(BuildViewModel.BuildResult) or nameof(BuildViewModel.IsBuilding) or nameof(BuildViewModel.WasBuildCancelled))
                Release.SetBuild(Build.BuildResult, Build.IsBuilding, Build.WasBuildCancelled);
            if (args.PropertyName is nameof(BuildViewModel.BuildOutput) or nameof(BuildViewModel.HasBuild))
                OnPropertyChanged(nameof(DisplayedOutput));
        };
        Storage.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(StorageViewModel.Output)) OnPropertyChanged(nameof(DisplayedOutput));
            if (args.PropertyName == nameof(StorageViewModel.Status)) OnPropertyChanged(nameof(DisplayedStatus));
        };
        Automations.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AutomationsViewModel.Output)) OnPropertyChanged(nameof(DisplayedOutput));
            if (args.PropertyName == nameof(AutomationsViewModel.Status)) OnPropertyChanged(nameof(DisplayedStatus));
        };
        Connections.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ConnectionsViewModel.Output)) OnPropertyChanged(nameof(DisplayedOutput));
            if (args.PropertyName == nameof(ConnectionsViewModel.Status)) OnPropertyChanged(nameof(DisplayedStatus));
        };
        Packages.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(PackagesViewModel.Output)) OnPropertyChanged(nameof(DisplayedOutput));
            if (args.PropertyName == nameof(PackagesViewModel.Status)) OnPropertyChanged(nameof(DisplayedStatus));
        };
        Activity.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ActivityViewModel.Output)) OnPropertyChanged(nameof(DisplayedOutput));
            if (args.PropertyName == nameof(ActivityViewModel.Status)) OnPropertyChanged(nameof(DisplayedStatus));
            if (args.PropertyName == nameof(ActivityViewModel.IsGitHubFilter))
            {
                OnPropertyChanged(nameof(IsActivityRailSelected));
                OnPropertyChanged(nameof(IsGitHubActivityPage));
            }
        };
        Overview.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ProjectOverviewViewModel.Output)) OnPropertyChanged(nameof(DisplayedOutput));
            if (args.PropertyName == nameof(ProjectOverviewViewModel.Status)) OnPropertyChanged(nameof(DisplayedStatus));
        };
        History.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ProjectHistoryViewModel.Output)) OnPropertyChanged(nameof(DisplayedOutput));
            if (args.PropertyName == nameof(ProjectHistoryViewModel.Status)) OnPropertyChanged(nameof(DisplayedStatus));
            if (args.PropertyName == nameof(ProjectHistoryViewModel.DetailStatus) && IsHistoryPage) OnPropertyChanged(nameof(DisplayedStatus));
        };
        Settings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SettingsViewModel.Output)) OnPropertyChanged(nameof(DisplayedOutput));
            if (args.PropertyName == nameof(SettingsViewModel.Status)) OnPropertyChanged(nameof(DisplayedStatus));
            if (args.PropertyName == nameof(SettingsViewModel.SavedPreferences)) ApplyStudioPreferences(Settings.SavedPreferences);
        };
        Settings.SetWorkspace(WorkspaceRoot);
        ApplyStudioPreferences(Settings.SavedPreferences);
    }
    public ReleaseViewModel Release { get; }
    [ObservableProperty] private bool _isReleasePage;
    partial void OnIsReleasePageChanged(bool value) { OnPropertyChanged(nameof(IsFilesPage)); OnPropertyChanged(nameof(IsProjectRoute)); OnPropertyChanged(nameof(ShowGenericProjectContext)); OnPropertyChanged(nameof(OutputPaneHeight)); }
    [RelayCommand] private void ShowRelease() { IsOverviewPage = false; IsHistoryPage = false; IsSettingsPage = false; IsActivityPage = false; IsStoragePage = false; IsAutomationsPage = false; IsConnectionsPage = false; IsPackagesPage = false; IsBuildPage = false; IsChangesPage = false; IsGitHubPage = false; IsReleasePage = true; Release.SetHistoryScope(ActiveWorkingCopyRoot); }
    public GitHubViewModel GitHub { get; }
    [ObservableProperty] private bool _isGitHubPage;
    partial void OnIsGitHubPageChanged(bool value) { OnPropertyChanged(nameof(IsFilesPage)); OnPropertyChanged(nameof(IsProjectRoute)); OnPropertyChanged(nameof(ShowGenericProjectContext)); OnPropertyChanged(nameof(OutputPaneHeight)); }
    private bool _compactViewport;
    public bool CompactViewport
    {
        get => _compactViewport;
        set { if (SetProperty(ref _compactViewport, value)) OnPropertyChanged(nameof(OutputPaneHeight)); }
    }
    public global::Avalonia.Controls.GridLength OutputPaneHeight => new(IsGitHubPage || IsReleasePage ? 0 : IsHistoryPage || CompactViewport ? 110 : 170);
    [RelayCommand] private void ShowGitHub() { if (KeepReleaseVisible()) return; IsOverviewPage = false; IsHistoryPage = false; IsSettingsPage = false; IsActivityPage = false; IsStoragePage = false; IsAutomationsPage = false; IsConnectionsPage = false; IsPackagesPage = false; IsReleasePage = false; IsBuildPage = false; IsChangesPage = false; IsGitHubPage = true; }
    public BuildViewModel Build { get; } = new();
    public GitChangesViewModel Changes { get; } = new();
    [ObservableProperty] private bool _isChangesPage;
    public bool ShowGenericProjectContext => !IsOverviewPage && !IsChangesPage && !IsGitHubPage && !IsReleasePage && !IsBuildPage;
    partial void OnIsChangesPageChanged(bool value) { OnPropertyChanged(nameof(IsFilesPage)); OnPropertyChanged(nameof(IsProjectRoute)); OnPropertyChanged(nameof(ShowGenericProjectContext)); OnPropertyChanged(nameof(DisplayedOutput)); }
    [ObservableProperty] private bool _isBuildPage;
    public bool IsFilesPage => !IsOverviewPage && !IsHistoryPage && !IsSettingsPage && !IsActivityPage && !IsStoragePage && !IsAutomationsPage && !IsConnectionsPage && !IsPackagesPage && !IsBuildPage && !IsChangesPage && !IsGitHubPage && !IsReleasePage;
    partial void OnIsBuildPageChanged(bool value)
    {
        OnPropertyChanged(nameof(IsFilesPage));
        OnPropertyChanged(nameof(IsProjectRoute));
        OnPropertyChanged(nameof(ShowGenericProjectContext));
        OnPropertyChanged(nameof(DisplayedOutput));
    }
    public string DisplayedOutput => IsOverviewPage ? Overview.Output : IsHistoryPage ? History.Output : IsSettingsPage ? Settings.Output : IsActivityPage ? Activity.Output : IsStoragePage ? Storage.Output : IsAutomationsPage ? Automations.Output : IsConnectionsPage ? Connections.Output : IsPackagesPage ? Packages.Output : IsChangesPage ? Changes.LastOperation : IsBuildPage && Build.HasBuild ? Build.BuildOutput : Output;
    partial void OnOutputChanged(string value) => OnPropertyChanged(nameof(DisplayedOutput));
    [RelayCommand] private void ShowFiles() { if (KeepReleaseVisible()) return; IsOverviewPage = false; IsHistoryPage = false; IsSettingsPage = false; IsActivityPage = false; IsStoragePage = false; IsAutomationsPage = false; IsConnectionsPage = false; IsPackagesPage = false; IsReleasePage = false; IsGitHubPage = false; IsBuildPage = false; IsChangesPage = false; }
    [RelayCommand] private void ShowBuild() { if (KeepReleaseVisible()) return; IsOverviewPage = false; IsHistoryPage = false; IsSettingsPage = false; IsActivityPage = false; IsStoragePage = false; IsAutomationsPage = false; IsConnectionsPage = false; IsPackagesPage = false; IsReleasePage = false; IsGitHubPage = false; IsChangesPage = false; IsBuildPage = true; }
    [RelayCommand] private void ShowChanges() { if (KeepReleaseVisible()) return; IsOverviewPage = false; IsHistoryPage = false; IsSettingsPage = false; IsActivityPage = false; IsStoragePage = false; IsAutomationsPage = false; IsConnectionsPage = false; IsPackagesPage = false; IsReleasePage = false; IsGitHubPage = false; IsBuildPage = false; IsChangesPage = true; }

    private bool KeepReleaseVisible()
    {
        if (!Release.HasProtectedReleaseWork) return false;
        IsOverviewPage = false; IsHistoryPage = false; IsSettingsPage = false; IsActivityPage = false; IsStoragePage = false; IsAutomationsPage = false; IsConnectionsPage = false; IsPackagesPage = false; IsBuildPage = false; IsChangesPage = false; IsGitHubPage = false; IsReleasePage = true;
        Release.Status = Release.HasUnpersistedEvidence
            ? "Save or explicitly discard the unsaved receipts before leaving this release."
            : "Wait for the release operation to finish, or cancel it and retain its receipts before leaving.";
        return true;
    }
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
    public string SelectedFileRelativePath => SelectedFile is null || string.IsNullOrEmpty(ActiveWorkingCopyRoot)
        ? "" : Path.GetRelativePath(ActiveWorkingCopyRoot, SelectedFile.FullPath);
    public bool CanManageFiles => HasWorkingCopy && !IsSelectionLoading && !IsFileOperationRunning;

    partial void OnActiveWorkingCopyRootChanged(string value)
    {
        if (string.IsNullOrEmpty(value)) Overview.Clear();
        foreach (var project in _projectNodes.Values)
            project.IsContextProject = !string.IsNullOrEmpty(value) &&
                (SamePath(project.Path, value) || project.Children.Any(child => child.Children.Any(worktree =>
                    worktree.Kind == "branch" && !string.IsNullOrEmpty(worktree.Path) && SamePath(worktree.Path, value))));
        OnPropertyChanged(nameof(HasActiveProject));
        OnPropertyChanged(nameof(FavoriteActionLabel));
        Build.SetWorkingCopy(value);
        NotifyEditorChanged();
        Changes.SetWorkingCopy(value);
        History.SetWorkingCopy(value);
        GitHub.SetWorkingCopy(value);
        if (_sessionReady || !string.IsNullOrEmpty(value)) Release.SetHistoryScope(value);
        OnPropertyChanged(nameof(HasWorkingCopy));
        OnPropertyChanged(nameof(SelectedFileRelativePath));
        OnPropertyChanged(nameof(CanManageFiles));
    }
    partial void OnIsFileOperationRunningChanged(bool value) { OnPropertyChanged(nameof(CanManageFiles)); NotifyEditorChanged(); }
    partial void OnIsSelectionLoadingChanged(bool value) { OnPropertyChanged(nameof(CanManageFiles)); NotifyEditorChanged(); }
    partial void OnSelectedFileChanged(FileItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedFile));
        OnPropertyChanged(nameof(SelectedFileRelativePath));
        if (!_updatingFileList && value is not null && !value.IsDirectory) _ = OpenEntryAsync(value);
    }

    partial void OnFilterChanged(string value) => ApplyFilter();
    partial void OnSelectedNodeChanged(ExplorerNode? value)
    {
        if (!_updatingTreeSelection && value is not null) _ = SelectAsync(value);
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_loadedStateRoot is not null && !SamePath(_loadedStateRoot, WorkspaceRoot) && Documents.Any(document => document.IsDirty || document.IsBusy))
        {
            WorkspaceRoot = _loadedStateRoot;
            Status = "Save or discard open drafts before changing workspace.";
            return;
        }
        var refresh = ++_refreshVersion;
        try
        {
            CaptureSessionBeforeRefresh();
            _sessionReady = false;
            Status = "Discovering local repositories…";
            var selection = ++_selectionVersion;
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
            Storage.SetWorkspace(root);
            Automations.SetWorkspace(root);
            Connections.SetWorkspace(root);
            Activity.SetWorkspace(root);
            Settings.SetWorkspace(root);
            await LoadSessionAsync(root, refresh);
            if (_disposed || refresh != _refreshVersion) return;
            var found = await _repositories.DiscoverAsync(root, _lifetime.Token);
            if (_disposed || refresh != _refreshVersion) return;
            _catalog.Clear();
            _catalog.AddRange(found);
            _projectNodes.Clear();
            _gitSnapshots.Clear();
            ApplyFilter();
            RefreshQuickProjectMatches();
            RepositoryCount = $"{found.Count} repositories";
            Status = "Local discovery complete";
            AppendOutput($"Discovered {found.Count} repositories in {root}.");
            await RestoreSessionAsync(refresh, root, selection);
            if (!_disposed && refresh == _refreshVersion && SamePath(root, WorkspaceRoot))
            {
                _sessionReady = true;
                Release.SetHistoryScope(ActiveWorkingCopyRoot);
                await RefreshActiveUtilityPageAsync();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_disposed && refresh == _refreshVersion) Report(ex); }
    }

    private void ApplyFilter()
    {
        Projects.Clear();
        foreach (var entry in _catalog.Where(x => x.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase) && (!FavoritesOnly || _favorites.Contains(x.RootPath))))
        {
            if (!_projectNodes.TryGetValue(entry.RootPath, out var node))
                _projectNodes[entry.RootPath] = node = new ExplorerNode(entry.Name, entry.RootPath, "project", entry.RootPath, LoadProjectAsync);
            Projects.Add(node);
        }
        RebuildExplorerGroups();
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
        UpdateGitDecorations(node.Path, git);
    }

    private async Task LoadDirectoryAsync(ExplorerNode node)
    {
        var entries = await _files.ListDirectoryAsync(node.Path, _lifetime.Token);
        _lifetime.Token.ThrowIfCancellationRequested();
        var existing = node.Children.Where(x => x.Path.Length > 0)
            .ToDictionary(x => x.Path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        node.Children.Clear();
        foreach (var entry in node.Kind == "branch" ? OrderWorkingCopyRoot(entries) : entries)
            node.Children.Add(existing.TryGetValue(entry.FullPath, out var child) && child.Kind == Kind(entry) ? child
                : new ExplorerNode(entry.Name, entry.FullPath, Kind(entry), node.RepositoryRoot, entry.IsDirectory ? LoadDirectoryAsync : null));
        if (_gitSnapshots.TryGetValue(node.RepositoryRoot, out var snapshot)) UpdateGitDecorations(node.RepositoryRoot, snapshot);
    }

    private static IEnumerable<FileSystemEntry> OrderWorkingCopyRoot(IReadOnlyList<FileSystemEntry> entries)
        => entries.OrderBy(RootEntryRank).ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase);

    private static int RootEntryRank(FileSystemEntry entry)
    {
        if (!entry.IsDirectory) return 4;
        if (entry.Name.Equals("Build", StringComparison.OrdinalIgnoreCase)) return 0;
        if (entry.Name.Equals("Docs", StringComparison.OrdinalIgnoreCase)) return 1;
        if (entry.Name.Equals("artifacts", StringComparison.OrdinalIgnoreCase) ||
            entry.Name.Equals("artefacts", StringComparison.OrdinalIgnoreCase) ||
            entry.Name.Equals("_temp", StringComparison.OrdinalIgnoreCase) ||
            entry.Name.Equals("_reports", StringComparison.OrdinalIgnoreCase)) return 5;
        return entry.Name.StartsWith('.') ? 3 : 2;
    }

    public async Task SelectAsync(ExplorerNode node)
    {
        if (string.IsNullOrEmpty(node.Path)) return;
        var version = ++_selectionVersion;
        try
        {
            IsSelectionLoading = true;
            PreviewTitle = node.Name;
            Preview = "Loading…";
            _updatingTreeSelection = true;
            try { SelectedNode = LoadedNodes().FirstOrDefault(item => item.Path.Length > 0 && SamePath(item.Path, node.Path) && SamePath(item.RepositoryRoot, node.RepositoryRoot)); }
            finally { _updatingTreeSelection = false; }
            if (node.Kind is "project" or "branch" or "folder" || Directory.Exists(node.Path)) ActiveDocument = null;
            else OpenDocument(node);
            var directory = Directory.Exists(node.Path) ? node.Path : Path.GetDirectoryName(node.Path)!;
            if (string.IsNullOrEmpty(CurrentDirectory) || !SamePath(CurrentDirectory, directory) ||
                string.IsNullOrEmpty(ActiveWorkingCopyRoot) || !SamePath(ActiveWorkingCopyRoot, node.RepositoryRoot))
            {
                SelectedFile = null;
                Files.Clear();
            }
            SelectedPath = node.Path;
            ActiveWorkingCopyRoot = node.RepositoryRoot;
            ProjectName = _projectNodes.Values.FirstOrDefault(project => project.IsContextProject)?.Name ?? Path.GetFileName(node.RepositoryRoot);
            Status = "Loading selection…";
            var entries = await _files.ListDirectoryAsync(directory, _lifetime.Token);
            if (_disposed || version != _selectionVersion) return;
            ReplaceFiles(entries);
            _updatingFileList = true;
            try { SelectedFile = Files.FirstOrDefault(file => !file.IsDirectory && SamePath(file.FullPath, node.Path)); }
            finally { _updatingFileList = false; }
            CurrentDirectory = directory;
            PreviewTitle = node.Name;
            var preview = Directory.Exists(node.Path) ? $"{entries.Count} visible entries\n\n{node.Path}" : await _files.ReadTextPreviewAsync(node.Path, _lifetime.Token);
            if (_disposed || version != _selectionVersion) return;
            Preview = preview;
            var git = await _git.GetStatusAsync(node.RepositoryRoot, _lifetime.Token);
            if (_disposed || version != _selectionVersion) return;
            Branch = git.BranchDisplay;
            UpdateGitDecorations(node.RepositoryRoot, git);
            GitSummary = git.IsGitRepository ? git.StatusSummary : "Git status unavailable";
            var selectedProject = _projectNodes.Values.FirstOrDefault(project => project.IsContextProject);
            var catalogEntry = selectedProject is null
                ? null
                : _catalog.FirstOrDefault(entry => SamePath(entry.RootPath, selectedProject.Path));
            if (catalogEntry is not null)
            {
                Overview.SetProject(catalogEntry, node.RepositoryRoot, git);
                if (node.Kind is "project" or "branch") await ShowOverviewFromSelectionAsync();
                else ShowFiles();
            }
            Status = "Ready";
            await SaveSessionAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (version == _selectionVersion) { Preview = "Unable to load this selection: " + StudioDisplayError.From(ex); Report(ex); } }
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

    private void Report(Exception error) { var message = StudioDisplayError.From(error); Status = message; AppendOutput(message); }
    private void AppendOutput(string line)
    {
        var text = Output + $"\n[{DateTime.Now:HH:mm:ss}] {line}";
        Output = text.Length > 128 * 1024 ? text[^(128 * 1024)..] : text;
    }
    public void Dispose() { if (_disposed) return; _disposed = true; Build.Dispose(); Changes.Dispose(); History.Dispose(); GitHub.Dispose(); Release.Dispose(); Storage.Dispose(); Automations.Dispose(); Connections.Dispose(); Packages.Dispose(); Activity.Dispose(); Overview.Dispose(); _lifetime.Cancel(); _lifetime.Dispose(); }
}
