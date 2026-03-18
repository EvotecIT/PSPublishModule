using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using PowerForgeStudio.Wpf.ViewModels;
using PowerForgeStudio.Wpf.ViewModels.Hub;

namespace PowerForgeStudio.Wpf;

public partial class MainWindow : Window
{
    private readonly HubShellViewModel _viewModel;
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
        InitializeComponent();
        DataContext = _viewModel;

        SourceInitialized += (_, _) => EnableDarkTitleBar();
        ContentRendered += (_, _) =>
        {
            Activate();
            Focus();
        };
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

    private void EnableDarkTitleBar()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            // DWMWA_USE_IMMERSIVE_DARK_MODE = 20
            var value = 1;
            DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int));
        }
        catch
        {
            // Older Windows versions don't support this
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);
}
