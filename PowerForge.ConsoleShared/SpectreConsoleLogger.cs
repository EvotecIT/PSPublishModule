using PowerForge;
using Spectre.Console;

namespace PowerForge.ConsoleShared;

internal class SpectreConsoleLogger : ILogger
{
    private readonly IAnsiConsole? _console;

    internal SpectreConsoleLogger(IAnsiConsole? console = null) => _console = console;

    public bool IsVerbose { get; set; }

    public void Info(string message) => Write(LogLevel.Info, message);
    public void Success(string message) => Write(LogLevel.Success, message);
    public void Warn(string message) => Write(LogLevel.Warning, message);
    public void Error(string message) => Write(LogLevel.Error, message);

    public void Verbose(string message)
    {
        if (!IsVerbose) return;
        Write(LogLevel.Verbose, message);
    }

    private void Write(LogLevel level, string message)
    {
        var console = _console ?? AnsiConsole.Console;
        var safe = Markup.Escape(message ?? string.Empty);
        var emoji = ConsoleEncoding.ShouldRenderEmoji(console.Profile.Capabilities.Unicode);

        var (icon, color) = level switch
        {
            LogLevel.Info => (emoji ? "ℹ️" : "i", "deepskyblue1"),
            LogLevel.Success => (emoji ? "✅" : "+", "green"),
            LogLevel.Warning => (emoji ? "⚠️" : "!", "yellow"),
            LogLevel.Error => (emoji ? "❌" : "x", "red"),
            LogLevel.Verbose => (emoji ? "🔎" : "v", "grey"),
            _ => (emoji ? "•" : "?", "grey")
        };

        icon = NormalizeIcon(icon);

        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("i").NoWrap().Width(2))
            .AddColumn(new TableColumn("m"));

        table.AddRow($"[{color}]{icon}[/]", safe);
        if (!SpectreProgressDisplay.TryWriteDiagnostic(console, table))
        {
            console.Write(table);
            console.WriteLine();
        }
    }

    private static string NormalizeIcon(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon)) return string.Empty;
        return icon!.Replace("\uFE0F", string.Empty).Replace("\uFE0E", string.Empty);
    }

    private enum LogLevel
    {
        Info,
        Success,
        Warning,
        Error,
        Verbose
    }
}
