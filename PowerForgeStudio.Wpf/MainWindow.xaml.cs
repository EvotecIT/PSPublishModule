using System.Windows;
using System.Windows.Threading;
using PowerForgeStudio.Wpf.ViewModels;

namespace PowerForgeStudio.Wpf;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel;
    private bool _isLoaded;

    public MainWindow()
        : this(new ShellViewModel())
    {
    }

    public MainWindow(ShellViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
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
}
