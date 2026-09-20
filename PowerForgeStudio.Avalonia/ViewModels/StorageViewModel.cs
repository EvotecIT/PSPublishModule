using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class StorageViewModel : ObservableObject, IDisposable
{
    private readonly IWorkspaceStorageInspectionService _inspection;
    private readonly List<WorkspaceStorageEntry> _allEntries = [];
    private CancellationTokenSource? _refreshCancellation;
    private int _refreshVersion;

    public StorageViewModel(IWorkspaceStorageInspectionService? inspection = null)
        => _inspection = inspection ?? new WorkspaceStorageInspectionService();

    public ObservableCollection<WorkspaceStorageEntry> Entries { get; } = [];
    [ObservableProperty] private WorkspaceStorageEntry? _selectedEntry;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _status = "Open Storage to inspect local working copies.";
    [ObservableProperty] private string _output = "Storage inspection has not run.";
    [ObservableProperty] private string _indexedDisplay = "—";
    [ObservableProperty] private string _worktreeDisplay = "—";
    [ObservableProperty] private int _candidateCount;
    [ObservableProperty] private string _filter = "All";
    public string WorkspaceRoot { get; private set; } = "";
    public bool HasEntries => Entries.Count > 0;
    public bool HasSelection => SelectedEntry is not null;
    public bool IsAllFilter => Filter == "All";
    public bool IsCandidatesFilter => Filter == "Candidates";
    public bool IsChangedFilter => Filter == "Changed";
    public bool IsBrokenFilter => Filter == "Broken";

    partial void OnSelectedEntryChanged(WorkspaceStorageEntry? value) => OnPropertyChanged(nameof(HasSelection));
    partial void OnFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsAllFilter));
        OnPropertyChanged(nameof(IsCandidatesFilter));
        OnPropertyChanged(nameof(IsChangedFilter));
        OnPropertyChanged(nameof(IsBrokenFilter));
        ApplyFilter();
    }

    public void SetWorkspace(string root)
    {
        var full = Path.GetFullPath(root);
        if (string.Equals(full, WorkspaceRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return;
        _refreshVersion++;
        _refreshCancellation?.Cancel();
        IsLoading = false;
        WorkspaceRoot = full;
        _allEntries.Clear();
        Entries.Clear();
        SelectedEntry = null;
        IndexedDisplay = "—";
        WorktreeDisplay = "—";
        CandidateCount = 0;
        Status = "Ready to inspect workspace storage.";
        Output = "Storage inspection has not run for this workspace.";
        OnPropertyChanged(nameof(HasEntries));
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsLoading || string.IsNullOrWhiteSpace(WorkspaceRoot))
            return;
        var version = ++_refreshVersion;
        var root = WorkspaceRoot;
        _refreshCancellation?.Dispose();
        _refreshCancellation = new CancellationTokenSource();
        IsLoading = true;
        Status = "Measuring repositories and registered worktrees…";
        try
        {
            var snapshot = await _inspection.InspectAsync(root, _refreshCancellation.Token);
            if (version != _refreshVersion || !string.Equals(root, WorkspaceRoot,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                return;
            _allEntries.Clear();
            _allEntries.AddRange(snapshot.Entries);
            IndexedDisplay = snapshot.IndexedDisplay;
            WorktreeDisplay = snapshot.WorktreeDisplay;
            CandidateCount = snapshot.ReviewCandidateCount;
            ApplyFilter();
            Status = $"Inspected {snapshot.Entries.Count} working copies. No files were removed.";
            Output = $"[{snapshot.InspectedAtUtc:HH:mm:ss}] Storage inspection complete — {snapshot.Entries.Count} working copies, " +
                     $"{snapshot.IndexedDisplay} indexed, {snapshot.WorktreeDisplay} in worktrees, {snapshot.ReviewCandidateCount} review candidate(s).\n" +
                     "Candidate status is local evidence only. Remote PR, active-use and retained-artifact checks are still required before removal.";
        }
        catch (OperationCanceledException)
        {
            if (version == _refreshVersion)
                Status = "Storage inspection cancelled.";
        }
        catch (Exception ex)
        {
            if (version == _refreshVersion)
            {
                Status = "Storage inspection failed.";
                Output = ex.Message;
            }
        }
        finally
        {
            if (version == _refreshVersion)
                IsLoading = false;
        }
    }

    [RelayCommand] private void ShowAll() => Filter = "All";
    [RelayCommand] private void ShowCandidates() => Filter = "Candidates";
    [RelayCommand] private void ShowChanged() => Filter = "Changed";
    [RelayCommand] private void ShowBroken() => Filter = "Broken";

    private void ApplyFilter()
    {
        var selectedPath = SelectedEntry?.Path;
        var visible = Filter switch
        {
            "Candidates" => _allEntries.Where(static entry => entry.IsReviewCandidate),
            "Changed" => _allEntries.Where(static entry => entry.IsChanged),
            "Broken" => _allEntries.Where(static entry => entry.IsBroken),
            _ => _allEntries
        };
        Entries.Clear();
        foreach (var entry in visible)
            Entries.Add(entry);
        SelectedEntry = Entries.FirstOrDefault(entry => string.Equals(entry.Path, selectedPath,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) ?? Entries.FirstOrDefault();
        OnPropertyChanged(nameof(HasEntries));
    }

    public void Dispose()
    {
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
    }
}
