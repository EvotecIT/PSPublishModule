using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Domain.Connections;
using PowerForgeStudio.Orchestrator.Connections;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class ConnectionsViewModel : ObservableObject, IDisposable
{
    private readonly IWorkspaceConnectionInventoryService _inventory;
    private readonly Action<string> _openExternal;
    private readonly List<WorkspaceConnectionEntry> _allEntries = [];
    private CancellationTokenSource? _refreshCancellation;
    private int _refreshVersion;

    public ConnectionsViewModel(IWorkspaceConnectionInventoryService? inventory = null, Action<string>? openExternal = null)
    {
        _inventory = inventory ?? new WorkspaceConnectionInventoryService();
        _openExternal = openExternal ?? (target => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }));
    }

    public ObservableCollection<WorkspaceConnectionEntry> Entries { get; } = [];
    public ObservableCollection<WorkspaceConnectionSourceState> Sources { get; } = [];
    [ObservableProperty] private WorkspaceConnectionEntry? _selectedEntry;
    [ObservableProperty] private string _filter = "All";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _status = "Open Connections to verify local tools and provider boundaries.";
    [ObservableProperty] private string _output = "Connection inventory has not run.";
    public string WorkspaceRoot { get; private set; } = "";
    public bool HasEntries => Entries.Count > 0;
    public bool IsAllFilter => Filter == "All";
    public bool IsVerifiedFilter => Filter == "Verified";
    public bool IsAttentionFilter => Filter == "Attention";
    public bool IsToolchainsFilter => Filter == "Toolchains";
    public bool IsServicesFilter => Filter == "Services";
    public int VerifiedCount => _allEntries.Count(static entry => entry.IsVerified);
    public int AvailableCount => _allEntries.Count(static entry => entry.State == "Available");
    public int UnconfiguredCount => _allEntries.Count(static entry => entry.State == "Unconfigured");
    public int PendingCount => AvailableCount + UnconfiguredCount;
    public int AttentionCount => _allEntries.Count(static entry => entry.NeedsAttention);
    public bool CanOpenSelectedPortal => SelectedEntry is { Id: "licensing:control", Provider: "Licensing" } entry &&
        IsEvotecControlUri(entry.Endpoint);

    partial void OnSelectedEntryChanged(WorkspaceConnectionEntry? value)
    {
        OnPropertyChanged(nameof(CanOpenSelectedPortal));
        OpenSelectedPortalCommand.NotifyCanExecuteChanged();
    }

    partial void OnFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsAllFilter));
        OnPropertyChanged(nameof(IsVerifiedFilter));
        OnPropertyChanged(nameof(IsAttentionFilter));
        OnPropertyChanged(nameof(IsToolchainsFilter));
        OnPropertyChanged(nameof(IsServicesFilter));
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
        Status = "Ready to verify connection providers.";
        Output = "Connection inventory has not run for this workspace.";
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
        Status = "Verifying provider capabilities without loading secrets…";
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
            Status = $"Verified {_allEntries.Count} connection boundaries across {Sources.Count} providers.";
            Output = $"[{snapshot.InspectedAtUtc:HH:mm:ss}] Connection inventory complete — {_allEntries.Count} boundaries, " +
                     $"{VerifiedCount} verified or reachable, {AvailableCount} available, {UnconfiguredCount} unconfigured, {AttentionCount} need attention.\n" +
                     "No secret values were requested, stored, logged or displayed. Provider configuration was not changed.";
        }
        catch (OperationCanceledException)
        {
            if (version == _refreshVersion) Status = "Connection inspection cancelled.";
        }
        catch (Exception ex)
        {
            if (version == _refreshVersion)
            {
                Status = "Connection inspection failed.";
                Output = StudioDisplayError.From(ex);
            }
        }
        finally
        {
            if (version == _refreshVersion) IsLoading = false;
        }
    }

    [RelayCommand] private void ShowAll() => Filter = "All";
    [RelayCommand] private void ShowVerified() => Filter = "Verified";
    [RelayCommand] private void ShowAttention() => Filter = "Attention";
    [RelayCommand] private void ShowToolchains() => Filter = "Toolchains";
    [RelayCommand] private void ShowServices() => Filter = "Services";

    [RelayCommand(CanExecute = nameof(CanOpenSelectedPortal))]
    private void OpenSelectedPortal()
    {
        if (!CanOpenSelectedPortal) return;
        try
        {
            _openExternal("https://control.evotec.xyz/");
            Status = "Opened Evotec Control in the system browser. Studio did not transfer credentials.";
        }
        catch (Exception ex)
        {
            Output = "Could not open Evotec Control: " + StudioDisplayError.From(ex);
        }
    }

    private static bool IsEvotecControlUri(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           uri.Scheme == Uri.UriSchemeHttps &&
           string.Equals(uri.Host, "control.evotec.xyz", StringComparison.OrdinalIgnoreCase) &&
           uri.Port == 443 && uri.AbsolutePath == "/" &&
           string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) &&
           string.IsNullOrEmpty(uri.Fragment);

    private void ApplyFilter()
    {
        var selectedId = SelectedEntry?.Id;
        var visible = Filter switch
        {
            "Verified" => _allEntries.Where(static entry => entry.IsVerified),
            "Attention" => _allEntries.Where(static entry => entry.NeedsAttention),
            "Toolchains" => _allEntries.Where(static entry => entry.Category == "Toolchains"),
            "Services" => _allEntries.Where(static entry => entry.Category != "Toolchains"),
            _ => _allEntries
        };
        Entries.Clear();
        foreach (var entry in visible) Entries.Add(entry);
        SelectedEntry = selectedId is null ? null : Entries.FirstOrDefault(entry => entry.Id == selectedId);
        OnPropertyChanged(nameof(HasEntries));
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(VerifiedCount));
        OnPropertyChanged(nameof(AvailableCount));
        OnPropertyChanged(nameof(UnconfiguredCount));
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(AttentionCount));
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
    }
}
