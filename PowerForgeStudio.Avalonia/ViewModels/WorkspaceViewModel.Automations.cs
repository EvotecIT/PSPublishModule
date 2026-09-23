using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Domain.Automation;
using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class WorkspaceViewModel
{
    public AutomationsViewModel Automations { get; private set; } = null!;
    [ObservableProperty] private bool _isAutomationsPage;
    public bool IsWorkspaceUtilityPage => IsSettingsPage || IsActivityPage || IsStoragePage || IsAutomationsPage || IsConnectionsPage || IsPackagesPage;

    partial void OnIsAutomationsPageChanged(bool value)
    {
        OnPropertyChanged(nameof(IsFilesPage));
        OnPropertyChanged(nameof(IsProjectRoute));
        OnPropertyChanged(nameof(IsWorkspaceUtilityPage));
        OnPropertyChanged(nameof(DisplayedOutput));
        OnPropertyChanged(nameof(DisplayedStatus));
    }

    [RelayCommand]
    private async Task ShowAutomationsAsync()
    {
        if (KeepReleaseVisible()) return;
        IsOverviewPage = false;
        IsHistoryPage = false;
        IsSettingsPage = false;
        IsActivityPage = false;
        IsStoragePage = false;
        IsConnectionsPage = false;
        IsPackagesPage = false;
        IsReleasePage = false;
        IsGitHubPage = false;
        IsBuildPage = false;
        IsChangesPage = false;
        IsAutomationsPage = true;
        Automations.SetWorkspace(WorkspaceRoot);
        await Automations.RefreshAsync();
    }

    private async Task OpenAutomationSourceAsync(WorkspaceAutomationEntry entry)
    {
        if (KeepReleaseVisible()) throw new InvalidOperationException("Finish the active release before opening another project file.");
        if (entry.Provider != "GitHub Actions" || !Path.IsPathFullyQualified(entry.SourcePath))
            throw new InvalidOperationException("Only local GitHub workflow definitions can open in Files.");

        var source = Path.GetFullPath(entry.SourcePath);
        var workflows = Path.GetDirectoryName(source);
        var github = workflows is null ? null : Path.GetDirectoryName(workflows);
        var root = github is null ? null : Path.GetDirectoryName(github);
        if (root is null || !string.Equals(Path.GetFileName(workflows), "workflows", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(github), ".github", StringComparison.OrdinalIgnoreCase) ||
            !WorkspacePathContainment.ContainsOrEquals(WorkspaceRoot, root))
            throw new InvalidOperationException("The workflow file is outside a discovered project.");
        var project = _catalog.FirstOrDefault(item => SamePath(item.RootPath, root));
        if (project is null || !string.Equals(project.Name, entry.Project, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(source) || !(source.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
                                     source.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The workflow file is no longer available in its discovered project. Refresh Automations.");

        for (var current = source; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Workflow files reached through symbolic links or junctions cannot open in Studio.");
            if (SamePath(current, WorkspaceRoot)) break;
        }
        await SelectAsync(new ExplorerNode(Path.GetFileName(source), source, "file", root));
    }
}
