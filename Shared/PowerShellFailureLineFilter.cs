#pragma warning disable 1591
using System;

namespace PowerForge;

public static class PowerShellFailureLineFilter
{
    public static bool ShouldSkip(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return true;

        return line.StartsWith("At ", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("+ CategoryInfo", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("CategoryInfo", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("+ FullyQualifiedErrorId", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("FullyQualifiedErrorId", StringComparison.OrdinalIgnoreCase);
    }
}
