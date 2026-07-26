using System.Text.RegularExpressions;

namespace PowerForge.Web;

internal static class WebGlobMatcher
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    internal static bool IsMatch(string? pattern, string? value)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(value))
            return false;

        var normalizedPattern = pattern.Replace('\\', '/');
        var normalizedValue = value.Replace('\\', '/');
        var regex = "^" + Regex.Escape(normalizedPattern)
            .Replace(@"\*\*/", "(?:.*/)?", StringComparison.Ordinal)
            .Replace(@"\*\*", ".*", StringComparison.Ordinal)
            .Replace(@"\*", "[^/]*", StringComparison.Ordinal)
            .Replace(@"\?", "[^/]", StringComparison.Ordinal) + "$";

        return Regex.IsMatch(
            normalizedValue,
            regex,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            MatchTimeout);
    }
}
