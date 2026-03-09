using PowerForge;
using Spectre.Console;

namespace PowerForge.ConsoleShared;

internal class SpectreConsoleLogger : ILogger
{
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

    private static void Write(LogLevel level, string message)
    {
        var safe = Markup.Escape(message ?? string.Empty);
        var unicode = ConsoleEncoding.ShouldRenderUnicode(AnsiConsole.Profile.Capabilities.Unicode);

        var (icon, color) = level switch
        {
            LogLevel.Info => (unicode ? "ℹ️" : "i", "deepskyblue1"),
            LogLevel.Success => (unicode ? "✅" : "+", "green"),
            LogLevel.Warning => (unicode ? "⚠️" : "!", "yellow"),
            LogLevel.Error => (unicode ? "❌" : "x", "red"),
            LogLevel.Verbose => (unicode ? "🔎" : "v", "grey"),
            _ => (unicode ? "•" : "?", "grey")
        };

        icon = NormalizeIcon(icon);

        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("i").NoWrap().Width(2))
            .AddColumn(new TableColumn("m"));

        table.AddRow($"[{color}]{icon}[/]", safe);
        AnsiConsole.Write(table);
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
