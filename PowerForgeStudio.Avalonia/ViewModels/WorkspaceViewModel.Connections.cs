using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class WorkspaceViewModel
{
    public ConnectionsViewModel Connections { get; private set; } = null!;
    [ObservableProperty] private bool _isConnectionsPage;

    partial void OnIsConnectionsPageChanged(bool value)
    {
        OnPropertyChanged(nameof(IsFilesPage));
        OnPropertyChanged(nameof(IsWorkspaceUtilityPage));
        OnPropertyChanged(nameof(DisplayedOutput));
        OnPropertyChanged(nameof(DisplayedStatus));
    }

    [RelayCommand]
    private async Task ShowConnectionsAsync()
    {
        if (KeepReleaseVisible()) return;
        IsActivityPage = false;
        IsStoragePage = false;
        IsAutomationsPage = false;
        IsReleasePage = false;
        IsGitHubPage = false;
        IsBuildPage = false;
        IsChangesPage = false;
        IsConnectionsPage = true;
        Connections.SetWorkspace(WorkspaceRoot);
        await Connections.RefreshAsync();
    }
}
