using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Avalonia.Views;

namespace PowerForgeStudio.Avalonia;

public sealed partial class MainWindow : Window
{
    private bool? _compactLayout;
    private bool _compactDetailsOpen;
    private bool _hasBeenActivated;
    private bool _closeAfterSave;
    private bool _savingOnClose;
    private bool _discardOnNextClose;

    public MainWindow()
    {
        InitializeComponent();
        SizeChanged += (_, args) => ApplyResponsiveLayout(args.NewSize.Width);
        Activated += (_, _) =>
        {
            if (!_hasBeenActivated) { _hasBeenActivated = true; return; }
            if (DataContext is WorkspaceViewModel model) _ = model.RefreshVisibleChangesAsync();
        };
        KeyDown += FocusQuickProjectSearch;
        Closing += SaveBeforeClosing;
        PropertyChanged += (_, args) =>
        {
            if (args.Property == WindowStateProperty)
                MaximizeWindowIcon.Kind = WindowState == WindowState.Maximized ? "restore" : "maximize";
        };
        DataContextChanged += (_, _) =>
        {
            if (DataContext is WorkspaceViewModel model)
            {
                model.ResolveUnsavedChanges = documents => new UnsavedChangesDialog(documents).ShowDialog<UnsavedChangesChoice>(this);
                if (_compactLayout is { } compact) model.CompactViewport = compact;
            }
        };
    }

    private void MinimizeWindow(object? sender, RoutedEventArgs args) => WindowState = WindowState.Minimized;

    private void ToggleMaximizeWindow(object? sender, RoutedEventArgs args)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseWindow(object? sender, RoutedEventArgs args) => Close();

    private void FocusQuickProjectSearch(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.K || !args.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        QuickProjectSearchBox.Focus();
        QuickProjectSearchBox.SelectAll();
        if (DataContext is WorkspaceViewModel model) model.ReopenQuickProjectSearch();
        args.Handled = true;
    }

    private void QuickProjectSearchGotFocus(object? sender, RoutedEventArgs args)
    {
        if (DataContext is WorkspaceViewModel model) model.ReopenQuickProjectSearch();
    }

    private void ApplyResponsiveLayout(double width)
    {
        var compact = width < 1280;
        if (_compactLayout == compact) return;
        if (compact && _compactLayout == false) _compactDetailsOpen = false;
        _compactLayout = compact;
        if (DataContext is WorkspaceViewModel model) model.CompactViewport = compact;
        UpdateContextLayout();
    }

    private void ToggleCompactContext(object? sender, RoutedEventArgs args)
    {
        if (_compactLayout != true) return;
        _compactDetailsOpen = !_compactDetailsOpen;
        UpdateContextLayout();
    }

    private void ShowProjectsFromRail(object? sender, RoutedEventArgs args)
    {
        if (_compactLayout != true || !_compactDetailsOpen) return;
        _compactDetailsOpen = false;
        UpdateContextLayout();
    }

    private void UpdateContextLayout()
    {
        var compact = _compactLayout == true;
        var showContext = !compact || _compactDetailsOpen;
        ContextPanel.IsVisible = showContext;
        ProjectTreePanel.IsVisible = !compact || !_compactDetailsOpen;
        CompactContextButton.IsVisible = compact;
        CompactContextButton.Content = _compactDetailsOpen ? "Projects" : "Details";
        global::Avalonia.Automation.AutomationProperties.SetName(CompactContextButton,
            _compactDetailsOpen ? "Show projects" : "Show details");
        ToolTip.SetTip(CompactContextButton,
            _compactDetailsOpen ? "Show project tree" : "Show selected item details");
        WorkspaceLayout.ColumnDefinitions[4].Width = new GridLength(showContext ? (compact ? 300 : 280) : 0);
        WorkspaceLayout.ColumnDefinitions[1].Width = new GridLength(compact ? (_compactDetailsOpen ? 0 : 270) : 326);
    }

    private async void QuickProjectSearchKeyDown(object? sender, KeyEventArgs args)
    {
        if (DataContext is not WorkspaceViewModel model) return;
        if (args.Key == Key.Escape)
        {
            model.QuickProjectQuery = "";
            args.Handled = true;
        }
        else if (args.Key == Key.Enter && model.QuickProjectMatches.FirstOrDefault() is { } first)
        {
            await model.OpenQuickProjectAsync(first);
            args.Handled = true;
        }
    }

    private async void ChooseQuickProject(object? sender, RoutedEventArgs args)
    {
        if (DataContext is WorkspaceViewModel model && sender is Button { Tag: ExplorerNode node })
            await model.OpenQuickProjectAsync(node);
    }

    private async void ChooseWorkspace(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not WorkspaceViewModel model) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose project workspace", AllowMultiple = false });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } path) return;
        await model.SaveSessionAsync();
        if (!await model.ActivateWorkspaceAsync(path)) return;
        model.WorkspaceRoot = path;
        await model.RefreshCommand.ExecuteAsync(null);
    }

    private async void SaveBeforeClosing(object? sender, WindowClosingEventArgs args)
    {
        if (DataContext is not WorkspaceViewModel model) return;
        if (model.Release.HasProtectedReleaseWork)
        {
            _closeAfterSave = false;
            _discardOnNextClose = false;
            args.Cancel = true;
            model.ShowReleaseCommand.Execute(null);
            model.Release.Status = model.Release.HasUnpersistedEvidence ? "Save or explicitly discard the unsaved receipts before closing."
                : model.Release.IsPublishing ? "Publication is still running. Cancel it or wait for receipt capture before closing."
                : model.Release.IsVerifying ? "Verification is still running. Cancel it or wait for receipt capture before closing."
                : "Signing is still running. Cancel signing or wait for completion before closing.";
            return;
        }
        if (model.Settings.IsDirty)
        {
            _closeAfterSave = false;
            args.Cancel = true;
            model.ShowSettingsCommand.Execute(null);
            model.Settings.Status = "Save or explicitly discard the unsaved settings before closing.";
            return;
        }
        if (_closeAfterSave) return;
        args.Cancel = true;
        if (_savingOnClose) return;
        _savingOnClose = true;
        if (!await model.ResolveUnsavedDocumentsAsync()) { _savingOnClose = false; return; }
        if (_discardOnNextClose && model.HasStateError) { _savingOnClose = false; _closeAfterSave = true; Close(); return; }
        await model.SaveSessionAsync();
        _savingOnClose = false;
        if (model.HasStateError)
        {
            _discardOnNextClose = true;
            model.StateError += " Close the window again to exit without saving.";
            return;
        }
        _closeAfterSave = true;
        Close();
    }

}
