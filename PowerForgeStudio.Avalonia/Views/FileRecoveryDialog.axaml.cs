using Avalonia.Controls;
using Avalonia.Interactivity;
using PowerForgeStudio.Avalonia.ViewModels;

namespace PowerForgeStudio.Avalonia.Views;

public sealed partial class FileRecoveryDialog : Window
{
    public FileRecoveryDialog()
    {
        InitializeComponent();
        Closing += (_, args) =>
        {
            if (DataContext is FileRecoveryViewModel { IsBusy: true })
                args.Cancel = true;
        };
    }

    private async void Refresh(object? sender, RoutedEventArgs args)
    {
        if (DataContext is FileRecoveryViewModel model)
            await model.RefreshAsync();
    }

    private async void Restore(object? sender, RoutedEventArgs args)
    {
        if (DataContext is FileRecoveryViewModel model)
            await model.RestoreSelectedAsync();
    }

    private async void DeletePermanently(object? sender, RoutedEventArgs args)
    {
        if (DataContext is FileRecoveryViewModel model)
            await model.DeleteSelectedPermanentlyAsync();
    }

    private void CloseDialog(object? sender, RoutedEventArgs args) => Close();
}
