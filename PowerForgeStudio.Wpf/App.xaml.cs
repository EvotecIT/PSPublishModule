using System.Windows;
using System.Windows.Threading;
using PowerForgeStudio.Wpf.ViewModels;
using PowerForgeStudio.Wpf.ViewModels.Hub;

namespace PowerForgeStudio.Wpf;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var logPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "PowerForgeStudio-crash.log");

        try
        {
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:O}] OnStartup entered\n");
            var shellViewModel = new ShellViewModel(ShellViewModelServices.CreateDefault());
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:O}] ShellViewModel created\n");
            var hubViewModel = new HubViewModel();
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:O}] HubViewModel created\n");
            var hubShellViewModel = new HubShellViewModel(shellViewModel, hubViewModel);
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:O}] HubShellViewModel created\n");
            var mainWindow = new MainWindow(hubShellViewModel);
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:O}] MainWindow created\n");
            MainWindow = mainWindow;
            mainWindow.Show();
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:O}] MainWindow.Show() called\n");
        }
        catch (Exception exception)
        {
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:O}] STARTUP EXCEPTION:\n{exception}\n");
            MessageBox.Show(
                $"Startup failed:\n\n{exception}",
                "PowerForge Studio",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void LogCrash(string label, object exception)
    {
        try
        {
            var logPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "PowerForgeStudio-crash.log");
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:O}] {label}:\n{exception}\n\n");
        }
        catch
        {
            // ignore logging failures
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash("DISPATCHER", e.Exception);
        MessageBox.Show(
            $"Unhandled UI exception:\n\n{e.Exception}",
            "PowerForge Studio",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        LogCrash("DOMAIN", e.ExceptionObject);
        MessageBox.Show(
            $"Unhandled domain exception:\n\n{e.ExceptionObject}",
            "PowerForge Studio",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogCrash("TASK", e.Exception);
        MessageBox.Show(
            $"Unobserved task exception:\n\n{e.Exception}",
            "PowerForge Studio",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.SetObserved();
    }
}

