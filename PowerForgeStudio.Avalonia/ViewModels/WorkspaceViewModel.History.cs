using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class WorkspaceViewModel
{
    public ProjectHistoryViewModel History { get; private set; } = null!;
    [ObservableProperty] private bool _isHistoryPage;

    partial void OnIsHistoryPageChanged(bool value)
    {
        OnPropertyChanged(nameof(IsFilesPage));
        OnPropertyChanged(nameof(IsProjectRoute));
        OnPropertyChanged(nameof(DisplayedOutput));
        OnPropertyChanged(nameof(DisplayedStatus));
        OnPropertyChanged(nameof(OutputPaneHeight));
    }

    [RelayCommand]
    private async Task ShowHistoryAsync()
    {
        if (KeepReleaseVisible() || !HasGitWorkingCopy) return;
        IsOverviewPage = false;
        IsSettingsPage = false;
        IsActivityPage = false;
        IsStoragePage = false;
        IsAutomationsPage = false;
        IsConnectionsPage = false;
        IsPackagesPage = false;
        IsReleasePage = false;
        IsGitHubPage = false;
        IsBuildPage = false;
        IsChangesPage = false;
        IsHistoryPage = true;
        await History.RefreshAsync();
    }
}
