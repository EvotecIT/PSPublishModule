using System;

namespace PowerForge.Web.Cli;

internal static class CliPatternHelper
{
    internal static string[] SplitPatterns(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Array.Empty<string>();
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
