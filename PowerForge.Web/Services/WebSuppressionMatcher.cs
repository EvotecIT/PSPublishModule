using System.Text.RegularExpressions;

namespace PowerForge.Web;

internal static class WebSuppressionMatcher
{
    internal static string[] NormalizePatterns(string[]? patterns)
    {
        if (patterns is null || patterns.Length == 0)
            return Array.Empty<string>();

        return patterns
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .Select(static p => p.Trim())
            .Where(static p => p.Length > 0)
            .ToArray();
    }

    internal static bool IsSuppressed(string text, string? code, string[] patterns)
    {
        if (patterns.Length == 0)
            return false;

        foreach (var raw in patterns)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var pattern = raw.Trim();
            if (pattern.StartsWith("re:", StringComparison.OrdinalIgnoreCase))
            {
                var rx = pattern.Substring(3);
                if (string.IsNullOrWhiteSpace(rx))
                    continue;

                try
                {
                    if (Regex.IsMatch(text ?? string.Empty, rx,
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                        TimeSpan.FromSeconds(1)))
                        return true;
                }
                catch
                {
                    // ignore invalid regex patterns
                }
                continue;
            }

            if (!string.IsNullOrWhiteSpace(code) && string.Equals(code, pattern, StringComparison.OrdinalIgnoreCase))
                return true;

            if (pattern.IndexOfAny(new[] { '*', '?' }) >= 0)
            {
                if (WildcardIsMatch(text, pattern))
                    return true;
                continue;
            }

            if ((text ?? string.Empty).IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private static bool WildcardIsMatch(string? input, string pattern)
    {
        try
        {
            var rx = "^" + Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            return Regex.IsMatch(input ?? string.Empty, rx,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
        }
        catch
        {
            return false;
        }
    }
}
