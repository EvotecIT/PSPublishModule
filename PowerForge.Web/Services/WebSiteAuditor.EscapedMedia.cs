using System.Text.RegularExpressions;

namespace PowerForge.Web;

public static partial class WebSiteAuditor
{
    private static readonly Regex EscapedMediaTagPattern = new(
        "<\\s*(img|iframe|video|source|picture)\\b(?=[^<>]*?=)",
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
        if (string.IsNullOrWhiteSpace(rawHtml))
            return;

        var content = (doc.QuerySelector("body") ?? doc.DocumentElement)?.Clone(true) as AngleSharp.Dom.IElement;
        if (content is null)
            return;

        // The parser normalizes named and numeric entities. TextContent excludes comments
        // and attributes; remove examples and inert text without changing later audit checks.
        foreach (var codeElement in content.QuerySelectorAll("code,pre,script,style,template,noscript"))
            codeElement.Remove();

        var matches = EscapedMediaTagPattern.Matches(content.TextContent);
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
