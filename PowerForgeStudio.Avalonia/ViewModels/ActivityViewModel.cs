using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Domain.Activity;
using PowerForgeStudio.Orchestrator.Activity;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class ActivityViewModel : ObservableObject, IDisposable
{
    private readonly IWorkspaceActivityInventoryService _inventory;
    private readonly bool _ownsInventory;
    private readonly List<WorkspaceActivityEntry> _allEntries = [];
    private readonly HashSet<string> _mutedIds = new(StringComparer.Ordinal);
    private WorkspaceActivityOptions _options = new();
    private CancellationTokenSource? _refreshCancellation;
    private int _refreshVersion;

    public ActivityViewModel(IWorkspaceActivityInventoryService? inventory = null)
    {
        _ownsInventory = inventory is null;
        _inventory = inventory ?? new WorkspaceActivityInventoryService();
    }

    public ObservableCollection<WorkspaceActivityEntry> Entries { get; } = [];
    public ObservableCollection<WorkspaceActivitySourceState> Sources { get; } = [];
    [ObservableProperty] private WorkspaceActivityEntry? _selectedEntry;
    [ObservableProperty] private string _filter = "Attention";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _status = "Open Activity to inspect cross-project attention signals.";
    [ObservableProperty] private string _output = "Activity inventory has not run.";
    [ObservableProperty] private int _omittedCount;
    public string WorkspaceRoot { get; private set; } = "";
    public bool HasEntries => Entries.Count > 0;
    public bool IsTruncated => OmittedCount > 0;
    public bool IsAttentionFilter => Filter == "Attention";
    public bool IsAllFilter => Filter == "All";
    public bool IsGitHubFilter => Filter == "GitHub";
    public bool IsReleasesFilter => Filter == "Releases";
    public bool IsSchedulesFilter => Filter == "Schedules";
    public bool IsMutedFilter => Filter == "Muted";
    public int ActionableCount => _allEntries.Count(entry => entry.IsActionable && !_mutedIds.Contains(entry.Id));
    public int CriticalCount => _allEntries.Count(entry => entry.Severity == "Critical" && !_mutedIds.Contains(entry.Id));
    public int ReviewCount => _allEntries.Count(entry => entry.Severity == "Review" && !_mutedIds.Contains(entry.Id));
    public int MutedCount => _mutedIds.Count;
    public bool HasMuted => MutedCount > 0;
    public int UnavailableSourceCount => Sources.Count(static source => source.IsUnavailable);
    public bool CanOpenSelected => SelectedEntry is { OpenTarget: { Length: > 0 } target } && IsSafeOpenTarget(target);
    public bool CanMuteSelected => SelectedEntry is not null && !_mutedIds.Contains(SelectedEntry.Id);

    partial void OnSelectedEntryChanged(WorkspaceActivityEntry? value)
    {
        OnPropertyChanged(nameof(CanOpenSelected));
        OnPropertyChanged(nameof(CanMuteSelected));
        OpenSelectedCommand.NotifyCanExecuteChanged();
        MuteSelectedCommand.NotifyCanExecuteChanged();
    }

    partial void OnOmittedCountChanged(int value) => OnPropertyChanged(nameof(IsTruncated));

    partial void OnFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsAttentionFilter));
        OnPropertyChanged(nameof(IsAllFilter));
        OnPropertyChanged(nameof(IsGitHubFilter));
        OnPropertyChanged(nameof(IsReleasesFilter));
        OnPropertyChanged(nameof(IsSchedulesFilter));
        OnPropertyChanged(nameof(IsMutedFilter));
        ApplyFilter();
    }

    public void SetWorkspace(string root)
    {
        var full = Path.GetFullPath(root);
        if (SamePath(full, WorkspaceRoot)) return;
        ++_refreshVersion;
        _refreshCancellation?.Cancel();
        IsLoading = false;
        WorkspaceRoot = full;
        _allEntries.Clear();
        _mutedIds.Clear();
        Entries.Clear();
        Sources.Clear();
        SelectedEntry = null;
        OmittedCount = 0;
        Status = "Ready to inspect workspace activity.";
        Output = "Activity inventory has not run for this workspace.";
        NotifyCounts();
    }

    public void SetOptions(WorkspaceActivityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (_options == options) return;
        _options = options;
        ++_refreshVersion;
        _refreshCancellation?.Cancel();
        IsLoading = false;
        Status = "Activity limits changed. Refresh evidence to apply them.";
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsLoading || string.IsNullOrWhiteSpace(WorkspaceRoot)) return;
        var version = ++_refreshVersion;
        var root = WorkspaceRoot;
        _refreshCancellation?.Dispose();
        _refreshCancellation = new CancellationTokenSource();
        IsLoading = true;
        Status = "Collecting read-only project, release, GitHub and schedule evidence…";
        try
        {
            var snapshot = await _inventory.InspectAsync(root, _options, _refreshCancellation.Token);
            if (version != _refreshVersion || !SamePath(root, WorkspaceRoot)) return;
            _allEntries.Clear();
            _allEntries.AddRange(snapshot.Entries);
            Sources.Clear();
            foreach (var source in snapshot.Sources) Sources.Add(source);
            OmittedCount = snapshot.OmittedEntryCount;
            ApplyFilter();
            NotifyCounts();
            Status = snapshot.IsTruncated
                ? $"Showing {_allEntries.Count} of {snapshot.TotalEntryCount} activity signals across {snapshot.RepositoryCount} managed repositories."
                : $"Observed {_allEntries.Count} activity signals across {snapshot.RepositoryCount} managed repositories.";
            Output = $"[{snapshot.InspectedAtUtc:HH:mm:ss}] Activity refresh complete — {_allEntries.Count} signals, " +
                     $"{ActionableCount} actionable, {CriticalCount} critical, {ReviewCount} awaiting review, " +
                     $"{UnavailableSourceCount} provider source(s) unavailable" +
                     (snapshot.IsTruncated ? $", {snapshot.OmittedEntryCount} lower-priority signal(s) omitted by the display limit.\n" : ".\n") +
                     "No build, script, schedule, publication, issue or pull request was changed.";
        }
        catch (OperationCanceledException)
        {
            if (version == _refreshVersion) Status = "Activity inspection cancelled.";
        }
        catch (Exception ex)
        {
            if (version == _refreshVersion)
            {
                Status = "Activity inspection failed.";
                Output = ex.Message;
            }
        }
        finally
        {
            if (version == _refreshVersion) IsLoading = false;
        }
    }

    [RelayCommand] private void ShowAttention() => Filter = "Attention";
    [RelayCommand] private void ShowAll() => Filter = "All";
    [RelayCommand] private void ShowGitHub() => Filter = "GitHub";
    [RelayCommand] private void ShowReleases() => Filter = "Releases";
    [RelayCommand] private void ShowSchedules() => Filter = "Schedules";
    [RelayCommand] private void ShowMuted() => Filter = "Muted";

    [RelayCommand(CanExecute = nameof(CanMuteSelected))]
    private void MuteSelected()
    {
        if (SelectedEntry is null) return;
        var title = SelectedEntry.Title;
        _mutedIds.Add(SelectedEntry.Id);
        SelectedEntry = null;
        ApplyFilter();
        NotifyCounts();
        Output = $"Hidden for this Studio session: {title}. Provider state was not changed.";
    }

    [RelayCommand]
    private void RestoreMuted()
    {
        _mutedIds.Clear();
        ApplyFilter();
        NotifyCounts();
        Output = "Restored all session-hidden Activity items. Provider state was not changed.";
    }

    [RelayCommand(CanExecute = nameof(CanOpenSelected))]
    private void OpenSelected()
    {
        var target = SelectedEntry?.OpenTarget;
        if (target is null || !IsSafeOpenTarget(target)) return;
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            Output = $"Opened source for {SelectedEntry!.Title}.";
        }
        catch (Exception ex)
        {
            Output = "Could not open the selected source: " + ex.Message;
        }
    }

    private void ApplyFilter()
    {
        var selectedId = SelectedEntry?.Id;
        IEnumerable<WorkspaceActivityEntry> visible = Filter switch
        {
            "Muted" => _allEntries.Where(entry => _mutedIds.Contains(entry.Id)),
            "All" => _allEntries.Where(entry => !_mutedIds.Contains(entry.Id)),
            "GitHub" => _allEntries.Where(entry => !_mutedIds.Contains(entry.Id) && entry.Kind is "Issue" or "Pull request" or "CI" or "GitHub"),
            "Releases" => _allEntries.Where(entry => !_mutedIds.Contains(entry.Id) && entry.Kind == "Release"),
            "Schedules" => _allEntries.Where(entry => !_mutedIds.Contains(entry.Id) && entry.Kind == "Schedule"),
            _ => _allEntries.Where(entry => !_mutedIds.Contains(entry.Id) && entry.IsActionable)
        };
        Entries.Clear();
        foreach (var entry in visible) Entries.Add(entry);
        SelectedEntry = selectedId is null ? null : Entries.FirstOrDefault(entry => entry.Id == selectedId);
        OnPropertyChanged(nameof(HasEntries));
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(ActionableCount));
        OnPropertyChanged(nameof(CriticalCount));
        OnPropertyChanged(nameof(ReviewCount));
        OnPropertyChanged(nameof(MutedCount));
        OnPropertyChanged(nameof(HasMuted));
        OnPropertyChanged(nameof(UnavailableSourceCount));
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(CanMuteSelected));
        MuteSelectedCommand.NotifyCanExecuteChanged();
    }

    private bool IsSafeOpenTarget(string target)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!File.Exists(target) && !Directory.Exists(target)) return false;
        var fullTarget = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(WorkspaceRoot));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var contained = string.Equals(fullTarget, fullRoot, comparison)
                        || fullTarget.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison)
                        || fullTarget.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, comparison);
        if (!contained) return false;
        try
        {
            var current = Directory.Exists(fullTarget) ? fullTarget : Path.GetDirectoryName(fullTarget);
            while (!string.IsNullOrWhiteSpace(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
                if (string.Equals(current, fullRoot, comparison)) return true;
                current = Path.GetDirectoryName(current);
            }
        }
        catch
        {
            return false;
        }
        return false;
    }

    private static bool SamePath(string left, string right)
        => !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
           string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
               OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public void Dispose()
    {
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        if (_ownsInventory && _inventory is IDisposable disposable) disposable.Dispose();
    }
}
