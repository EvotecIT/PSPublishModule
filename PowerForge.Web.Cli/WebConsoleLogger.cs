using System;
using System.Text;

namespace PowerForge.Web.Cli;

internal sealed class WebConsoleLogger
{
    private readonly bool _useUnicodePrefixes = ShouldUseUnicodePrefixes();
    private readonly bool _writeToStandardError;

    internal WebConsoleLogger(bool writeToStandardError = false) => _writeToStandardError = writeToStandardError;

    public void Info(string message) => Write($"{(_useUnicodePrefixes ? "ℹ " : "[INFO]")} {message}");
    public void Success(string message) => Write($"{(_useUnicodePrefixes ? "✅" : "[OK]")} {message}");
    public void Warn(string message) => Write($"{(_useUnicodePrefixes ? "⚠️" : "[WARN]")} {message}");
    public void Error(string message) => Write($"{(_useUnicodePrefixes ? "❌" : "[ERROR]")} {message}");

    private void Write(string message) => (_writeToStandardError ? Console.Error : Console.Out).WriteLine(message);

    private static bool ShouldUseUnicodePrefixes()
    {
        var forceAscii = Environment.GetEnvironmentVariable("POWERFORGE_WEB_ASCII_LOGS");
        if (string.Equals(forceAscii, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(forceAscii, "true", StringComparison.OrdinalIgnoreCase))
            return false;

        var codePage = Console.OutputEncoding.CodePage;
        return codePage == Encoding.UTF8.CodePage ||
               codePage == Encoding.Unicode.CodePage ||
               codePage == Encoding.BigEndianUnicode.CodePage;
    }
}
