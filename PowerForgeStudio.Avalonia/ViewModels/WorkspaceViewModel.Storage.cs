using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class WorkspaceViewModel
{
    public StorageViewModel Storage { get; }
    [ObservableProperty] private bool _isStoragePage;

    partial void OnIsStoragePageChanged(bool value)
    {
        OnPropertyChanged(nameof(IsFilesPage));
        OnPropertyChanged(nameof(DisplayedOutput));
        OnPropertyChanged(nameof(DisplayedStatus));
    }

    [RelayCommand]
    private async Task ShowStorageAsync()
    {
        if (KeepReleaseVisible())
            return;
        IsReleasePage = false;
        IsGitHubPage = false;
        IsBuildPage = false;
        IsChangesPage = false;
        IsStoragePage = true;
        Storage.SetWorkspace(WorkspaceRoot);
        await Storage.RefreshAsync();
    }
}
