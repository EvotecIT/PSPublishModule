namespace PowerForge.Web;

internal static class WebPackageVersionIdentity
{
    internal static bool NuGetVersionsEqual(string? left, string? right)
        => TryNormalizeNuGetVersion(left, out var normalizedLeft) &&
           TryNormalizeNuGetVersion(right, out var normalizedRight) &&
           normalizedLeft.Equals(normalizedRight, StringComparison.OrdinalIgnoreCase);

    private static bool TryNormalizeNuGetVersion(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var candidate = value.Trim().TrimStart('v');
        var metadataIndex = candidate.IndexOf('+');
        if (metadataIndex >= 0)
            candidate = candidate[..metadataIndex];
        var releaseIndex = candidate.IndexOf('-');
        var release = releaseIndex >= 0 ? candidate[..releaseIndex] : candidate;
        var suffix = releaseIndex >= 0 ? candidate[(releaseIndex + 1)..] : string.Empty;
        var segments = release.Split('.');
        if (segments.Length is < 1 or > 4 || segments.Any(static segment =>
                segment.Length == 0 || !segment.All(char.IsAsciiDigit)))
            return false;

        var normalizedSegments = segments.Select(NormalizeNumericSegment).ToList();
        while (normalizedSegments.Count < 3)
            normalizedSegments.Add("0");
        if (normalizedSegments.Count == 4 && normalizedSegments[3] == "0")
            normalizedSegments.RemoveAt(3);

        if (!string.IsNullOrEmpty(suffix))
        {
            var labels = suffix.Split('.');
            if (labels.Any(static label => label.Length == 0 ||
                    !label.All(static character => char.IsAsciiLetterOrDigit(character) || character == '-')))
                return false;
            suffix = string.Join('.', labels.Select(static label => label.All(char.IsAsciiDigit)
                ? NormalizeNumericSegment(label)
                : label.ToLowerInvariant()));
        }

        normalized = string.Join('.', normalizedSegments) +
                     (string.IsNullOrEmpty(suffix) ? string.Empty : "-" + suffix);
        return true;
    }

    private static string NormalizeNumericSegment(string value)
    {
        var normalized = value.TrimStart('0');
        return normalized.Length == 0 ? "0" : normalized;
    }
}
