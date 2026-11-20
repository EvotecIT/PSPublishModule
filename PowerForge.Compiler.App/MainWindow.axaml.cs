using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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
        DropZone.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        DropZone.AddHandler(DragDrop.DropEvent, OnDrop);
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
        LogScroll.ScrollToEnd();

    private static string? GetDroppedPath(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath();

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        var accept = _viewModel is { IsBusy: false } && GetDroppedPath(e) is not null;
        e.DragEffects = accept ? DragDropEffects.Copy : DragDropEffects.None;
        DropZone.Classes.Set("dragover", accept);
    }

    private void OnDragLeave(object? sender, DragEventArgs e) => DropZone.Classes.Set("dragover", false);

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        DropZone.Classes.Set("dragover", false);
        if (_viewModel is not { IsBusy: false } || GetDroppedPath(e) is not { } path) return;
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
}
