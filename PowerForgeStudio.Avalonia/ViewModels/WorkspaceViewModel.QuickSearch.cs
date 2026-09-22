using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class WorkspaceViewModel
{
    public ObservableCollection<ExplorerNode> QuickProjectMatches { get; } = [];

    [ObservableProperty] private string _quickProjectQuery = "";
    [ObservableProperty] private bool _isQuickProjectSearchOpen;

    public bool HasQuickProjectQuery => !string.IsNullOrWhiteSpace(QuickProjectQuery);
    public bool HasQuickProjectMatches => QuickProjectMatches.Count > 0;

    partial void OnQuickProjectQueryChanged(string value)
    {
        OnPropertyChanged(nameof(HasQuickProjectQuery));
        IsQuickProjectSearchOpen = HasQuickProjectQuery;
        RefreshQuickProjectMatches();
    }

    public void ReopenQuickProjectSearch() => IsQuickProjectSearchOpen = HasQuickProjectQuery;

    private void RefreshQuickProjectMatches()
    {
        QuickProjectMatches.Clear();
        var query = QuickProjectQuery.Trim();
        if (query.Length == 0)
        {
            OnPropertyChanged(nameof(HasQuickProjectMatches));
            return;
        }

        foreach (var entry in _catalog
                     .Where(entry => entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                     entry.RootPath.Contains(query, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(entry => entry.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                     .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                     .Take(8))
        {
            QuickProjectMatches.Add(GetOrCreateProjectNode(entry));
        }
        OnPropertyChanged(nameof(HasQuickProjectMatches));
    }

    public async Task OpenQuickProjectAsync(ExplorerNode node)
    {
        if (KeepReleaseVisible()) return;
        if (node.Kind != "project" || !_catalog.Any(entry => SamePath(entry.RootPath, node.Path))) return;
        QuickProjectQuery = "";
        FavoritesOnly = false;
        ChangedProjectsOnly = false;
        Filter = "";
        ApplyFilter();
        if (_archived.Contains(node.Path))
        {
            var archivedGroup = ExplorerRoots.FirstOrDefault(group => group.Kind == "archive");
            if (archivedGroup is not null) archivedGroup.IsExpanded = true;
        }
        ShowOverviewCommand.Execute(null);
        await node.EnsureLoadedAsync();
        node.IsExpanded = true;
        await SelectAsync(node);
    }
}
