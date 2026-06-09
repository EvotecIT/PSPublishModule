using System.Collections.ObjectModel;
using System.Windows.Media;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Orchestrator.Explorer;

namespace PowerForgeStudio.Wpf.ViewModels.Hub;

public sealed class FileTreeNodeViewModel : ViewModelBase
{
    private readonly FileExplorerService? _service;
    private bool _isExpanded;
    private bool _isSelected;
    private bool _isLoading;
    private bool _isLoaded;
    private readonly bool _isPlaceholder;

    private FileTreeNodeViewModel(bool isPlaceholder)
    {
        _isPlaceholder = isPlaceholder;
        Name = string.Empty;
        FullPath = string.Empty;
        Children = [];
    }

    public FileTreeNodeViewModel(FileSystemEntry entry, FileExplorerService service)
    {
        Name = entry.Name;
        FullPath = entry.FullPath;
        IsDirectory = entry.IsDirectory;
        Icon = FileIconCache.GetIcon(entry.FullPath, entry.IsDirectory);
        _service = service;

        Children = [];
        if (entry.IsDirectory)
        {
            // Add a per-node placeholder so the expand arrow appears before the folder is loaded.
            Children.Add(new FileTreeNodeViewModel(isPlaceholder: true));
        }
    }

    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public ImageSource? Icon { get; }
    public bool IsPlaceholder => _isPlaceholder;

    public ObservableCollection<FileTreeNodeViewModel> Children { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value) && value && !_isLoaded && IsDirectory)
            {
                _ = LoadChildrenAsync();
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    private async Task LoadChildrenAsync()
    {
        if (_isLoaded || _service is null) return;
        _isLoaded = true;
        IsLoading = true;

        try
        {
            var entries = await _service.ListDirectoryAsync(FullPath).ConfigureAwait(true);

            Children.Clear(); // Remove sentinel

            foreach (var entry in entries)
            {
                if (entry.IsDirectory)
                {
                    Children.Add(new FileTreeNodeViewModel(entry, _service));
                }
            }
        }
        catch
        {
            Children.Clear();
        }
        finally
        {
            IsLoading = false;
        }
    }
}
