using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media;

namespace PowerForgeStudio.Avalonia.ViewModels;

/// <summary>A presentation node. Filesystem and Git behavior stay in the orchestrator.</summary>
public sealed partial class ExplorerNode : ObservableObject
{
    private readonly Func<ExplorerNode, Task>? _load;
    private bool _loaded;
    private Task? _loading;

    public ExplorerNode(string name, string path, string kind, string repositoryRoot,
        Func<ExplorerNode, Task>? load = null)
    {
        Name = name;
        Path = path;
        Kind = kind;
        RepositoryRoot = repositoryRoot;
        _load = load;
        if (load is not null) Children.Add(new ExplorerNode("Loading…", "", "placeholder", repositoryRoot));
    }

    public string Name { get; }
    public string Path { get; }
    public string Kind { get; }
    public FontWeight NodeWeight => Kind == "branch" && string.IsNullOrEmpty(Path) ? FontWeight.SemiBold : FontWeight.Normal;
    public string IconKind => IsContextProject && Kind == "project" ? "folder" : Kind;
    private static readonly IBrush ContextBrush = Brush.Parse("#EAF2FF");
    public IBrush ContextBackground => IsContextProject ? ContextBrush : Brushes.Transparent;
    private static readonly IBrush CleanStatusBrush = Brush.Parse("#168A46");
    private static readonly IBrush ChangedStatusBrush = Brush.Parse("#E6A000");
    private static readonly IBrush ConflictStatusBrush = Brush.Parse("#D32835");
    private static readonly IBrush NeutralStatusBrush = Brush.Parse("#738198");
    public bool HasStatusMarker => !string.IsNullOrEmpty(StatusMarker);
    public IBrush StatusBrush => StatusMarker switch
    {
        "clean" => CleanStatusBrush,
        "conflicts" or "U" => ConflictStatusBrush,
        "not Git" => NeutralStatusBrush,
        _ => ChangedStatusBrush
    };
    public string RepositoryRoot { get; }
    public ObservableCollection<ExplorerNode> Children { get; } = [];
    internal bool BuildExpansionHandled { get; set; }
    internal bool BuildWasExplicitlyCollapsed { get; private set; }

    internal void RestoreBuildCollapsed()
    {
        BuildExpansionHandled = true;
        BuildWasExplicitlyCollapsed = true;
    }
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private string _statusMarker = "";
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isContextProject;
    partial void OnStatusMarkerChanged(string value)
    {
        OnPropertyChanged(nameof(HasStatusMarker));
        OnPropertyChanged(nameof(StatusBrush));
    }
    partial void OnIsContextProjectChanged(bool value)
    {
        OnPropertyChanged(nameof(IconKind));
        OnPropertyChanged(nameof(ContextBackground));
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (Kind == "folder" && Name.Equals("Build", StringComparison.OrdinalIgnoreCase))
        {
            if (value) { BuildExpansionHandled = true; BuildWasExplicitlyCollapsed = false; }
            else if (BuildExpansionHandled) BuildWasExplicitlyCollapsed = true;
        }
        if (value)
        {
            _ = EnsureLoadedAsync();
        }
    }

    public Task EnsureLoadedAsync()
    {
        if (_loaded || _load is null) return Task.CompletedTask;
        if (_loading is { IsCompleted: false }) return _loading;
        return _loading = LoadAsync();
    }

    public async Task ReloadAsync()
    {
        if (_loading is not null) await _loading;
        _loaded = false;
        _loading = null;
        await EnsureLoadedAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            await _load!(this);
            _loaded = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Children.Clear();
            Children.Add(new ExplorerNode("Unable to load", "", "error", RepositoryRoot) { Detail = StudioDisplayError.From(ex) });
            IsExpanded = false;
        }
        finally { _loading = null; }
    }
}
