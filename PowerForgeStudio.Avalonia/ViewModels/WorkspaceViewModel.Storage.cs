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
        OnPropertyChanged(nameof(IsProjectRoute));
        OnPropertyChanged(nameof(IsWorkspaceUtilityPage));
        OnPropertyChanged(nameof(DisplayedOutput));
        OnPropertyChanged(nameof(DisplayedStatus));
    }

    [RelayCommand]
    private async Task ShowStorageAsync()
    {
        if (KeepReleaseVisible())
            return;
        IsOverviewPage = false;
        IsHistoryPage = false;
        IsSettingsPage = false;
        IsActivityPage = false;
        IsAutomationsPage = false;
        IsConnectionsPage = false;
        IsPackagesPage = false;
        IsReleasePage = false;
        IsGitHubPage = false;
        IsBuildPage = false;
        IsChangesPage = false;
        IsStoragePage = true;
        Storage.SetWorkspace(WorkspaceRoot);
        await Storage.RefreshAsync();
    }

    private IReadOnlyCollection<string> GetProtectedWorkingCopies()
    {
        var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(ActiveWorkingCopyRoot)) paths.Add(ActiveWorkingCopyRoot);
        foreach (var document in Documents) paths.Add(document.Reference.WorkingCopyRoot);
        if (Build.IsBuilding && !string.IsNullOrWhiteSpace(Build.BuildRoot)) paths.Add(Build.BuildRoot);
        if (Build.IsTaskRunning && !string.IsNullOrWhiteSpace(Build.TaskRoot)) paths.Add(Build.TaskRoot);
        if (Release.HasActiveReleaseOperation && !string.IsNullOrWhiteSpace(Release.BuildRoot)) paths.Add(Release.BuildRoot);
        return paths;
    }
}
