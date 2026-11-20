using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PowerForge.Compiler.App.ViewModels;

namespace PowerForge.Compiler.App;

/// <summary>The single window: drop zone, mode cards, problems and build output.</summary>
public sealed partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;

    /// <summary>Creates the window.</summary>
    public MainWindow()
    {
        InitializeComponent();
        DropZone.AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        DropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        DropZone.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        DropZone.AddHandler(DragDrop.DropEvent, OnDrop);

        // The window stays dark so its title bar reads on the navy band; the body follows the system theme.
        if (Application.Current is { } application)
        {
            BodyScope.RequestedThemeVariant = application.ActualThemeVariant;
            application.ActualThemeVariantChanged += (_, _) => BodyScope.RequestedThemeVariant = application.ActualThemeVariant;
        }
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Log.CollectionChanged -= OnLogChanged;
        }

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is null) return;
        _viewModel.OpenFolder = OpenFolderAsync;
        _viewModel.PickSettingsSavePath = PickSettingsSavePathAsync;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Log.CollectionChanged += OnLogChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null) return;
        if (e.PropertyName == nameof(MainWindowViewModel.IsCompiling))
        {
            // Show live output while building; show problems if the build failed.
            Tabs.SelectedIndex = _viewModel.IsCompiling || _viewModel.HasResult || !_viewModel.HasDiagnostics ? 2 : 0;
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.IsChecking) && _viewModel.IsChecking)
        {
            Tabs.SelectedIndex = 0;
        }
    }

    private void OnLogChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        // Scroll after the new line has been measured, so the last line is fully visible.
        Dispatcher.UIThread.Post(LogScroll.ScrollToEnd, DispatcherPriority.Background);

    private static string? GetDroppedPath(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath();

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        var accept = _viewModel is { IsBusy: false } && GetDroppedPath(e) is not null;
        e.DragEffects = accept ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.IsVisible = accept;
    }

    // The app only reads the dropped path, so it always reports Copy or None; never Move, which could make the source delete the file.
    private void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = _viewModel is { IsBusy: false } && GetDroppedPath(e) is not null ? DragDropEffects.Copy : DragDropEffects.None;

    private void OnDragLeave(object? sender, DragEventArgs e) => DropOverlay.IsVisible = false;

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
        if (_viewModel is not { IsBusy: false } || GetDroppedPath(e) is not { } path)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        await _viewModel.SetInputAsync(path);
    }

    private async void OnChooseFile(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a PowerShell script or module",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("PowerShell") { Patterns = new[] { "*.ps1", "*.psm1", "*.psd1" } },
                FilePickerFileTypes.All
            }
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) await _viewModel.SetInputAsync(path);
    }

    private async void OnChooseFolder(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a module folder",
            AllowMultiple = false
        });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) await _viewModel.SetInputAsync(path);
    }

    private async void OnChooseOutput(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose where to write the output",
            AllowMultiple = false
        });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) _viewModel.OutputDirectory = path;
    }

    private async void OnOpenOutput(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null) await _viewModel.OpenOutputAsync();
    }

    private async Task OpenFolderAsync(string folder)
    {
        if (Directory.Exists(folder)) await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder));
    }

    /// <inheritdoc />
    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && _viewModel is { IsDetailsOpen: true })
        {
            e.Handled = true;
            await _viewModel.CloseDetailsAsync();
        }
    }

    private void OnOpenDetails(object? sender, RoutedEventArgs e) => _viewModel?.OpenDetails();

    private async void OnCloseDetails(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null) await _viewModel.CloseDetailsAsync();
    }

    private async void OnDetailsScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is not null) await _viewModel.CloseDetailsAsync();
    }

    private async void OnOpenSettings(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open saved build settings",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("PowerForge settings") { Patterns = new[] { "*.psproject.json" } },
                FilePickerFileTypes.All
            }
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) await _viewModel.OpenSettingsAsync(path);
    }

    private async void OnSaveSettings(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null) await _viewModel.SaveSettingsAsync();
    }

    private async void OnSaveSettingsAs(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null) await _viewModel.SaveSettingsAsAsync();
    }

    private async Task<string?> PickSettingsSavePathAsync(string suggestedPath)
    {
        var folder = await StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(suggestedPath) ?? string.Empty);
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save build settings",
            SuggestedFileName = Path.GetFileName(suggestedPath),
            SuggestedStartLocation = folder,
            DefaultExtension = "json",
            FileTypeChoices = new[] { new FilePickerFileType("PowerForge settings") { Patterns = new[] { "*.psproject.json" } } }
        });
        return file?.TryGetLocalPath();
    }
}
