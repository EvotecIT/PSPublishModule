using System;

namespace PowerForge;

/// <summary>
/// Filters common PowerShell error-record noise from human-facing diagnostic summaries.
/// </summary>
public static class PowerShellFailureLineFilter
{
    /// <summary>
    /// Returns <see langword="true"/> when a line is boilerplate PowerShell error-record output
    /// that should be hidden from compact failure summaries.
    /// </summary>
    /// <param name="line">The line to evaluate.</param>
    /// <returns><see langword="true"/> when the line should be omitted from summaries.</returns>
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
