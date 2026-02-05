using PowerForge;
using Spectre.Console;

namespace PowerForge.Cli;

/// <summary>ILogger implementation that renders messages using Spectre.Console.</summary>
public sealed class SpectreConsoleLogger : ILogger
{
    /// <summary>When true, verbose messages are emitted.</summary>
    public bool IsVerbose { get; set; }

    /// <summary>Writes an informational message.</summary>
    public void Info(string message) => Write(LogLevel.Info, message);
    /// <summary>Writes a success message.</summary>
    public void Success(string message) => Write(LogLevel.Success, message);
    /// <summary>Writes a warning message.</summary>
    public void Warn(string message) => Write(LogLevel.Warning, message);
    /// <summary>Writes an error message.</summary>
    public void Error(string message) => Write(LogLevel.Error, message);

    /// <summary>Writes a verbose message when <see cref="IsVerbose"/> is true.</summary>
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

        icon = NormalizeIcon(icon);

        // Render with a fixed-width icon column so emoji/codepage glyphs don't shift the message text.
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
