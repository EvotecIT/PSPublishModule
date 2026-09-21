using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class WorkspaceViewModel
{
    public AutomationsViewModel Automations { get; private set; } = null!;
    [ObservableProperty] private bool _isAutomationsPage;
    public bool IsWorkspaceUtilityPage => IsSettingsPage || IsActivityPage || IsStoragePage || IsAutomationsPage || IsConnectionsPage;

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
        IsSettingsPage = false;
        IsActivityPage = false;
        IsStoragePage = false;
        IsConnectionsPage = false;
        IsReleasePage = false;
        IsGitHubPage = false;
        IsBuildPage = false;
        IsChangesPage = false;
        IsAutomationsPage = true;
        Automations.SetWorkspace(WorkspaceRoot);
        await Automations.RefreshAsync();
    }
}
