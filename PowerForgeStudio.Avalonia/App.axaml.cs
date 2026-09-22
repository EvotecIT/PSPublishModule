using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Avalonia;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var index = Array.IndexOf(Program.Arguments, "--workspace");
            var root = index >= 0 && index + 1 < Program.Arguments.Length
                ? Program.Arguments[index + 1]
                : Environment.GetEnvironmentVariable("EVOTEC_GITHUB_ROOT")
                  ?? (OperatingSystem.IsWindows() ? @"C:\Support\GitHub" : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "GitHub"));
            var catalog = new WorkspaceRootCatalogService();
            // An explicit workspace always wins over machine-local recent-root state.
            string? startupError = null;
            if (index < 0)
            {
                try { root = catalog.Load(root).ActiveWorkspaceRoot; }
                catch (Exception ex) { startupError = "Could not read the workspace catalog: " + StudioDisplayError.From(ex); }
            }
            var model = new WorkspaceViewModel(root, catalog);
            if (startupError is not null) model.StateError = startupError;
            var window = new MainWindow { DataContext = model };
            window.Opened += async (_, _) => await model.RefreshAsync();
            window.Closed += (_, _) => model.Dispose();
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
