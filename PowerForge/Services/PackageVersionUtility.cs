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

        normalized = match.Groups["core"].Value;
        if (match.Groups["prerelease"].Success)
            normalized += "-" + match.Groups["prerelease"].Value;
        return true;
    }

    internal static string GetNumericVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return string.Empty;

        var delimiter = version.IndexOfAny(new[] { '-', '+' });
        return delimiter < 0 ? version.Trim() : version.Substring(0, delimiter).Trim();
    }

    internal static string GetPrereleaseVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return string.Empty;

        var trimmed = version.Trim();
        var separator = trimmed.IndexOf('-');
        if (separator < 0)
            return string.Empty;

        var metadataSeparator = trimmed.IndexOf('+', separator + 1);
        return metadataSeparator < 0
            ? trimmed.Substring(separator + 1)
            : trimmed.Substring(separator + 1, metadataSeparator - separator - 1);
    }
}
