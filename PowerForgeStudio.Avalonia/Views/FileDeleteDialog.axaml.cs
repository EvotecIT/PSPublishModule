using Avalonia.Controls;
using Avalonia.Interactivity;
using PowerForgeStudio.Avalonia.ViewModels;

namespace PowerForgeStudio.Avalonia.Views;

public sealed partial class FileDeleteDialog : Window
{
    public FileDeleteDialog()
    {
        InitializeComponent();
        Closing += (_, args) =>
        {
            if (DataContext is not FileDeleteViewModel { IsBusy: true } model)
                return;
            model.CancelOperation();
            args.Cancel = true;
        };
    }

    private void Cancel(object? sender, RoutedEventArgs args)
    {
        if (DataContext is FileDeleteViewModel { IsBusy: true } model)
            model.CancelOperation();
        else
            Close();
    }

    private async void Apply(object? sender, RoutedEventArgs args)
    {
        if (DataContext is FileDeleteViewModel model && await model.SubmitAsync())
            Close();
    }
}
