using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Domain.Publish;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class WorkspaceViewModel
{
    public PackagesViewModel Packages { get; private set; } = null!;
    [ObservableProperty] private bool _isPackagesPage;

    partial void OnIsPackagesPageChanged(bool value)
    {
        OnPropertyChanged(nameof(IsFilesPage));
        OnPropertyChanged(nameof(IsProjectRoute));
        OnPropertyChanged(nameof(IsWorkspaceUtilityPage));
        OnPropertyChanged(nameof(DisplayedOutput));
        OnPropertyChanged(nameof(DisplayedStatus));
    }

    [RelayCommand]
    private async Task ShowPackagesAsync()
    {
        if (KeepReleaseVisible()) return;
        IsOverviewPage = false;
        IsHistoryPage = false;
        IsSettingsPage = false;
        IsActivityPage = false;
        IsStoragePage = false;
        IsAutomationsPage = false;
        IsConnectionsPage = false;
        IsReleasePage = false;
        IsGitHubPage = false;
        IsBuildPage = false;
        IsChangesPage = false;
        IsPackagesPage = true;
        if (!Packages.HasLoaded) await Packages.RefreshAsync();
    }

    private async Task OpenPublicPackageAsync(ReleasePublishReceipt receipt)
    {
        if (!receipt.CanInspectPublicPackage || KeepReleaseVisible()) return;
        await ShowPackagesAsync();
        if (IsPackagesPage) Packages.FocusPublishedPackage(receipt);
    }
}
