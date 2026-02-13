using OfficeIMO.Markdown;

namespace PowerForge.Web;

internal static class MarkdownRenderer
{
    private static readonly MarkdownReaderOptions GitHubLikeReaderOptions = new()
    {
        // GitHub-flavored markdown does not support definition lists by default.
        // Disabling this avoids accidental dt/dd rendering from "Q:"/"A:" prose.
        DefinitionLists = false
    };

    public static string RenderToHtml(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return string.Empty;

        try
        {
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
            "<h(?<level>[1-6])>(?<text>.*?)</h\\1>",
            match =>
            {
                var text = System.Text.RegularExpressions.Regex.Replace(match.Groups["text"].Value, "<.*?>", string.Empty);
                var id = Slugify(text);
                return $"<h{match.Groups["level"].Value} id=\"{id}\">{match.Groups["text"].Value}</h{match.Groups["level"].Value}>";
            },
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
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
