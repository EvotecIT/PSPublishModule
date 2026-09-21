using Avalonia.Controls;
using Avalonia.Interactivity;
using PowerForgeStudio.Avalonia.ViewModels;

namespace PowerForgeStudio.Avalonia.Views;

public sealed partial class GitHubActionReviewDialog : Window
{
    public GitHubActionReviewDialog()
    {
        InitializeComponent();
        Closing += (_, args) =>
        {
            if (DataContext is GitHubViewModel { IsActionRunning: true }) args.Cancel = true;
        };
    }

    private void Cancel(object? sender, RoutedEventArgs args)
    {
        if (DataContext is GitHubViewModel model) model.CancelPendingAction();
        Close();
    }

    private async void Submit(object? sender, RoutedEventArgs args)
    {
        if (DataContext is GitHubViewModel model && await model.SubmitPreparedActionAsync()) Close();
    }
}
