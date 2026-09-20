using Avalonia.Controls;
using Avalonia.Interactivity;
using PowerForgeStudio.Avalonia.ViewModels;

namespace PowerForgeStudio.Avalonia.Views;

public sealed partial class WorktreeRemovalDialog : Window
{
    public WorktreeRemovalDialog()
    {
        InitializeComponent();
        Closing += (_, args) =>
        {
            if (DataContext is StorageViewModel { IsRemoving: true }) args.Cancel = true;
        };
    }

    private async void RefreshEvidence(object? sender, RoutedEventArgs args)
    {
        if (DataContext is StorageViewModel model) await model.ReviewSelectedRemovalAsync();
    }

    private void Cancel(object? sender, RoutedEventArgs args) => Close();

    private async void Remove(object? sender, RoutedEventArgs args)
    {
        if (DataContext is StorageViewModel model && await model.RemoveReviewedAsync()) Close();
    }
}
