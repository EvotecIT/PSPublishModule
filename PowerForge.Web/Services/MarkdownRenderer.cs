using OfficeIMO.Markdown;

namespace PowerForge.Web;

internal static class MarkdownRenderer
{
    private static readonly System.Text.RegularExpressions.Regex ImageTagRegex = new(
        "<img\\b(?<attrs>[^>]*?)(?<close>\\s*/?)>",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex LoadingAttributeRegex = new(
        "\\bloading\\s*=",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex DecodingAttributeRegex = new(
        "\\bdecoding\\s*=",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly MarkdownReaderOptions GitHubLikeReaderOptions = new()
    {
        // GitHub-flavored markdown does not support definition lists by default.
        // Disabling this avoids accidental dt/dd rendering from "Q:"/"A:" prose.
        DefinitionLists = false
    };

    public static string RenderToHtml(string content, MarkdownSpec? markdown = null)
    {
        if (string.IsNullOrWhiteSpace(content)) return string.Empty;

        try
        {
            content = MarkdownMediaTagNormalizer.NormalizeMultilineMediaTagsOutsideFences(content);
            var doc = MarkdownReader.Parse(content, GitHubLikeReaderOptions);
            var options = new HtmlOptions
            {
                Kind = HtmlKind.Fragment,
                Prism = new PrismOptions
                {
                    Enabled = true,
                    Theme = PrismTheme.GithubAuto
                }
            };
            var html = doc.ToHtmlFragment(options);
            html = InjectDefaultImageHints(html, markdown);
            return InjectHeadingIds(html);
        }
        catch (Exception ex)
        {
            return $"<pre class=\"markdown-error\">Error rendering markdown: {System.Web.HttpUtility.HtmlEncode(ex.Message)}</pre>";
        }
    }

    private static string InjectHeadingIds(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        return System.Text.RegularExpressions.Regex.Replace(html,
            "<h(?<level>[1-6])(?<attrs>[^>]*)>(?<text>.*?)</h\\1>",
            match =>
            {
                var level = match.Groups["level"].Value;
                var attrs = match.Groups["attrs"].Value;
                var headingHtml = NormalizeInlineCodeInHeading(match.Groups["text"].Value);
                var text = System.Text.RegularExpressions.Regex.Replace(headingHtml, "<.*?>", string.Empty);
                var id = Slugify(text);
                var hasId = System.Text.RegularExpressions.Regex.IsMatch(
                    attrs,
                    "\\sid\\s*=",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
                var attrsWithId = hasId
                    ? attrs
                    : (string.IsNullOrWhiteSpace(attrs) ? $" id=\"{id}\"" : $"{attrs} id=\"{id}\"");
                return $"<h{level}{attrsWithId}>{headingHtml}</h{level}>";
            },
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
    }

    private static string InjectDefaultImageHints(string html, MarkdownSpec? markdown)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        if (markdown?.AutoImageHints == false)
            return html;

        var loadingValue = string.IsNullOrWhiteSpace(markdown?.DefaultImageLoading)
            ? "lazy"
            : markdown!.DefaultImageLoading.Trim();
        var decodingValue = string.IsNullOrWhiteSpace(markdown?.DefaultImageDecoding)
            ? "async"
            : markdown!.DefaultImageDecoding.Trim();

        return ImageTagRegex.Replace(html, match =>
        {
            var attrs = match.Groups["attrs"].Value;
            var close = match.Groups["close"].Value;
            if (string.IsNullOrEmpty(close))
                close = string.Empty;

            var hasLoading = LoadingAttributeRegex.IsMatch(attrs);
            var hasDecoding = DecodingAttributeRegex.IsMatch(attrs);
            if (hasLoading && hasDecoding)
                return match.Value;

            var attrsUpdated = attrs;
            if (!hasLoading)
                attrsUpdated += $" loading=\"{System.Web.HttpUtility.HtmlEncode(loadingValue)}\"";
            if (!hasDecoding)
                attrsUpdated += $" decoding=\"{System.Web.HttpUtility.HtmlEncode(decodingValue)}\"";

            return $"<img{attrsUpdated}{close}>";
        });
    }

    private static string NormalizeInlineCodeInHeading(string headingHtml)
    {
        if (string.IsNullOrWhiteSpace(headingHtml))
            return string.Empty;
        if (headingHtml.Contains("<code", StringComparison.OrdinalIgnoreCase))
            return headingHtml;

        return System.Text.RegularExpressions.Regex.Replace(
            headingHtml,
            "(?<!\\\\)`(?<code>[^`]+)`",
            "<code>${code}</code>");
    }

    private static string Slugify(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var lower = text.Trim().ToLowerInvariant();
        lower = System.Text.RegularExpressions.Regex.Replace(lower, "[^a-z0-9\\s-]", string.Empty);
        lower = System.Text.RegularExpressions.Regex.Replace(lower, "\\s+", "-");
        return lower.Trim('-');
    }
}
