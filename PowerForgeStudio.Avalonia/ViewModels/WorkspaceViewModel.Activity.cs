using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Domain.Activity;
using PowerForgeStudio.Orchestrator.Explorer;
using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class WorkspaceViewModel
{
    public ActivityViewModel Activity { get; private set; } = null!;
    [ObservableProperty] private bool _isActivityPage;
    public bool IsActivityRailSelected => IsActivityPage && !Activity.IsGitHubFilter;
    public bool IsGitHubActivityPage => IsActivityPage && Activity.IsGitHubFilter;

    partial void OnIsActivityPageChanged(bool value)
    {
        OnPropertyChanged(nameof(IsFilesPage));
        OnPropertyChanged(nameof(IsProjectRoute));
        OnPropertyChanged(nameof(IsWorkspaceUtilityPage));
        OnPropertyChanged(nameof(IsActivityRailSelected));
        OnPropertyChanged(nameof(IsGitHubActivityPage));
        OnPropertyChanged(nameof(DisplayedOutput));
        OnPropertyChanged(nameof(DisplayedStatus));
    }

    [RelayCommand]
    private Task ShowActivityAsync() => ShowActivityAsync(gitHubOnly: false);

    [RelayCommand]
    private Task ShowGitHubActivityAsync() => ShowActivityAsync(gitHubOnly: true);

    private async Task ShowActivityAsync(bool gitHubOnly)
    {
        if (KeepReleaseVisible()) return;
        var hasCurrentEvidence = IsActivityPage && SamePath(Activity.WorkspaceRoot, WorkspaceRoot);
        IsOverviewPage = false;
        IsHistoryPage = false;
        IsSettingsPage = false;
        IsStoragePage = false;
        IsAutomationsPage = false;
        IsConnectionsPage = false;
        IsPackagesPage = false;
        IsReleasePage = false;
        IsGitHubPage = false;
        IsBuildPage = false;
        IsChangesPage = false;
        IsActivityPage = true;
        Activity.SetWorkspace(WorkspaceRoot);
        if (gitHubOnly) Activity.ShowGitHubCommand.Execute(null);
        else Activity.ShowAttentionCommand.Execute(null);
        if (!hasCurrentEvidence) await Activity.RefreshAsync();
    }

    private async Task OpenActivityReleaseAsync(WorkspaceActivityEntry entry)
    {
        if (_disposed || Release.HasProtectedReleaseWork || string.IsNullOrWhiteSpace(entry.ReleaseSessionId)) return;
        var workspace = WorkspaceRoot;
        var workingCopy = Path.TrimEndingDirectorySeparator(Path.GetFullPath(entry.Source));
        if (!Directory.Exists(workingCopy) || !WorkspacePathContainment.ContainsOrEquals(workspace, workingCopy))
            throw new InvalidOperationException("The saved release working copy is no longer available in this workspace.");

        await SelectAsync(new ExplorerNode(Path.GetFileName(workingCopy), workingCopy, "branch", workingCopy));
        if (_disposed || !SamePath(workspace, WorkspaceRoot) || !SamePath(workingCopy, ActiveWorkingCopyRoot)) return;
        ShowReleaseCommand.Execute(null);
        await Release.RefreshHistoryAsync();
        if (_disposed || !IsReleasePage || !SamePath(workingCopy, ActiveWorkingCopyRoot)) return;
        await Release.OpenHistorySessionAsync(entry.ReleaseSessionId);
    }

    private Task RefreshActiveUtilityPageAsync()
    {
        if (IsActivityPage) return Activity.RefreshAsync();
        if (IsStoragePage) return Storage.RefreshAsync();
        if (IsAutomationsPage) return Automations.RefreshAsync();
        if (IsConnectionsPage) return Connections.RefreshAsync();
        if (IsPackagesPage) return Packages.RefreshAsync();
        if (IsSettingsPage && Settings.CanReload) Settings.Reload();
        return Task.CompletedTask;
    }
}
