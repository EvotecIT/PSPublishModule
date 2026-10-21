using System.Text.RegularExpressions;

namespace PowerForge.Web;

public static partial class WebSiteAuditor
{
    private static readonly Regex EscapedMediaTagPattern = new(
        "&lt;\\s*(img|iframe|video|source|picture)\\b(?=[^&]*?=)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Checks escaped media markup outside code examples without changing the document used by other audit checks.
    /// </summary>
    private static void ValidateEscapedMediaTags(
        AngleSharp.Dom.IDocument doc,
        string rawHtml,
        string relativePath,
        Action<string, string, string?, string, string?> addIssue)
    {
        if (string.IsNullOrWhiteSpace(rawHtml) || !EscapedMediaTagPattern.IsMatch(rawHtml))
            return;

        var content = doc.DocumentElement?.Clone(true) as AngleSharp.Dom.IElement;
        if (content is null)
            return;

        // DOM removal handles normalized entities and leaves identical text outside examples intact.
        foreach (var codeElement in content.QuerySelectorAll("code,pre"))
            codeElement.Remove();

        var matches = EscapedMediaTagPattern.Matches(content.InnerHtml);
        if (matches.Count == 0)
            return;

        var tags = matches
            .Select(match => match.Groups[1].Value.ToLowerInvariant())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        addIssue("warning", "media", relativePath,
            $"escaped media HTML tags detected in rendered content ({string.Join(", ", tags)}). " +
            "This usually means markdown HTML was not rendered as intended; prefer markdown image syntax or supported media shortcodes.",
            "media-escaped-html-tag");
    }
}
