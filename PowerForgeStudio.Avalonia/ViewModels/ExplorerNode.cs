using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

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
    public string RepositoryRoot { get; }
    public ObservableCollection<ExplorerNode> Children { get; } = [];
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private bool _isExpanded;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value) _ = EnsureLoadedAsync();
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
            Children.Add(new ExplorerNode("Unable to load", "", "error", RepositoryRoot) { Detail = ex.Message });
            IsExpanded = false;
        }
        finally { _loading = null; }
    }
}
