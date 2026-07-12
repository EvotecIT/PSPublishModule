using System;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Validates exact NuGet-style package versions without treating prerelease labels as X-patterns.
/// </summary>
internal static class PackageVersionUtility
{
    private static readonly Regex ExactVersionRegex = new(
        @"^(?<core>\d+\.\d+(?:\.\d+){0,2})(?:-(?<prerelease>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+(?<metadata>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static bool TryNormalizeExact(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var candidate = value!.Trim();
        var match = ExactVersionRegex.Match(candidate);
        if (!match.Success || !Version.TryParse(match.Groups["core"].Value, out _))
            return false;

        normalized = candidate;
        return true;
    }
}
