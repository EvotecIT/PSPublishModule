using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PowerForge.Compiler.App.Engine;
using PowerForge.Compiler.App.ViewModels;

namespace PowerForge.Compiler.App;

/// <summary>The application.</summary>
public sealed class App : Application
{
    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainWindowViewModel(new CompilerAppEngine());
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            var startPath = desktop.Args?.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(startPath)) _ = viewModel.SetInputAsync(startPath);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
