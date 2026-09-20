using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class WorkspaceViewModel
{
    public AutomationsViewModel Automations { get; private set; } = null!;
    [ObservableProperty] private bool _isAutomationsPage;
    public bool IsWorkspaceUtilityPage => IsStoragePage || IsAutomationsPage;

    partial void OnIsAutomationsPageChanged(bool value)
    {
        OnPropertyChanged(nameof(IsFilesPage));
        OnPropertyChanged(nameof(IsWorkspaceUtilityPage));
        OnPropertyChanged(nameof(DisplayedOutput));
        OnPropertyChanged(nameof(DisplayedStatus));
    }

    [RelayCommand]
    private async Task ShowAutomationsAsync()
    {
        if (KeepReleaseVisible()) return;
        IsStoragePage = false;
        IsReleasePage = false;
        IsGitHubPage = false;
        IsBuildPage = false;
        IsChangesPage = false;
        IsAutomationsPage = true;
        Automations.SetWorkspace(WorkspaceRoot);
        await Automations.RefreshAsync();
    }
}
