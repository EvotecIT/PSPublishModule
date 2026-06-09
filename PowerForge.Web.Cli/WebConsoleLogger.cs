using System;
using System.Text;

namespace PowerForge.Web.Cli;

internal sealed class WebConsoleLogger
{
    private readonly bool _useUnicodePrefixes = ShouldUseUnicodePrefixes();

    public void Info(string message) => Console.WriteLine($"{(_useUnicodePrefixes ? "ℹ " : "[INFO]")} {message}");
    public void Success(string message) => Console.WriteLine($"{(_useUnicodePrefixes ? "✅" : "[OK]")} {message}");
    public void Warn(string message) => Console.WriteLine($"{(_useUnicodePrefixes ? "⚠️" : "[WARN]")} {message}");
    public void Error(string message) => Console.WriteLine($"{(_useUnicodePrefixes ? "❌" : "[ERROR]")} {message}");

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
