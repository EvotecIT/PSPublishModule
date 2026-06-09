using System.Collections.ObjectModel;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed class WorkspaceExplorerNodeViewModel : ViewModelBase
{
    private bool _isActive;
    private bool _isExpanded;

    public WorkspaceExplorerNodeViewModel(
        string displayName,
        string badge,
        string summary,
        string detail,
        string? familyKey,
        string? rootPath,
        bool isFamily)
    {
        DisplayName = displayName;
        Badge = badge;
        Summary = summary;
        Detail = detail;
        FamilyKey = familyKey;
        RootPath = rootPath;
        IsFamily = isFamily;
    }

    public ObservableCollection<WorkspaceExplorerNodeViewModel> Children { get; } = [];

    public string DisplayName { get; }

    public string Badge { get; }

    public string Summary { get; }

    public string Detail { get; }

    public string? FamilyKey { get; }

    public string? RootPath { get; }

    public bool IsFamily { get; }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
}
