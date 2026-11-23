using System;
using System.Linq;
namespace PowerForge;

internal readonly struct ModuleStateVersion : IComparable<ModuleStateVersion>, IEquatable<ModuleStateVersion>
{
    private static readonly int[] EmptySegments = new int[4];
    private readonly int[] _segments;

    private ModuleStateVersion(string original, int[] segments, string? prerelease)
    {
        Original = original;
        _segments = segments;
        Prerelease = prerelease;
    }

    internal string Original { get; }

    internal string? Prerelease { get; }

    internal bool IsPrerelease => !string.IsNullOrWhiteSpace(Prerelease);

    internal string Normalized => string.Join(".", Segments.Take(NormalizedSegmentCount)) + (IsPrerelease ? "-" + Prerelease : string.Empty);

    internal static bool TryParse(string? value, out ModuleStateVersion version)
    {
        version = default;
        if (value is null)
            return false;

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
            return false;

        var dashIndex = trimmed.IndexOf('-');
        var numericPart = dashIndex >= 0 ? trimmed.Substring(0, dashIndex) : trimmed;
        var prerelease = dashIndex >= 0 ? trimmed.Substring(dashIndex + 1) : null;
        if (string.IsNullOrWhiteSpace(numericPart) || prerelease is { Length: 0 })
            return false;

        var parts = numericPart.Split('.');
        if (parts.Length is < 1 or > 4)
            return false;

        var segments = new int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out var segment) || segment < 0)
                return false;

            segments[i] = segment;
        }

        version = new ModuleStateVersion(trimmed, segments, prerelease);
        return true;
    }

    internal static ModuleStateVersion Parse(string value)
        => TryParse(value, out var version)
            ? version
            : throw new ArgumentException($"Invalid module version '{value}'.", nameof(value));

    public int CompareTo(ModuleStateVersion other)
    {
        var segments = Segments;
        var otherSegments = other.Segments;
        for (var i = 0; i < segments.Length; i++)
        {
            var comparison = segments[i].CompareTo(otherSegments[i]);
            if (comparison != 0)
                return comparison;
        }

        if (IsPrerelease && !other.IsPrerelease)
            return -1;
        if (!IsPrerelease && other.IsPrerelease)
            return 1;

        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    public bool Equals(ModuleStateVersion other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is ModuleStateVersion other && Equals(other);

    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var segment in Segments)
            hash = (hash * 31) + segment;

        hash = (hash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(Prerelease ?? string.Empty);
        return hash;
    }

    public override string ToString() => Normalized;

    internal static string NormalizeOrOriginal(string version)
        => TryParse(version, out var parsed) ? parsed.Normalized : version;

    private static int ComparePrerelease(string? left, string? right)
    {
        var leftIdentifiers = SplitPrerelease(left);
        var rightIdentifiers = SplitPrerelease(right);
        var count = Math.Min(leftIdentifiers.Length, rightIdentifiers.Length);
        for (var i = 0; i < count; i++)
        {
            var leftPart = leftIdentifiers[i];
            var rightPart = rightIdentifiers[i];
            var leftIsNumeric = int.TryParse(leftPart, out var leftNumeric);
            var rightIsNumeric = int.TryParse(rightPart, out var rightNumeric);
            if (leftIsNumeric && rightIsNumeric)
            {
                var numericComparison = leftNumeric.CompareTo(rightNumeric);
                if (numericComparison != 0)
                    return numericComparison;

                continue;
            }

            if (leftIsNumeric != rightIsNumeric)
                return leftIsNumeric ? -1 : 1;

            var comparison = CompareMixedIdentifier(leftPart, rightPart);
            if (comparison != 0)
                return comparison;
        }

        return leftIdentifiers.Length.CompareTo(rightIdentifiers.Length);
    }

    private static string[] SplitPrerelease(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value!.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);

    private static int CompareMixedIdentifier(string left, string right)
    {
        var leftIndex = 0;
        var rightIndex = 0;
        while (leftIndex < left.Length && rightIndex < right.Length)
        {
            var leftDigits = char.IsDigit(left[leftIndex]);
            var rightDigits = char.IsDigit(right[rightIndex]);
            if (leftDigits != rightDigits)
                return leftDigits ? -1 : 1;

            var leftStart = leftIndex;
            while (leftIndex < left.Length && char.IsDigit(left[leftIndex]) == leftDigits)
                leftIndex++;

            var rightStart = rightIndex;
            while (rightIndex < right.Length && char.IsDigit(right[rightIndex]) == rightDigits)
                rightIndex++;

            var leftPart = left.Substring(leftStart, leftIndex - leftStart);
            var rightPart = right.Substring(rightStart, rightIndex - rightStart);
            var comparison = leftDigits
                ? CompareNumericStrings(leftPart, rightPart)
                : string.Compare(leftPart, rightPart, StringComparison.OrdinalIgnoreCase);
            if (comparison != 0)
                return comparison;
        }

        return left.Length.CompareTo(right.Length);
    }

    private static int CompareNumericStrings(string left, string right)
    {
        var trimmedLeft = left.TrimStart('0');
        var trimmedRight = right.TrimStart('0');
        if (trimmedLeft.Length == 0)
            trimmedLeft = "0";
        if (trimmedRight.Length == 0)
            trimmedRight = "0";

        var comparison = trimmedLeft.Length.CompareTo(trimmedRight.Length);
        return comparison != 0
            ? comparison
            : string.Compare(trimmedLeft, trimmedRight, StringComparison.Ordinal);
    }

    private int[] Segments => _segments ?? EmptySegments;

    private int NormalizedSegmentCount => Segments[3] != 0 ? 4 : 3;
}
