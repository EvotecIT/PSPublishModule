using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Avalonia;

public sealed partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private async void ChooseWorkspace(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not WorkspaceViewModel model) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose project workspace", AllowMultiple = false });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } path) return;
        model.WorkspaceRoot = path;
        await model.RefreshCommand.ExecuteAsync(null);
    }

    private async void OpenFile(object? sender, TappedEventArgs args)
    {
        if (DataContext is WorkspaceViewModel model && FileList.SelectedItem is FileSystemEntry entry)
            await model.OpenEntryAsync(entry);
    }
}
