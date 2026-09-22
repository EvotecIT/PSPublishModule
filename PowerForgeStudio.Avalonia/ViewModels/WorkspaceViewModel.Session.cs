using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class WorkspaceViewModel
{
    private readonly IWorkspaceExplorerStateStore? _stateStore;
    private readonly HashSet<string> _favorites = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly SemaphoreSlim _sessionSave = new(1, 1);
    private string? _loadedStateRoot;
    private int _restoringSession;
    private bool _sessionReady;
    private WorkspaceExplorerState? _pendingRestore;
    private bool _restoreDocumentsFromDisk;
    public ObservableCollection<ExplorerNode> ExplorerRoots { get; } = [];
    public ObservableCollection<WorkspaceDocumentViewModel> Documents { get; } = [];
    [ObservableProperty] private WorkspaceDocumentViewModel? _activeDocument;
    [ObservableProperty] private bool _favoritesOnly;
    [ObservableProperty] private string _stateError = "";
    public bool AllProjects => !FavoritesOnly && !ChangedProjectsOnly;
    public bool IsWorkspaceTab => ActiveDocument is null;
    public bool HasStateError => !string.IsNullOrEmpty(StateError);
    public bool HasActiveProject => _projectNodes.Values.Any(project => project.IsContextProject);
    public string FavoriteActionLabel => _projectNodes.Values.FirstOrDefault(project => project.IsContextProject) is { } project && _favorites.Contains(project.Path)
        ? "Remove favorite" : "Add favorite";
    public string DisplayedStatus => HasStateError ? StateError : IsOverviewPage ? Overview.Status : IsHistoryPage ? History.DetailStatus : IsSettingsPage ? Settings.Status : IsActivityPage ? Activity.Status : IsStoragePage ? Storage.Status : IsAutomationsPage ? Automations.Status : IsConnectionsPage ? Connections.Status : IsPackagesPage ? Packages.Status : Status;
    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(DisplayedStatus));
    partial void OnStateErrorChanged(string value) { OnPropertyChanged(nameof(HasStateError)); OnPropertyChanged(nameof(DisplayedStatus)); }
    partial void OnFavoritesOnlyChanged(bool value) { if (value) ChangedProjectsOnly = false; OnPropertyChanged(nameof(AllProjects)); ApplyFilter(); }
    partial void OnActiveDocumentChanged(WorkspaceDocumentViewModel? value)
    {
        OnPropertyChanged(nameof(IsWorkspaceTab));
        foreach (var document in Documents) document.IsActive = ReferenceEquals(document, value);
        NotifyEditorChanged();
    }
    [RelayCommand] private void ShowAllProjects() { FavoritesOnly = false; ChangedProjectsOnly = false; }
    [RelayCommand] private void ShowFavoriteProjects() { ChangedProjectsOnly = false; FavoritesOnly = true; }

    [RelayCommand]
    private async Task ShowWorkspaceAsync()
    {
        ShowFiles();
        if (HasWorkingCopy) await SelectAsync(new ExplorerNode(ProjectName, ActiveWorkingCopyRoot, "branch", ActiveWorkingCopyRoot));
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        var project = _projectNodes.Values.FirstOrDefault(item => item.IsContextProject);
        if (project is null) return;
        var root = WorkspaceRoot;
        var favorite = !_favorites.Contains(project.Path);
        try
        {
            if (_stateStore is not null) await Task.Run(() => _stateStore.SetFavorite(root, project.Path, favorite));
            if (_disposed || !SamePath(root, WorkspaceRoot)) return;
            if (favorite) _favorites.Add(project.Path); else _favorites.Remove(project.Path);
            StateError = "";
            ApplyFilter();
            OnPropertyChanged(nameof(FavoriteActionLabel));
        }
        catch (Exception ex) { StateError = "Could not save favorite: " + StudioDisplayError.From(ex); }
    }

    private async Task LoadSessionAsync(string root, int refresh)
    {
        if (_loadedStateRoot is not null && SamePath(_loadedStateRoot, root)) return;
        _loadedStateRoot = null;
        Documents.Clear(); ActiveDocument = null; _favorites.Clear(); _pendingRestore = null;
        StateError = "";
        if (_stateStore is null) { _loadedStateRoot = root; return; }
        try
        {
            var state = await Task.Run(() => _stateStore.LoadExplorer(root));
            if (_disposed || refresh != _refreshVersion || !SamePath(root, WorkspaceRoot)) return;
            _loadedStateRoot = root;
            _pendingRestore = state;
            _restoreDocumentsFromDisk = _restoreOpenDocuments;
            foreach (var favorite in state.FavoriteProjectRoots) _favorites.Add(Path.GetFullPath(favorite));
        }
        catch (Exception ex)
        {
            if (!_disposed && refresh == _refreshVersion && SamePath(root, WorkspaceRoot))
            {
                _loadedStateRoot = root;
                StateError = "Could not read saved workspace state: " + StudioDisplayError.From(ex);
            }
        }
    }

    private async Task RestoreSessionAsync(int refresh, string root, int selection)
    {
        var state = _pendingRestore;
        if (state is null) return;
        ++_restoringSession;
        try
        {
            var restoreDocuments = _restoreDocumentsFromDisk;
            if (restoreDocuments)
                foreach (var reference in state.OpenDocuments)
                    if (!Documents.Any(document => SameDocument(document.Reference, reference))) Documents.Add(CreateDocument(reference));
            _restoreDocumentsFromDisk = false;
            var expanded = new HashSet<string>(state.ExpandedPaths, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var project in _projectNodes.Values.ToArray())
            {
                await RestoreExpandedAsync(project, expanded);
                if (_disposed || refresh != _refreshVersion || !SamePath(root, WorkspaceRoot)) return;
            }
            if (selection == _selectionVersion)
            {
                var selected = ActiveDocument ?? (restoreDocuments && state.ActiveDocument is { } active
                    ? Documents.FirstOrDefault(document => SameDocument(document.Reference, active)) : null);
                if (selected is not null) await SelectDocumentAsync(selected);
            }
            if (ReferenceEquals(_pendingRestore, state)) _pendingRestore = null;
        }
        finally { --_restoringSession; }
    }

    private static async Task RestoreExpandedAsync(ExplorerNode node, HashSet<string> expanded)
    {
        if (!string.IsNullOrEmpty(node.Path) && !expanded.Contains(Path.GetFullPath(node.Path))) return;
        await node.EnsureLoadedAsync(); node.IsExpanded = true;
        foreach (var child in node.Children.ToArray())
            if (child.Kind is "project" or "branch" or "folder") await RestoreExpandedAsync(child, expanded);
    }

    private void OpenDocument(ExplorerNode node)
    {
        var reference = new WorkspaceDocumentReference(Path.GetFullPath(node.RepositoryRoot), Path.GetFullPath(node.Path));
        var document = Documents.FirstOrDefault(item => SameDocument(item.Reference, reference));
        if (document is null) { document = CreateDocument(reference); Documents.Add(document); }
        ActiveDocument = document;
    }

    [RelayCommand]
    private async Task SelectDocumentAsync(WorkspaceDocumentViewModel document)
    {
        if (!Documents.Contains(document)) return;
        ActiveDocument = document;
        ShowFiles();
        var reference = document.Reference;
        await SelectAsync(new ExplorerNode(document.Name, reference.Path, "file", reference.WorkingCopyRoot));
    }

    [RelayCommand]
    private async Task CloseDocumentAsync(WorkspaceDocumentViewModel document)
    {
        if (!await ResolveUnsavedDocumentsAsync([document])) return;
        var wasActive = ReferenceEquals(document, ActiveDocument);
        Documents.Remove(document);
        if (wasActive)
        {
            ActiveDocument = null;
            if (Documents.LastOrDefault() is { } next) await SelectDocumentAsync(next);
            else
            {
                ++_selectionVersion;
                IsSelectionLoading = false;
                Preview = "Select a file to open a document.";
                PreviewTitle = "Workspace";
                SelectedFile = null;
                SelectedNode = null;
                SelectedPath = CurrentDirectory;
            }
        }
        await SaveSessionAsync();
    }

    [RelayCommand]
    public async Task SaveSessionAsync()
    {
        if (_stateStore is null || !_sessionReady || _restoringSession > 0 || _loadedStateRoot is null || _disposed || !SamePath(_loadedStateRoot, WorkspaceRoot)) return;
        var root = WorkspaceRoot;
        var documents = Documents.Select(document => document.Reference).ToArray();
        var active = ActiveDocument?.Reference;
        var expanded = LoadedNodes().Where(node => node.IsExpanded && !string.IsNullOrEmpty(node.Path)).Select(node => Path.GetFullPath(node.Path)).ToArray();
        await _sessionSave.WaitAsync();
        try
        {
            await Task.Run(() => _stateStore.SaveSession(root, documents, active, expanded));
            if (SamePath(root, WorkspaceRoot)) StateError = "";
        }
        catch (Exception ex) { if (SamePath(root, WorkspaceRoot)) StateError = "Could not save workspace state: " + StudioDisplayError.From(ex); }
        finally { _sessionSave.Release(); }
    }

    private static bool SameDocument(WorkspaceDocumentReference first, WorkspaceDocumentReference second)
        => SamePath(first.WorkingCopyRoot, second.WorkingCopyRoot) && SamePath(first.Path, second.Path);

    private void CaptureSessionBeforeRefresh()
    {
        if (!_sessionReady || _loadedStateRoot is null || !SamePath(_loadedStateRoot, WorkspaceRoot)) return;
        _restoreDocumentsFromDisk = false;
        _pendingRestore = new WorkspaceExplorerState(WorkspaceRoot, _favorites.ToArray(), Documents.Select(document => document.Reference).ToArray(),
            ActiveDocument?.Reference, LoadedNodes().Where(node => node.IsExpanded && node.Path.Length > 0).Select(node => Path.GetFullPath(node.Path)).ToArray());
    }

    public async Task<bool> ActivateWorkspaceAsync(string root)
    {
        if (!await ResolveUnsavedDocumentsAsync()) return false;
        try
        {
            if (_stateStore is IWorkspaceRootCatalogService catalog) await Task.Run(() => catalog.SaveActive(root));
            return true;
        }
        catch (Exception ex) { StateError = "Could not save the active workspace: " + StudioDisplayError.From(ex); return false; }
    }

    private void RebuildExplorerGroups()
    {
        var selected = SelectedNode;
        ExplorerRoots.Clear();
        var favorites = Projects.Where(project => _favorites.Contains(project.Path)).ToArray();
        var favoriteGroup = new ExplorerNode("Favorites", "", "favorite", WorkspaceRoot) { IsExpanded = true, Detail = favorites.Length == 0 ? ChangedProjectsOnly && _hasProjectChangeSnapshot ? "No changed favorites" : "No favorites" : "" };
        foreach (var project in favorites) favoriteGroup.Children.Add(project);
        ExplorerRoots.Add(favoriteGroup);
        var others = Projects.Except(favorites).ToArray();
        if (others.Length > 0)
        {
            var group = new ExplorerNode("Other projects", "", "project", WorkspaceRoot) { IsExpanded = true };
            foreach (var project in others) group.Children.Add(project);
            ExplorerRoots.Add(group);
        }
        if (selected is not null && LoadedNodes().Contains(selected)) SelectedNode = selected;
    }
}
