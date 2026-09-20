using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PowerForgeStudio.Avalonia.ViewModels;

namespace PowerForgeStudio.Avalonia;

public sealed partial class MainWindow : Window
{
    private bool? _compactLayout;

    public MainWindow()
    {
        InitializeComponent();
        SizeChanged += (_, args) => ApplyResponsiveLayout(args.NewSize.Width);
    }

    private void ApplyResponsiveLayout(double width)
    {
        var compact = width < 1280;
        if (_compactLayout == compact) return;
        _compactLayout = compact;
        ContextPanel.IsVisible = !compact;
        WorkspaceLayout.ColumnDefinitions[4].Width = new GridLength(compact ? 0 : 280);
        WorkspaceLayout.ColumnDefinitions[1].Width = new GridLength(compact ? 270 : 326);
    }

    private async void ChooseWorkspace(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not WorkspaceViewModel model) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose project workspace", AllowMultiple = false });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } path) return;
        model.WorkspaceRoot = path;
        await model.RefreshCommand.ExecuteAsync(null);
    }

}
