using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    private sealed class DotNetPackResult
    {
        public bool Success { get; set; }
        public bool SigningFailed { get; set; }
        public string? ErrorMessage { get; set; }
        public TimeSpan Duration { get; set; }
        public List<string> Packages { get; } = new();
        public List<string> SymbolPackages { get; } = new();
    }

    private sealed class AssemblySigningPlan
    {
        public string[] IncludePatterns { get; set; } = Array.Empty<string>();
        public string[] Files { get; set; } = Array.Empty<string>();
        public int OutputDirectoryCount { get; set; }
    }

    internal static string FormatDuration(TimeSpan duration) => OperationDisplay.FormatDuration(duration);

    internal static string FormatBytes(long bytes) => OperationDisplay.FormatBytes(bytes);


    private static void TryDeleteDirectory(string path, ILogger? logger = null)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            if (logger?.IsVerbose == true)
                logger.Verbose($"Failed to delete temporary directory '{path}': {ex.Message}");
        }
    }

#if NET472
    private static string BuildWindowsArgumentString(IEnumerable<string> arguments)
        => string.Join(" ", arguments.Select(EscapeWindowsArgument));

    // Based on .NET's internal ProcessStartInfo quoting behavior for Windows CreateProcess.
    private static string EscapeWindowsArgument(string arg)
    {
        if (arg is null) return "\"\"";
        if (arg.Length == 0) return "\"\"";

        bool needsQuotes = arg.Any(ch => char.IsWhiteSpace(ch) || ch == '"');
        if (!needsQuotes) return arg;

        var sb = new System.Text.StringBuilder();
        sb.Append('"');

        int backslashCount = 0;
        foreach (var ch in arg)
        {
            if (ch == '\\')
            {
                backslashCount++;
                continue;
            }

            if (ch == '"')
            {
                sb.Append('\\', backslashCount * 2 + 1);
                sb.Append('"');
                backslashCount = 0;
                continue;
            }

            if (backslashCount > 0)
            {
                sb.Append('\\', backslashCount);
                backslashCount = 0;
            }

            sb.Append(ch);
        }

        if (backslashCount > 0)
            sb.Append('\\', backslashCount * 2);

        sb.Append('"');
        return sb.ToString();
    }
#endif
}
