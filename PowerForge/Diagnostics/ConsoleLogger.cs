using System;
using System.Text;

namespace PowerForge;

/// <summary>
/// Simple console logger that prefixes messages with markers (emoji when possible, otherwise legacy tokens).
/// </summary>
public sealed class ConsoleLogger : ILogger
{
    /// <inheritdoc />
    public bool IsVerbose { get; set; }

    /// <inheritdoc />
    public void Info(string message) => Write(GetPrefix(LogLevel.Info), message);

    /// <inheritdoc />
    public void Success(string message) => Write(GetPrefix(LogLevel.Success), message);

    /// <inheritdoc />
    public void Warn(string message) => Write(GetPrefix(LogLevel.Warning), message);

    /// <inheritdoc />
    public void Error(string message) => Write(GetPrefix(LogLevel.Error), message);

    /// <inheritdoc />
    public void Verbose(string message)
    {
        if (IsVerbose) Write(GetPrefix(LogLevel.Verbose), message);
    }

    private static void Write(string prefix, string message)
        => Console.WriteLine(prefix + (message ?? string.Empty));

    private static string GetPrefix(LogLevel level)
    {
        if (ShouldUseUnicode())
        {
            return level switch
            {
                LogLevel.Info => "ℹ️ ",
                LogLevel.Success => "✅ ",
                LogLevel.Warning => "⚠️ ",
                LogLevel.Error => "❌ ",
                LogLevel.Verbose => "🔎 ",
                _ => "• "
            };
        }

        return level switch
        {
            LogLevel.Info => "[i] ",
            LogLevel.Success => "[+] ",
            LogLevel.Warning => "[-] ",
            LogLevel.Error => "[e] ",
            LogLevel.Verbose => "[v] ",
            _ => "[?] "
        };
    }

    private static bool ShouldUseUnicode()
    {
        try
        {
            // Prefer emoji when the console is configured for UTF-8 output.
            // Many hosts set this automatically; PowerForge CLI/cmdlets also do best-effort setup.
            return Console.OutputEncoding.CodePage == Encoding.UTF8.CodePage;
        }
        catch
        {
            return false;
        }
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
