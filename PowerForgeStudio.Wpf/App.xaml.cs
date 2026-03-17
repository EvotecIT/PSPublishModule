using System.Windows;
using PowerForgeStudio.Wpf.ViewModels;

namespace PowerForgeStudio.Wpf;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var shellViewModel = new ShellViewModel(ShellViewModelServices.CreateDefault());
        var mainWindow = new MainWindow(shellViewModel);
        MainWindow = mainWindow;
        mainWindow.Show();
    }
}

