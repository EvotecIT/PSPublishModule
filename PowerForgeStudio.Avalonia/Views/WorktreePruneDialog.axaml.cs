using Avalonia.Controls;
using Avalonia.Interactivity;
using PowerForgeStudio.Avalonia.ViewModels;

namespace PowerForgeStudio.Avalonia.Views;

public sealed partial class WorktreePruneDialog : Window
{
    public WorktreePruneDialog()
    {
        InitializeComponent();
        Closing += (_, args) =>
        {
            if (DataContext is StorageViewModel { IsPruning: true }) args.Cancel = true;
        };
    }

    private async void RefreshEvidence(object? sender, RoutedEventArgs args)
    {
        if (DataContext is StorageViewModel model) await model.ReviewSelectedPruneAsync();
    }

    private void Cancel(object? sender, RoutedEventArgs args) => Close();

    private async void Prune(object? sender, RoutedEventArgs args)
    {
        if (DataContext is StorageViewModel model && await model.PruneReviewedAsync()) Close();
    }
}
