using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Domain.Workspace;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class WorkspaceViewModel
{
    public SettingsViewModel Settings { get; private set; } = null!;
    [ObservableProperty] private bool _isSettingsPage;
    private bool _restoreOpenDocuments = true;

    partial void OnIsSettingsPageChanged(bool value)
    {
        OnPropertyChanged(nameof(IsFilesPage));
        OnPropertyChanged(nameof(IsProjectRoute));
        OnPropertyChanged(nameof(IsWorkspaceUtilityPage));
        OnPropertyChanged(nameof(DisplayedOutput));
        OnPropertyChanged(nameof(DisplayedStatus));
    }

    [RelayCommand]
    private void ShowSettings()
    {
        if (KeepReleaseVisible()) return;
        IsOverviewPage = false;
        IsHistoryPage = false;
        IsActivityPage = false;
        IsStoragePage = false;
        IsAutomationsPage = false;
        IsConnectionsPage = false;
        IsPackagesPage = false;
        IsReleasePage = false;
        IsGitHubPage = false;
        IsBuildPage = false;
        IsChangesPage = false;
        IsSettingsPage = true;
        Settings.SetWorkspace(WorkspaceRoot);
    }

    private void ApplyStudioPreferences(WorkspaceStudioPreferences preferences)
    {
        _restoreOpenDocuments = preferences.RestoreOpenDocuments;
        Activity.SetOptions(preferences.ToActivityOptions());
    }
}
