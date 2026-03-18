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

        ContentRendered += (_, _) =>
        {
            Activate();
            Focus();
        };

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
