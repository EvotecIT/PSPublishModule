namespace PowerForge.Web;

public static partial class WebSeoDoctor
{
    /// <summary>Keeps script and region subtags distinct when comparing localized page intent.</summary>
    private static string NormalizeDocumentLanguage(string? language)
    {
        var normalized = language?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized != "x-default" && HreflangTokenPattern.IsMatch(normalized)
            ? normalized
            : string.Empty;
    }

    /// <summary>
    /// Dense CJK writing conveys more information per character than Latin text.
    /// Halve the editorial minimum for those document languages, retaining the
    /// configured maximum and the separate missing-content checks.
    /// </summary>
    private static int LocalizedMinimumLength(int minimum, string language)
    {
        var primaryLanguage = language.Split('-')[0];
        return primaryLanguage is "ja" or "ko" or "zh"
            ? (int)Math.Ceiling(minimum / 2d)
            : minimum;
    }
}
