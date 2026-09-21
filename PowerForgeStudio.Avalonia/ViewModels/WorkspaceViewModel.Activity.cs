using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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

    private Task RefreshActiveUtilityPageAsync()
    {
        if (IsActivityPage) return Activity.RefreshAsync();
        if (IsStoragePage) return Storage.RefreshAsync();
        if (IsAutomationsPage) return Automations.RefreshAsync();
        if (IsConnectionsPage) return Connections.RefreshAsync();
        if (IsSettingsPage && Settings.CanReload) Settings.Reload();
        return Task.CompletedTask;
    }
}
