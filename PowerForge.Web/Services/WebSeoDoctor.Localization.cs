namespace PowerForge.Web;

public static partial class WebSeoDoctor
{
    // This is a grouping token, not a language registry or hreflang validator.
    // Preserve singleton extensions, private-use tags, and grandfathered forms.
    private static readonly System.Text.RegularExpressions.Regex DocumentLanguageTokenPattern = new(
        @"\A[a-z]{1,8}(?:-[a-z0-9]{1,8})*\z",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Keeps script and region subtags distinct when comparing localized page intent.</summary>
    private static string NormalizeDocumentLanguage(string? language)
    {
        var normalized = language?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized != "x-default" && DocumentLanguageTokenPattern.IsMatch(normalized)
            ? normalized
            : string.Empty;
    }

    /// <summary>Distinguishes language and Unicode titles in the ASCII baseline key space.</summary>
    private static string DuplicateTitleKey(string language, string normalizedTitle)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(language + "\0" + normalizedTitle);
        return "duplicate-title-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
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
