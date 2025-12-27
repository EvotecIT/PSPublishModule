using PowerForge;
using Spectre.Console;

namespace PowerForge.Cli;

public sealed class SpectreConsoleLogger : ILogger
{
    public bool IsVerbose { get; set; }

    public void Info(string message) => Write("i", "grey", message);
    public void Success(string message) => Write("+", "green", message);
    public void Warn(string message) => Write("-", "yellow", message);
    public void Error(string message) => Write("e", "red", message);

    public void Verbose(string message)
    {
        if (!IsVerbose) return;
        Write("v", "grey", message);
    }

    private static void Write(string marker, string color, string message)
    {
        var safe = Markup.Escape(message ?? string.Empty);
        AnsiConsole.MarkupLine($"[{color}][[{marker}]][/] {safe}");
    }
}

