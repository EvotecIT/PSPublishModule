using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using PowerForgeStudio.Orchestrator.Explorer;

namespace PowerForgeStudio.Wpf.ViewModels.Hub;

public sealed class FileExplorerViewModel : ViewModelBase, IDisposable
{
    private readonly FileExplorerService _service;
    private readonly string _rootPath;
    private FileTreeNodeViewModel? _selectedTreeNode;
    private string _currentPath;
    private string _searchFilter = string.Empty;
    private FileSystemWatcher? _currentWatcher;
    private readonly DispatcherTimer _refreshDebounce;
    private bool _disposed;

    private readonly List<FileListItemViewModel> _allCurrentItems = [];

    public FileExplorerViewModel(string rootPath, FileExplorerService service)
    {
        _rootPath = rootPath;
        _service = service;
        _currentPath = rootPath;

        RootNodes = [];
        CurrentFolderContents = new BatchObservableCollection<FileListItemViewModel>();

        OpenItemCommand = new DelegateCommand<FileListItemViewModel?>(OnOpenItem);
        OpenInExplorerCommand = new DelegateCommand<FileListItemViewModel?>(item =>
        {
            var path = item?.FullPath ?? _currentPath;
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")); } catch { }
        });
        CopyPathCommand = new DelegateCommand<FileListItemViewModel?>(item =>
        {
            var path = item?.FullPath ?? _currentPath;
            try { System.Windows.Clipboard.SetText(path); } catch { }
        });
        RefreshCommand = new AsyncDelegateCommand(RefreshCurrentFolderAsync);
        NavigateUpCommand = new DelegateCommand<object?>(_ => NavigateUp());

        _refreshDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _refreshDebounce.Tick += async (_, _) =>
        {
            _refreshDebounce.Stop();
            await RefreshCurrentFolderAsync().ConfigureAwait(true);
        };

        // Create root node
        var rootEntry = new Domain.Hub.FileSystemEntry(
            Path.GetFileName(rootPath),
            rootPath,
            IsDirectory: true,
            SizeBytes: 0,
            LastModifiedUtc: DateTimeOffset.UtcNow);

        var rootNode = new FileTreeNodeViewModel(rootEntry, _service);
        RootNodes.Add(rootNode);
        rootNode.IsExpanded = true;

        // Load initial right pane
        _ = LoadFolderContentsAsync(rootPath);
    }

    public ObservableCollection<FileTreeNodeViewModel> RootNodes { get; }
    public BatchObservableCollection<FileListItemViewModel> CurrentFolderContents { get; }

    public FileTreeNodeViewModel? SelectedTreeNode
    {
        get => _selectedTreeNode;
        set
        {
            if (SetProperty(ref _selectedTreeNode, value) && value is not null)
            {
                _ = LoadFolderContentsAsync(value.FullPath);
            }
        }
    }

    public string CurrentPath
    {
        get => _currentPath;
        private set => SetProperty(ref _currentPath, value);
    }

    public string SearchFilter
    {
        get => _searchFilter;
        set
        {
            if (SetProperty(ref _searchFilter, value))
            {
                ApplyFilter();
            }
        }
    }

    public DelegateCommand<FileListItemViewModel?> OpenItemCommand { get; }
    public DelegateCommand<FileListItemViewModel?> OpenInExplorerCommand { get; }
    public DelegateCommand<FileListItemViewModel?> CopyPathCommand { get; }
    public AsyncDelegateCommand RefreshCommand { get; }
    public DelegateCommand<object?> NavigateUpCommand { get; }

    public void OnTreeSelectionChanged(FileTreeNodeViewModel? node)
    {
        SelectedTreeNode = node;
    }

    private void OnOpenItem(FileListItemViewModel? item)
    {
        if (item is null) return;

        if (item.IsDirectory)
        {
            _ = LoadFolderContentsAsync(item.FullPath);
        }
        else
        {
            try
            {
                Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
            }
            catch { }
        }
    }

    private void NavigateUp()
    {
        var parent = Path.GetDirectoryName(_currentPath);
        if (!string.IsNullOrEmpty(parent) && parent.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
        {
            _ = LoadFolderContentsAsync(parent);
        }
    }

    private async Task LoadFolderContentsAsync(string path)
    {
        CurrentPath = path;
        SetupWatcher(path);

        try
        {
            var entries = await _service.ListDirectoryAsync(path).ConfigureAwait(true);

            _allCurrentItems.Clear();
            foreach (var entry in entries)
            {
                _allCurrentItems.Add(new FileListItemViewModel(entry));
            }

            ApplyFilter();
        }
        catch
        {
            _allCurrentItems.Clear();
            CurrentFolderContents.ReplaceAll([]);
        }
    }

    private async Task RefreshCurrentFolderAsync()
    {
        await LoadFolderContentsAsync(_currentPath).ConfigureAwait(true);
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(_searchFilter))
        {
            CurrentFolderContents.ReplaceAll(_allCurrentItems);
        }
        else
        {
            var filtered = _allCurrentItems
                .Where(item => item.Name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
            CurrentFolderContents.ReplaceAll(filtered);
        }
    }

    private void SetupWatcher(string path)
    {
        _currentWatcher?.Dispose();
        _currentWatcher = null;

        if (_disposed) return;

        try
        {
            _currentWatcher = _service.CreateWatcher(path);
            _currentWatcher.Changed += OnFileSystemChanged;
            _currentWatcher.Created += OnFileSystemChanged;
            _currentWatcher.Deleted += OnFileSystemChanged;
            _currentWatcher.Renamed += OnFileSystemChanged;
        }
        catch
        {
            // Watcher creation failed — non-fatal
        }
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce: restart the timer on each event
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            _refreshDebounce.Stop();
            _refreshDebounce.Start();
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _refreshDebounce.Stop();
        _currentWatcher?.Dispose();
    }
}
