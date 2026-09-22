using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Domain.Packages;
using PowerForgeStudio.Orchestrator.Packages;

namespace PowerForgeStudio.Avalonia.ViewModels;

/// <summary>Read-only public package snapshot with local filtering and explicit source handoff.</summary>
public sealed partial class PackagesViewModel : ObservableObject, IDisposable
{
    private readonly IWorkspacePackageCatalogService _catalog;
    private readonly List<WorkspacePackageMetric> _all = [];
    private CancellationTokenSource? _refresh;
    private int _version;

    public PackagesViewModel(IWorkspacePackageCatalogService? catalog = null)
        => _catalog = catalog ?? new WorkspacePackageCatalogService();

    public ObservableCollection<WorkspacePackageMetric> Entries { get; } = [];
    [ObservableProperty] private WorkspacePackageMetric? _selectedEntry;
    [ObservableProperty] private string _filter = "All";
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _status = "Open Packages to read the published package snapshot.";
    [ObservableProperty] private string _output = "No public package snapshot has been read.";
    [ObservableProperty] private string _sourceStatus = "Not loaded";
    [ObservableProperty] private string _nuGetDownloads = "Not reported";
    [ObservableProperty] private string _powerShellGalleryDownloads = "Not reported";
    [ObservableProperty] private string _totalDownloads = "Not reported";
    [ObservableProperty] private string _nuGetCountLabel = "Not loaded";
    [ObservableProperty] private string _powerShellGalleryCountLabel = "Not loaded";
    [ObservableProperty] private string _warningSummary = "";
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private bool _isStale;
    public bool HasLoaded { get; private set; }
    public bool HasEntries => Entries.Count > 0;
    public bool HasWarnings => WarningCount > 0;
    public bool HasSelection => SelectedEntry is not null;
    public bool IsAllFilter => Filter == "All";
    public bool IsNuGetFilter => Filter == "NuGet.org";
    public bool IsGalleryFilter => Filter == "PowerShell Gallery";
    public int TotalCount => _all.Count;
    public int NuGetCount => _all.Count(static entry => entry.Registry == "NuGet.org");
    public int PowerShellGalleryCount => _all.Count(static entry => entry.Registry == "PowerShell Gallery");
    public string SelectedSource => SelectedEntry?.DetailsUrl ?? "";
    public bool CanOpenSelected => SelectedEntry?.DetailsUrl is { Length: > 0 };

    partial void OnWarningCountChanged(int value) => OnPropertyChanged(nameof(HasWarnings));

    partial void OnSelectedEntryChanged(WorkspacePackageMetric? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedSource));
        OnPropertyChanged(nameof(CanOpenSelected));
        OpenSelectedCommand.NotifyCanExecuteChanged();
    }

    partial void OnFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsAllFilter));
        OnPropertyChanged(nameof(IsNuGetFilter));
        OnPropertyChanged(nameof(IsGalleryFilter));
        ApplyFilter();
    }

    partial void OnSearchChanged(string value) => ApplyFilter();

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsLoading) return;
        var version = ++_version;
        _refresh?.Dispose();
        _refresh = new CancellationTokenSource();
        IsLoading = true;
        Status = "Reading the public NuGet and PowerShell Gallery snapshot…";
        try
        {
            var snapshot = await _catalog.ReadAsync(_refresh.Token);
            if (version != _version) return;
            var selected = SelectedEntry is { } current ? (Registry: current.Registry, Id: current.Id) : ((string Registry, string Id)?)null;
            _all.Clear();
            _all.AddRange(snapshot.Packages.OrderByDescending(static item => item.Downloads ?? -1)
                .ThenBy(static item => item.Id, StringComparer.OrdinalIgnoreCase));
            SourceStatus = $"Generated {snapshot.GeneratedAtUtc:yyyy-MM-dd HH:mm} UTC · public Evotec website";
            NuGetDownloads = Format(snapshot.NuGetDownloads);
            PowerShellGalleryDownloads = Format(snapshot.PowerShellGalleryDownloads);
            TotalDownloads = Format(snapshot.TotalDownloads);
            NuGetCountLabel = snapshot.NuGetAvailable ? $"{snapshot.NuGetCount} packages" : "Registry unavailable";
            PowerShellGalleryCountLabel = snapshot.PowerShellGalleryAvailable ? $"{snapshot.PowerShellGalleryCount} modules" : "Registry unavailable";
            WarningCount = snapshot.WarningCount;
            WarningSummary = snapshot.Warnings.Count > 0
                ? string.Join(" · ", snapshot.Warnings)
                : snapshot.WarningCount > 0 ? "The source reported warnings without details." : "";
            IsStale = snapshot.IsStale;
            HasLoaded = true;
            ApplyFilter();
            if (selected is { } key)
                SelectedEntry = Entries.FirstOrDefault(item => item.Registry == key.Registry && item.Id == key.Id);
            NotifyCounts();
            Status = $"{TotalCount} public packages observed" +
                (snapshot.IsStale ? " · source snapshot is older than two days" : "") +
                (snapshot.IsPartial ? $" · {snapshot.WarningCount} evidence warning(s)" : "") + ".";
            Output = $"[{snapshot.ReadAtUtc:HH:mm:ss}] Read {TotalCount} public package entries generated " +
                     $"{snapshot.GeneratedAtUtc:yyyy-MM-dd HH:mm} UTC. No registry credentials were loaded and no package was published.";
        }
        catch (OperationCanceledException)
        {
            if (version == _version) Status = "Package read cancelled; previous evidence remains visible.";
        }
        catch (Exception ex)
        {
            if (version == _version)
            {
                Status = "Package snapshot unavailable; previous evidence remains visible.";
                Output = StudioDisplayError.From(ex);
            }
        }
        finally { if (version == _version) IsLoading = false; }
    }

    [RelayCommand] private void ShowAll() => Filter = "All";
    [RelayCommand] private void ShowNuGet() => Filter = "NuGet.org";
    [RelayCommand] private void ShowGallery() => Filter = "PowerShell Gallery";

    [RelayCommand(CanExecute = nameof(CanOpenSelected))]
    private void OpenSelected()
    {
        if (SelectedEntry?.DetailsUrl is not { } target ||
            !Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.Host is not ("www.nuget.org" or "www.powershellgallery.com")) return;
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Output = "Could not open package details: " + StudioDisplayError.From(ex); }
    }

    private void ApplyFilter()
    {
        var selected = SelectedEntry is { } current ? (Registry: current.Registry, Id: current.Id) : ((string Registry, string Id)?)null;
        Entries.Clear();
        var query = Search.Trim();
        foreach (var item in _all.Where(item =>
                     (Filter == "All" || item.Registry == Filter) &&
                     (query.Length == 0 || item.Id.Contains(query, StringComparison.OrdinalIgnoreCase))))
            Entries.Add(item);
        SelectedEntry = selected is { } key
            ? Entries.FirstOrDefault(item => item.Registry == key.Registry && item.Id == key.Id) : null;
        OnPropertyChanged(nameof(HasEntries));
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(NuGetCount));
        OnPropertyChanged(nameof(PowerShellGalleryCount));
        OnPropertyChanged(nameof(HasEntries));
    }

    private static string Format(long? value) => value is { } count ? count.ToString("N0") : "Not reported";

    public void Dispose()
    {
        ++_version;
        _refresh?.Cancel();
        _refresh?.Dispose();
    }
}
