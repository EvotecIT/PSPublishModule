using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class WorkspaceViewModel
{
    public ActivityViewModel Activity { get; private set; } = null!;
    [ObservableProperty] private bool _isActivityPage;

    partial void OnIsActivityPageChanged(bool value)
    {
        OnPropertyChanged(nameof(IsFilesPage));
        OnPropertyChanged(nameof(IsProjectRoute));
        OnPropertyChanged(nameof(IsWorkspaceUtilityPage));
        OnPropertyChanged(nameof(DisplayedOutput));
        OnPropertyChanged(nameof(DisplayedStatus));
    }

    [RelayCommand]
    private async Task ShowActivityAsync()
    {
        if (KeepReleaseVisible()) return;
        IsOverviewPage = false;
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
        await Activity.RefreshAsync();
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
