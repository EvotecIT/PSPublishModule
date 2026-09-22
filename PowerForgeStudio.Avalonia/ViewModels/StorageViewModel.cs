using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class StorageViewModel : ObservableObject, IDisposable
{
    private readonly IWorkspaceStorageInspectionService _inspection;
    private readonly IWorkspaceStorageRemovalService _removal;
    private readonly bool _ownsRemoval;
    private readonly Func<IReadOnlyCollection<string>> _protectedWorkingCopies;
    private readonly List<WorkspaceStorageEntry> _allEntries = [];
    private CancellationTokenSource? _refreshCancellation;
    private int _refreshVersion;
    private int _reviewVersion;

    public StorageViewModel(
        IWorkspaceStorageInspectionService? inspection = null,
        IWorkspaceStorageRemovalService? removal = null,
        Func<IReadOnlyCollection<string>>? protectedWorkingCopies = null)
    {
        _inspection = inspection ?? new WorkspaceStorageInspectionService();
        _removal = removal ?? new WorkspaceStorageRemovalService();
        _ownsRemoval = removal is null;
        _protectedWorkingCopies = protectedWorkingCopies ?? (() => []);
    }

    public ObservableCollection<WorkspaceStorageEntry> Entries { get; } = [];
    [ObservableProperty] private WorkspaceStorageEntry? _selectedEntry;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private double _scanProgressPercent;
    [ObservableProperty] private string _status = "Open Storage to inspect local working copies.";
    [ObservableProperty] private string _emptyMessage = "Refresh inspection to measure working copies.";
    [ObservableProperty] private string _output = "Storage inspection has not run.";
    [ObservableProperty] private string _indexedDisplay = "—";
    [ObservableProperty] private string _worktreeDisplay = "—";
    [ObservableProperty] private int _candidateCount;
    [ObservableProperty] private string _filter = "All";
    [ObservableProperty] private WorkspaceStorageRemovalReview? _removalReview;
    [ObservableProperty] private bool _isReviewingRemoval;
    [ObservableProperty] private bool _isRemoving;
    [ObservableProperty] private bool _confirmNoExternalUse;
    [ObservableProperty] private bool _confirmRetainedArtifacts;
    [ObservableProperty] private string _removalError = "";
    [ObservableProperty] private WorkspaceStoragePruneReview? _pruneReview;
    [ObservableProperty] private bool _isReviewingPrune;
    [ObservableProperty] private bool _isPruning;
    [ObservableProperty] private bool _confirmPrunableRegistrations;
    [ObservableProperty] private string _pruneError = "";
    public string WorkspaceRoot { get; private set; } = "";
    public bool HasEntries => Entries.Count > 0;
    public bool ShowEmpty => !IsLoading && !HasEntries;
    public bool HasSelection => SelectedEntry is not null;
    public bool IsAllFilter => Filter == "All";
    public bool IsCandidatesFilter => Filter == "Candidates";
    public bool IsChangedFilter => Filter == "Changed";
    public bool IsBrokenFilter => Filter == "Broken";

    public bool CanReviewRemoval => SelectedEntry is { IsPrimary: false, Exists: true } && !IsBusy;
    public bool CanRemoveReviewed => RemovalReview is { ReadyForConfirmation: true } review &&
                                     ConfirmNoExternalUse && (!review.HasRetainedArtifacts || ConfirmRetainedArtifacts) && !IsRemoving;
    public bool CanReviewPrune => SelectedEntry is { IsPrimary: false, Exists: false, IsBroken: true } && !IsBusy;
    public bool CanPruneReviewed => PruneReview is { ReadyForConfirmation: true } && ConfirmPrunableRegistrations && !IsPruning;
    private bool IsBusy => IsLoading || IsReviewingRemoval || IsRemoving || IsReviewingPrune || IsPruning;

    partial void OnSelectedEntryChanged(WorkspaceStorageEntry? value)
    {
        _reviewVersion++;
        IsReviewingRemoval = false;
        IsReviewingPrune = false;
        RemovalReview = null;
        ConfirmNoExternalUse = false;
        ConfirmRetainedArtifacts = false;
        RemovalError = "";
        PruneReview = null;
        ConfirmPrunableRegistrations = false;
        PruneError = "";
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanReviewRemoval));
        OnPropertyChanged(nameof(CanReviewPrune));
    }
    partial void OnIsLoadingChanged(bool value)
    {
        UpdateActionAvailability();
        OnPropertyChanged(nameof(ShowEmpty));
    }
    partial void OnIsReviewingRemovalChanged(bool value) => UpdateActionAvailability();
    partial void OnIsRemovingChanged(bool value) => UpdateActionAvailability();
    partial void OnIsReviewingPruneChanged(bool value) => UpdateActionAvailability();
    partial void OnIsPruningChanged(bool value) => UpdateActionAvailability();
    partial void OnRemovalReviewChanged(WorkspaceStorageRemovalReview? value) => OnPropertyChanged(nameof(CanRemoveReviewed));
    partial void OnConfirmNoExternalUseChanged(bool value) => OnPropertyChanged(nameof(CanRemoveReviewed));
    partial void OnConfirmRetainedArtifactsChanged(bool value) => OnPropertyChanged(nameof(CanRemoveReviewed));
    partial void OnPruneReviewChanged(WorkspaceStoragePruneReview? value) => OnPropertyChanged(nameof(CanPruneReviewed));
    partial void OnConfirmPrunableRegistrationsChanged(bool value) => OnPropertyChanged(nameof(CanPruneReviewed));
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
        _reviewVersion++;
        _refreshCancellation?.Cancel();
        IsLoading = false;
        WorkspaceRoot = full;
        _allEntries.Clear();
        Entries.Clear();
        SelectedEntry = null;
        IndexedDisplay = "—";
        WorktreeDisplay = "—";
        CandidateCount = 0;
        ScanProgressPercent = 0;
        Status = "Ready to inspect workspace storage.";
        EmptyMessage = "Refresh inspection to measure working copies.";
        Output = "Storage inspection has not run for this workspace.";
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(ShowEmpty));
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
        ScanProgressPercent = 0;
        Status = "Measuring repositories and registered worktrees…";
        try
        {
            var progress = new Progress<WorkspaceStorageScanProgress>(item => Dispatcher.UIThread.Post(() =>
            {
                if (version != _refreshVersion || !IsLoading || _refreshCancellation?.IsCancellationRequested == true || !string.Equals(root, WorkspaceRoot,
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return;
                ScanProgressPercent = item.TotalRepositories == 0 ? 0 : 100d * item.CompletedRepositories / item.TotalRepositories;
                Status = $"Measuring {Path.GetFileName(item.WorkingCopyPath)} · {item.CompletedRepositories}/{item.TotalRepositories} repositories · {item.MeasuredDisplay} in current copy";
            }));
            var snapshot = await _inspection.InspectAsync(root, progress, _refreshCancellation.Token);
            if (version != _refreshVersion || !string.Equals(root, WorkspaceRoot,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                return;
            _allEntries.Clear();
            _allEntries.AddRange(snapshot.Entries);
            IndexedDisplay = snapshot.IndexedDisplay;
            WorktreeDisplay = snapshot.WorktreeDisplay;
            CandidateCount = snapshot.ReviewCandidateCount;
            EmptyMessage = "No working copies match this filter.";
            ApplyFilter();
            Status = $"Inspected {snapshot.Entries.Count} working copies. No files were removed.";
            Output = $"[{snapshot.InspectedAtUtc:HH:mm:ss}] Storage inspection complete — {snapshot.Entries.Count} working copies, " +
                     $"{snapshot.IndexedDisplay} indexed, {snapshot.WorktreeDisplay} in worktrees, {snapshot.ReviewCandidateCount} review candidate(s).\n" +
                     "Candidate status is local evidence only. Open Review removal to refresh remote, Studio-use and retained-content evidence.";
        }
        catch (OperationCanceledException)
        {
            if (version == _refreshVersion)
            {
                Status = "Storage inspection cancelled.";
                if (_allEntries.Count == 0)
                    EmptyMessage = "Inspection cancelled. Refresh inspection to retry.";
            }
        }
        catch (Exception ex)
        {
            if (version == _refreshVersion)
            {
                Status = "Storage inspection failed.";
                Output = StudioDisplayError.From(ex);
                if (_allEntries.Count == 0)
                    EmptyMessage = "Inspection failed. Review the output and refresh.";
            }
        }
        finally
        {
            if (version == _refreshVersion)
                IsLoading = false;
        }
    }

    [RelayCommand]
    private void CancelRefresh()
    {
        if (!IsLoading) return;
        _refreshCancellation?.Cancel();
        Status = "Cancelling storage inspection…";
    }

    [RelayCommand] private void ShowAll() => Filter = "All";
    [RelayCommand] private void ShowCandidates() => Filter = "Candidates";
    [RelayCommand] private void ShowChanged() => Filter = "Changed";
    [RelayCommand] private void ShowBroken() => Filter = "Broken";

    public async Task<bool> ReviewSelectedRemovalAsync()
    {
        if (!CanReviewRemoval || SelectedEntry is not { } entry)
            return false;
        var version = ++_reviewVersion;
        var root = WorkspaceRoot;
        var path = entry.Path;
        IsReviewingRemoval = true;
        RemovalError = "";
        ConfirmNoExternalUse = false;
        ConfirmRetainedArtifacts = false;
        RemovalReview = null;
        try
        {
            var review = await _removal.ReviewAsync(root, entry, _protectedWorkingCopies());
            if (!IsCurrentReview(version, root, path)) return false;
            RemovalReview = review;
            return true;
        }
        catch (Exception ex)
        {
            if (!IsCurrentReview(version, root, path)) return false;
            RemovalError = StudioDisplayError.From(ex);
            Status = "Removal review failed.";
            Output += $"\n[{DateTime.Now:HH:mm:ss}] Removal review failed: {StudioDisplayError.From(ex)}";
            return false;
        }
        finally
        {
            if (version == _reviewVersion) IsReviewingRemoval = false;
        }
    }

    public async Task<bool> RemoveReviewedAsync()
    {
        if (!CanRemoveReviewed || RemovalReview is not { } review)
            return false;
        IsRemoving = true;
        RemovalError = "";
        try
        {
            await _removal.RemoveAsync(review, _protectedWorkingCopies(), ConfirmNoExternalUse, ConfirmRetainedArtifacts);
            RemovalReview = null;
            await RefreshAsync();
            var refreshFailed = Status is "Storage inspection failed." or "Storage inspection cancelled.";
            Status = refreshFailed
                ? $"Removed linked worktree {Path.GetFileName(review.WorktreePath)}; storage refresh failed."
                : $"Removed linked worktree {Path.GetFileName(review.WorktreePath)} and refreshed storage.";
            Output += $"\n[{DateTime.Now:HH:mm:ss}] Removed registered worktree without force and verified its path and registration are gone: {review.WorktreePath}";
            return true;
        }
        catch (Exception ex)
        {
            RemovalError = StudioDisplayError.From(ex);
            return false;
        }
        finally
        {
            IsRemoving = false;
        }
    }

    public async Task<bool> ReviewSelectedPruneAsync()
    {
        if (!CanReviewPrune || SelectedEntry is not { } entry)
            return false;
        var version = ++_reviewVersion;
        var root = WorkspaceRoot;
        var path = entry.Path;
        IsReviewingPrune = true;
        PruneError = "";
        ConfirmPrunableRegistrations = false;
        PruneReview = null;
        try
        {
            var review = await _removal.ReviewPruneAsync(root, entry);
            if (!IsCurrentReview(version, root, path)) return false;
            PruneReview = review;
            return true;
        }
        catch (Exception ex)
        {
            if (!IsCurrentReview(version, root, path)) return false;
            PruneError = StudioDisplayError.From(ex);
            Status = "Stale registration review failed.";
            Output += $"\n[{DateTime.Now:HH:mm:ss}] Stale registration review failed: {StudioDisplayError.From(ex)}";
            return false;
        }
        finally
        {
            if (version == _reviewVersion) IsReviewingPrune = false;
        }
    }

    public async Task<bool> PruneReviewedAsync()
    {
        if (!CanPruneReviewed || PruneReview is not { } review)
            return false;
        IsPruning = true;
        PruneError = "";
        try
        {
            await _removal.PruneAsync(review, ConfirmPrunableRegistrations);
            PruneReview = null;
            await RefreshAsync();
            var refreshFailed = Status is "Storage inspection failed." or "Storage inspection cancelled.";
            Status = refreshFailed
                ? "Removed the reviewed stale registration; storage refresh failed."
                : "Removed the reviewed stale registration and refreshed storage.";
            Output += $"\n[{DateTime.Now:HH:mm:ss}] Studio removed the exact reviewed stale registration and preserved the remaining Git registry: " +
                      string.Join(", ", review.PrunableRegistrations.Select(static item => item.Path));
            return true;
        }
        catch (Exception ex)
        {
            PruneError = StudioDisplayError.From(ex);
            return false;
        }
        finally
        {
            IsPruning = false;
        }
    }

    private void UpdateActionAvailability()
    {
        OnPropertyChanged(nameof(CanReviewRemoval));
        OnPropertyChanged(nameof(CanRemoveReviewed));
        OnPropertyChanged(nameof(CanReviewPrune));
        OnPropertyChanged(nameof(CanPruneReviewed));
    }

    private bool IsCurrentReview(int version, string root, string path)
        => version == _reviewVersion &&
           string.Equals(root, WorkspaceRoot, PathComparison) &&
           SelectedEntry is { } selected && string.Equals(path, selected.Path, PathComparison);

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
        OnPropertyChanged(nameof(ShowEmpty));
    }

    public void Dispose()
    {
        _reviewVersion++;
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        if (_ownsRemoval && _removal is IDisposable disposable) disposable.Dispose();
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
