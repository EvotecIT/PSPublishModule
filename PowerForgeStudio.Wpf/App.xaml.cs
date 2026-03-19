using System.Windows;
using System.Windows.Threading;
using PowerForgeStudio.Wpf.Themes;
using PowerForgeStudio.Wpf.ViewModels;
using PowerForgeStudio.Wpf.ViewModels.Hub;

namespace PowerForgeStudio.Wpf;

public partial class App : Application
{
    public ThemeService ThemeService { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            // Initialize theme before creating any UI
            ThemeService.Initialize();

            var shellViewModel = new ShellViewModel(ShellViewModelServices.CreateDefault());
            var hubViewModel = new HubViewModel();
            var hubShellViewModel = new HubShellViewModel(shellViewModel, hubViewModel);
            var mainWindow = new MainWindow(hubShellViewModel);
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Startup failed:\n\n{exception}",
                "PowerForge Studio",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ThemeService.Dispose();
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Unhandled UI exception:\n\n{e.Exception}",
            "PowerForge Studio",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Unhandled domain exception:\n\n{e.ExceptionObject}",
            "PowerForge Studio",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Unobserved task exception:\n\n{e.Exception}",
            "PowerForge Studio",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.SetObserved();
    }
}
