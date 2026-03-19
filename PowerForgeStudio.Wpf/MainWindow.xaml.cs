using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using PowerForgeStudio.Wpf.Themes;
using PowerForgeStudio.Wpf.ViewModels;
using PowerForgeStudio.Wpf.ViewModels.Hub;

namespace PowerForgeStudio.Wpf;

public partial class MainWindow : Window
{
    private readonly HubShellViewModel _viewModel;
    private readonly ThemeService _themeService;
    private bool _isLoaded;

    public MainWindow()
        : this(new HubShellViewModel(
            new ShellViewModel(),
            new HubViewModel()))
    {
    }

    public MainWindow(HubShellViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _themeService = ((App)Application.Current).ThemeService;

        InitializeComponent();
        DataContext = _viewModel;

        SourceInitialized += (_, _) =>
        {
            ThemeService.SetDarkTitleBar(this, _themeService.ActiveTheme.IsDarkTitleBar);
        };

        // Restore window position/size
        WindowStateService.RestoreState(this);

        // Save state and cleanup on close
        Closing += (_, _) =>
        {
            var lastProject = (_viewModel.ActiveContent as HubViewModel)?.SelectedProject?.Name;
            WindowStateService.SaveState(this, lastProject);
            _themeService.ThemeChanged -= OnThemeChanged;
        };

        ContentRendered += (_, _) =>
        {
            Activate();
            Focus();
        };

        // Keyboard shortcuts
        CommandBindings.Add(new CommandBinding(HubCommands.FocusSearchCommand, (_, _) =>
        {
            var searchBox = FindVisualChild<TextBox>(this, "ProjectSearchBox");
            searchBox?.Focus();
            searchBox?.SelectAll();
        }));
        CommandBindings.Add(new CommandBinding(HubCommands.OpenTerminalCommand, (_, _) =>
        {
            if (_viewModel.ActiveContent is HubViewModel hub && hub.ActiveWorkspace is not null)
            {
                hub.ActiveWorkspace.SelectedTabIndex = 4; // Terminal
            }
        }));
        CommandBindings.Add(new CommandBinding(HubCommands.OpenFilesCommand, (_, _) =>
        {
            if (_viewModel.ActiveContent is HubViewModel hub && hub.ActiveWorkspace is not null)
            {
                hub.ActiveWorkspace.SelectedTabIndex = 5; // Files
            }
        }));
        CommandBindings.Add(new CommandBinding(HubCommands.ClearSearchCommand, (_, _) =>
        {
            if (_viewModel.ActiveContent is HubViewModel hub)
            {
                hub.SearchText = string.Empty;
            }
        }));

        // Set initial theme selector
        SetThemeSelectorValue(_themeService.CurrentMode);

        // Listen for theme changes (e.g. from system Auto switch)
        _themeService.ThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged(ThemeDefinition theme)
    {
        ThemeService.SetDarkTitleBar(this, theme.IsDarkTitleBar);
    }

    private void ThemeSelector_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeSelector.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            if (Enum.TryParse<AppThemeMode>(tag, true, out var mode))
            {
                _themeService.CurrentMode = mode;
            }
        }
    }

    private void SetThemeSelectorValue(AppThemeMode mode)
    {
        var tag = mode.ToString();
        foreach (ComboBoxItem item in ThemeSelector.Items)
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                ThemeSelector.SelectedItem = item;
                break;
            }
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        await Dispatcher.Yield(DispatcherPriority.Background);

        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "PowerForge Studio startup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T element && element.Name == name)
            {
                return element;
            }

            var found = FindVisualChild<T>(child, name);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private void IssueList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox { SelectedItem: Domain.Hub.GitHubIssue issue }
            && _viewModel.ActiveContent is HubViewModel hub
            && hub.ActiveWorkspace?.GitHubSlug is not null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(
                    $"https://github.com/{hub.ActiveWorkspace.GitHubSlug}/issues/{issue.Number}")
                { UseShellExecute = true });
            }
            catch { }
        }
    }

    private void PrList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox { SelectedItem: Domain.Hub.GitHubPullRequest pr }
            && _viewModel.ActiveContent is HubViewModel hub
            && hub.ActiveWorkspace?.GitHubSlug is not null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(
                    $"https://github.com/{hub.ActiveWorkspace.GitHubSlug}/pull/{pr.Number}")
                { UseShellExecute = true });
            }
            catch { }
        }
    }

    private void GitHubSlug_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProjectWorkspaceViewModel workspace }
            && workspace.GitHubSlug is not null)
        {
            try
            {
                Process.Start(new ProcessStartInfo($"https://github.com/{workspace.GitHubSlug}")
                {
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    private void WorktreeItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProjectListItemViewModel worktreeVm }
            && _viewModel.ActiveContent is HubViewModel hub)
        {
            _ = hub.OnProjectSelectedAsync(worktreeVm);
            e.Handled = true;
        }
    }
}
