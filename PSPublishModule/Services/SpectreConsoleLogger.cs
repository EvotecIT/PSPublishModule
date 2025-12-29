using PowerForge;
using Spectre.Console;

namespace PSPublishModule;

internal sealed class SpectreConsoleLogger : ILogger
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
        var unicode = AnsiConsole.Profile.Capabilities.Unicode;

        var (icon, color) = level switch
        {
            LogLevel.Info => (unicode ? "ℹ️" : "i", "deepskyblue1"),
            LogLevel.Success => (unicode ? "✅" : "+", "green"),
            LogLevel.Warning => (unicode ? "⚠️" : "!", "yellow"),
            LogLevel.Error => (unicode ? "❌" : "x", "red"),
            LogLevel.Verbose => (unicode ? "🔎" : "v", "grey"),
            _ => (unicode ? "•" : "?", "grey")
        };

        AnsiConsole.MarkupLine($"[{color}]{icon}[/] {safe}");
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

