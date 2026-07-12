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
        if (!match.Success || !Version.TryParse(match.Groups["core"].Value, out var coreVersion))
            return false;

        normalized = NormalizeNumericCore(coreVersion);
        if (match.Groups["prerelease"].Success)
        {
            var prerelease = match.Groups["prerelease"].Value;
            if (HasLeadingZeroNumericIdentifier(prerelease))
                return false;

            normalized += "-" + prerelease;
        }
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

    internal static int Compare(string left, string right)
    {
        if (!TryNormalizeExact(left, out var normalizedLeft))
            throw new ArgumentException("A valid exact package version is required.", nameof(left));
        if (!TryNormalizeExact(right, out var normalizedRight))
            throw new ArgumentException("A valid exact package version is required.", nameof(right));

        var leftCore = Version.Parse(GetNumericVersion(normalizedLeft));
        var rightCore = Version.Parse(GetNumericVersion(normalizedRight));
        var coreComparison = leftCore.CompareTo(rightCore);
        if (coreComparison != 0)
            return coreComparison;

        var leftPrerelease = GetPrereleaseVersion(normalizedLeft);
        var rightPrerelease = GetPrereleaseVersion(normalizedRight);
        if (leftPrerelease.Length == 0)
            return rightPrerelease.Length == 0 ? 0 : 1;
        if (rightPrerelease.Length == 0)
            return -1;

        var leftIdentifiers = leftPrerelease.Split('.');
        var rightIdentifiers = rightPrerelease.Split('.');
        var commonLength = Math.Min(leftIdentifiers.Length, rightIdentifiers.Length);
        for (var index = 0; index < commonLength; index++)
        {
            var identifierComparison = ComparePrereleaseIdentifier(leftIdentifiers[index], rightIdentifiers[index]);
            if (identifierComparison != 0)
                return identifierComparison;
        }

        return leftIdentifiers.Length.CompareTo(rightIdentifiers.Length);
    }

    private static string NormalizeNumericCore(Version version)
    {
        var build = version.Build < 0 ? 0 : version.Build;
        var normalized = $"{version.Major}.{version.Minor}.{build}";
        return version.Revision > 0 ? normalized + "." + version.Revision : normalized;
    }

    private static int ComparePrereleaseIdentifier(string left, string right)
    {
        var leftNumeric = IsNumericIdentifier(left);
        var rightNumeric = IsNumericIdentifier(right);
        if (leftNumeric && rightNumeric)
        {
            var normalizedLeft = left.TrimStart('0');
            var normalizedRight = right.TrimStart('0');
            if (normalizedLeft.Length == 0) normalizedLeft = "0";
            if (normalizedRight.Length == 0) normalizedRight = "0";
            var lengthComparison = normalizedLeft.Length.CompareTo(normalizedRight.Length);
            return lengthComparison != 0
                ? lengthComparison
                : string.Compare(normalizedLeft, normalizedRight, StringComparison.Ordinal);
        }

        if (leftNumeric != rightNumeric)
            return leftNumeric ? -1 : 1;

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasLeadingZeroNumericIdentifier(string prerelease)
    {
        foreach (var identifier in prerelease.Split('.'))
        {
            if (identifier.Length > 1 && identifier[0] == '0' && IsNumericIdentifier(identifier))
                return true;
        }

        return false;
    }

    private static bool IsNumericIdentifier(string value)
    {
        if (value.Length == 0) return false;
        foreach (var character in value)
        {
            if (character < '0' || character > '9') return false;
        }
        return true;
    }
}
