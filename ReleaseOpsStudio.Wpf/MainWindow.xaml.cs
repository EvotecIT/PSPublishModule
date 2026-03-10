using System.Windows;
using ReleaseOpsStudio.Wpf.ViewModels;

namespace ReleaseOpsStudio.Wpf;

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
