using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class WorkspaceViewModel
{
    private readonly IWorkspaceProjectChangeService _projectChanges;
    private readonly HashSet<string> _changedProjectRoots = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private CancellationTokenSource? _projectChangeRefresh;
    private int _projectChangeVersion;
    private int _projectCatalogGeneration;
    private string _projectCatalogRoot = "";
    private bool _isProjectCatalogRefreshing;
    private bool _hasProjectChangeSnapshot;

    [ObservableProperty] private bool _changedProjectsOnly;
    [ObservableProperty] private bool _isProjectChangeLoading;
    [ObservableProperty] private string _projectChangeStatus = "";
    public bool HasProjectChangeStatus => ProjectChangeStatus.Length > 0 && (ChangedProjectsOnly || IsProjectChangeLoading);
    public bool CanCancelProjectChangeRefresh => IsProjectChangeLoading;
    public bool CanShowChangedProjects => !_isProjectCatalogRefreshing &&
                                          _catalog.Any(static entry => WorktreeDetector.IsGitRepository(entry.RootPath)) &&
                                          CatalogMatchesWorkspace();

    partial void OnChangedProjectsOnlyChanged(bool value)
    {
        if (value) FavoritesOnly = false;
        OnPropertyChanged(nameof(AllProjects));
        OnPropertyChanged(nameof(HasProjectChangeStatus));
        ApplyFilter();
    }

    partial void OnIsProjectChangeLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCancelProjectChangeRefresh));
        OnPropertyChanged(nameof(HasProjectChangeStatus));
    }
    partial void OnProjectChangeStatusChanged(string value) => OnPropertyChanged(nameof(HasProjectChangeStatus));

    [RelayCommand]
    private async Task ShowChangedProjectsAsync()
    {
        if (!CanShowChangedProjects) return;
        FavoritesOnly = false;
        ChangedProjectsOnly = true;
        if (IsProjectChangeLoading) return;
        var version = ++_projectChangeVersion;
        var catalogGeneration = _projectCatalogGeneration;
        var root = WorkspaceRoot;
        var catalog = _catalog.ToArray();
        _projectChangeRefresh?.Dispose();
        using var refresh = new CancellationTokenSource();
        _projectChangeRefresh = refresh;
        IsProjectChangeLoading = true;
        ProjectChangeStatus = _hasProjectChangeSnapshot
            ? "Refreshing local Git changes; the previous observation remains visible."
            : "Checking local Git changes across this workspace…";
        try
        {
            var progress = new Progress<PowerForgeStudio.Domain.Workspace.WorkspaceProjectChangeProgress>(item => Dispatcher.UIThread.Post(() =>
            {
                if (_disposed || version != _projectChangeVersion || catalogGeneration != _projectCatalogGeneration ||
                    !IsProjectChangeLoading || !SamePath(root, WorkspaceRoot)) return;
                ProjectChangeStatus = $"Checking {item.CurrentProject} · {item.CompletedProjects}/{item.TotalProjects} projects";
            }));
            var snapshot = await _projectChanges.InspectAsync(catalog, progress, refresh.Token);
            if (_disposed || version != _projectChangeVersion || catalogGeneration != _projectCatalogGeneration ||
                !SamePath(root, WorkspaceRoot) || !CatalogMatchesWorkspace()) return;
            _changedProjectRoots.Clear();
            foreach (var projectRoot in snapshot.ChangedProjects.Keys) _changedProjectRoots.Add(Path.GetFullPath(projectRoot));
            _hasProjectChangeSnapshot = true;
            ApplyFilter();
            var workingCopies = snapshot.ChangedProjects.Values.Sum(static copies => copies.Count);
            ProjectChangeStatus = $"{CountLabel(snapshot.ChangedProjects.Count, "changed project", "changed projects")}, " +
                                  $"{CountLabel(workingCopies, "changed working copy", "changed working copies")}. " +
                                  $"Read {snapshot.ObservedAtUtc:yyyy-MM-dd HH:mm} UTC." +
                                  (snapshot.UnavailableWorkingCopies.Count > 0
                                      ? $" Local Git was unavailable for {CountLabel(snapshot.UnavailableWorkingCopies.Count, "working copy", "working copies")}."
                                      : "");
        }
        catch (OperationCanceledException)
        {
            if (!_disposed && version == _projectChangeVersion)
                ProjectChangeStatus = _hasProjectChangeSnapshot
                    ? "Change refresh cancelled; the previous observation remains visible."
                    : "Change scan cancelled; showing all projects until a scan completes.";
        }
        catch (Exception ex)
        {
            if (!_disposed && version == _projectChangeVersion)
                ProjectChangeStatus = "Change scan unavailable: " + StudioDisplayError.From(ex) +
                                      (_hasProjectChangeSnapshot ? " The previous observation remains visible." : " Showing all projects.");
        }
        finally
        {
            if (ReferenceEquals(_projectChangeRefresh, refresh)) _projectChangeRefresh = null;
            if (!_disposed && version == _projectChangeVersion) IsProjectChangeLoading = false;
        }
    }

    [RelayCommand]
    private void CancelProjectChangeRefresh()
    {
        if (!IsProjectChangeLoading) return;
        ProjectChangeStatus = "Cancelling local Git change scan…";
        _projectChangeRefresh?.Cancel();
    }

    private void ResetProjectChangeInventory()
    {
        ++_projectChangeVersion;
        _projectChangeRefresh?.Cancel();
        _projectChangeRefresh?.Dispose();
        _projectChangeRefresh = null;
        _changedProjectRoots.Clear();
        _hasProjectChangeSnapshot = false;
        IsProjectChangeLoading = false;
        ChangedProjectsOnly = false;
        ProjectChangeStatus = "";
    }

    private void BeginProjectCatalogRefresh(string root)
    {
        ResetProjectChangeInventory();
        _isProjectCatalogRefreshing = true;
        if (!CatalogMatches(root))
        {
            _catalog.Clear();
            _projectNodes.Clear();
            _gitSnapshots.Clear();
            ApplyFilter();
            RefreshQuickProjectMatches();
            RepositoryCount = "Discovering repositories";
        }
        OnPropertyChanged(nameof(CanShowChangedProjects));
    }

    private void CompleteProjectCatalogRefresh(string root)
    {
        _projectCatalogRoot = Path.GetFullPath(root);
        ++_projectCatalogGeneration;
        _isProjectCatalogRefreshing = false;
        OnPropertyChanged(nameof(CanShowChangedProjects));
    }

    private void EndProjectCatalogRefresh(string root, int refresh)
    {
        if (_disposed || refresh != _refreshVersion || !SamePath(root, WorkspaceRoot)) return;
        _isProjectCatalogRefreshing = false;
        OnPropertyChanged(nameof(CanShowChangedProjects));
    }

    private bool CatalogMatchesWorkspace() => CatalogMatches(WorkspaceRoot);

    private bool CatalogMatches(string root) => _projectCatalogRoot.Length > 0 && SamePath(_projectCatalogRoot, root);

    private void DisposeProjectChangeInventory()
    {
        ++_projectChangeVersion;
        _projectChangeRefresh?.Cancel();
        _projectChangeRefresh?.Dispose();
        _projectChangeRefresh = null;
    }

    private static string CountLabel(int count, string singular, string plural) => $"{count} {(count == 1 ? singular : plural)}";
}
