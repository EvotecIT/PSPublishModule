using OfficeIMO.Markdown;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

internal static class MarkdownRenderer
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex FenceRegex = new("^```", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex HtmlImgTagRegex = new("<img\\b[\\s\\S]*?>", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);

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
            content = NormalizeMultilineHtmlImageTags(content);
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

    private static string NormalizeMultilineHtmlImageTags(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        var lines = markdown.Split('\n');
        var inFence = false;
        var outsideFence = new StringBuilder();
        var insideFence = new StringBuilder();
        var rebuilt = new StringBuilder(markdown.Length + 64);

        void FlushOutside()
        {
            if (outsideFence.Length == 0) return;
            var normalized = HtmlImgTagRegex.Replace(outsideFence.ToString(), match =>
            {
                var value = match.Value;
                if (value.IndexOf('\n') < 0 && value.IndexOf('\r') < 0)
                    return value;

                value = value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
                value = Regex.Replace(value, "\\s+", " ", RegexOptions.CultureInvariant, RegexTimeout).Trim();
                value = Regex.Replace(value, "\\s+/\\s*>", " />", RegexOptions.CultureInvariant, RegexTimeout);
                value = Regex.Replace(value, "\\s+>", ">", RegexOptions.CultureInvariant, RegexTimeout);
                return value;
            });
            rebuilt.Append(normalized);
            outsideFence.Clear();
        }

        void FlushInside()
        {
            if (insideFence.Length == 0) return;
            rebuilt.Append(insideFence);
            insideFence.Clear();
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine;
            var target = inFence ? insideFence : outsideFence;
            target.Append(line);
            target.Append('\n');

            if (!FenceRegex.IsMatch(line.TrimStart()))
                continue;

            if (inFence)
            {
                FlushInside();
                inFence = false;
            }
            else
            {
                FlushOutside();
                inFence = true;
            }
        }

        if (inFence)
            FlushInside();
        else
            FlushOutside();

        var updated = rebuilt.ToString();
        if (!markdown.EndsWith('\n') && updated.EndsWith('\n'))
            updated = updated[..^1];

        return updated;
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
