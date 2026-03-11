using System.Windows;
using PowerForgeStudio.Wpf.ViewModels;

namespace PowerForgeStudio.Wpf;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }
}
