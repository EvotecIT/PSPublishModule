using Avalonia.Controls;
using Avalonia.Interactivity;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Avalonia.Views;

public sealed partial class FileOperationDialog : Window
{
    public FileOperationDialog() => InitializeComponent();

    public FileOperationDialog(WorkspaceViewModel workspace, WorkspaceFileOperation operation) : this()
    {
        DataContext = new FileOperationViewModel(workspace, operation);
        Opened += (_, _) => { DestinationBox.Focus(); DestinationBox.SelectAll(); };
        Closing += (_, args) =>
        {
            if (DataContext is FileOperationViewModel { IsBusy: true } model) { model.CancelOperation(); args.Cancel = true; }
        };
    }

    private void Cancel(object? sender, RoutedEventArgs args)
    {
        if (DataContext is FileOperationViewModel { IsBusy: true } model) model.CancelOperation();
        else Close();
    }

    private async void Apply(object? sender, RoutedEventArgs args)
    {
        if (DataContext is FileOperationViewModel model && await model.SubmitAsync()) Close();
    }
}
