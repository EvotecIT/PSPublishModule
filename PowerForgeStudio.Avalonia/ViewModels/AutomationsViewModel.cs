using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Domain.Automation;
using PowerForgeStudio.Orchestrator.Automation;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class AutomationsViewModel : ObservableObject, IDisposable
{
    private readonly IWorkspaceAutomationInventoryService _inventory;
    private readonly bool _ownsInventory;
    private readonly List<WorkspaceAutomationEntry> _allEntries = [];
    private CancellationTokenSource? _refreshCancellation;
    private int _refreshVersion;

    public AutomationsViewModel(IWorkspaceAutomationInventoryService? inventory = null)
    {
        _inventory = inventory ?? new WorkspaceAutomationInventoryService();
        _ownsInventory = inventory is null;
    }

    public ObservableCollection<WorkspaceAutomationEntry> Entries { get; } = [];
    public ObservableCollection<WorkspaceAutomationSourceState> Sources { get; } = [];
    [ObservableProperty] private WorkspaceAutomationEntry? _selectedEntry;
    [ObservableProperty] private string _filter = "Relevant";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _status = "Open Automations to inspect provider-owned schedules.";
    [ObservableProperty] private string _output = "Automation inventory has not run.";
    public string WorkspaceRoot { get; private set; } = "";
    public bool HasEntries => Entries.Count > 0;
    public bool IsRelevantFilter => Filter == "Relevant";
    public bool IsAllFilter => Filter == "All";
    public bool IsWindowsFilter => Filter == "Windows";
    public bool IsGitHubFilter => Filter == "GitHub";
    public bool IsAttentionFilter => Filter == "Attention";
    public int RelevantCount => _allEntries.Count(static entry => entry.IsRelevant);
    public int AttentionCount => _allEntries.Count(static entry => entry.NeedsAttention);
    public int DefinitionOnlyCount => _allEntries.Count(static entry => !entry.HasRuntimeEvidence);

    partial void OnFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsRelevantFilter));
        OnPropertyChanged(nameof(IsAllFilter));
        OnPropertyChanged(nameof(IsWindowsFilter));
        OnPropertyChanged(nameof(IsGitHubFilter));
        OnPropertyChanged(nameof(IsAttentionFilter));
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
        Entries.Clear();
        Sources.Clear();
        SelectedEntry = null;
        Status = "Ready to inspect automation providers.";
        Output = "Automation inventory has not run for this workspace.";
        NotifyCounts();
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
        Status = "Reading provider-owned schedules…";
        try
        {
            var snapshot = await _inventory.InspectAsync(root, _refreshCancellation.Token);
            if (version != _refreshVersion || !SamePath(root, WorkspaceRoot)) return;
            _allEntries.Clear();
            _allEntries.AddRange(snapshot.Entries);
            Sources.Clear();
            foreach (var source in snapshot.Sources) Sources.Add(source);
            ApplyFilter();
            NotifyCounts();
            Status = $"Inspected {_allEntries.Count} schedule definitions across {Sources.Count} providers.";
            Output = $"[{snapshot.InspectedAtUtc:HH:mm:ss}] Automation inventory complete — {_allEntries.Count} definitions, " +
                     $"{RelevantCount} relevant, {AttentionCount} need attention, {DefinitionOnlyCount} definition-only.\n" +
                     "Provider ownership is preserved. No task or workflow was run, enabled, paused or edited.";
        }
        catch (OperationCanceledException)
        {
            if (version == _refreshVersion) Status = "Automation inspection cancelled.";
        }
        catch (Exception ex)
        {
            if (version == _refreshVersion)
            {
                Status = "Automation inspection failed.";
                Output = StudioDisplayError.From(ex);
            }
        }
        finally
        {
            if (version == _refreshVersion) IsLoading = false;
        }
    }

    [RelayCommand] private void ShowRelevant() => Filter = "Relevant";
    [RelayCommand] private void ShowAll() => Filter = "All";
    [RelayCommand] private void ShowWindows() => Filter = "Windows";
    [RelayCommand] private void ShowGitHub() => Filter = "GitHub";
    [RelayCommand] private void ShowAttention() => Filter = "Attention";

    private void ApplyFilter()
    {
        var selectedId = SelectedEntry?.Id;
        var visible = Filter switch
        {
            "Relevant" => _allEntries.Where(static entry => entry.IsRelevant),
            "Windows" => _allEntries.Where(static entry => entry.Provider == "Windows Task Scheduler"),
            "GitHub" => _allEntries.Where(static entry => entry.Provider == "GitHub Actions"),
            "Attention" => _allEntries.Where(static entry => entry.NeedsAttention),
            _ => _allEntries
        };
        Entries.Clear();
        foreach (var entry in visible) Entries.Add(entry);
        SelectedEntry = selectedId is null ? null : Entries.FirstOrDefault(entry => entry.Id == selectedId);
        OnPropertyChanged(nameof(HasEntries));
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(RelevantCount));
        OnPropertyChanged(nameof(AttentionCount));
        OnPropertyChanged(nameof(DefinitionOnlyCount));
        OnPropertyChanged(nameof(HasEntries));
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
