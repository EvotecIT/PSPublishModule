using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using HtmlTinkerX;

namespace PowerForge.Web;

public static partial class WebAgentReadiness
{
    private static string ConvertHtmlDocumentToMarkdown(string html, AgentMarkdownArtifactsSpec spec, ContentItem? source = null)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        if (source?.MarkdownContent is not null && !string.IsNullOrWhiteSpace(source.HtmlContent) &&
            html.Contains(source.HtmlContent, StringComparison.Ordinal) &&
            !source.MarkdownContent.Contains("xref:", StringComparison.OrdinalIgnoreCase))
        {
            html = html.Replace(source.HtmlContent,
                "<div data-pf-markdown-source>" + System.Net.WebUtility.HtmlEncode(RemoveMarkdownTitle(source.MarkdownContent.Trim('\r', '\n'), source.Title)) + "</div>", StringComparison.Ordinal);
        }
        var doc = HtmlParser.ParseWithAngleSharp(html);
        var contentRoot = doc.QuerySelector("main,[role='main']") ?? doc.Body ?? doc.DocumentElement;
        if (contentRoot is null)
            return string.Empty;

        // One article identifies a document. Sibling articles are listing entries.
        var articles = contentRoot.QuerySelectorAll("article").Where(article =>
            article.ParentElement?.Closest("article") is not { } parent || !contentRoot.Contains(parent)).ToArray();
        if (articles.Length == 1)
            contentRoot = articles[0];

        foreach (var chrome in contentRoot.QuerySelectorAll("nav,aside,[role='navigation'],button,form,[data-pf-markdown='exclude']").ToArray())
            chrome.Remove();
        var title = source?.Title ?? contentRoot.QuerySelector("h1")?.TextContent.Trim() ?? doc.Title?.Trim();
        var description = source?.Description ?? doc.QuerySelector("meta[name='description']")?.GetAttribute("content");
        foreach (var link in contentRoot.QuerySelectorAll("a").Where(link =>
                     link.GetAttribute("href") == source?.EditUrl && source?.EditUrl is not null ||
                     link.TextContent.Trim().Equals("Edit on GitHub", StringComparison.OrdinalIgnoreCase)).ToArray())
            link.Remove();
        foreach (var heading in contentRoot.QuerySelectorAll("h1").Where(h => h.TextContent.Trim().Equals(title, StringComparison.OrdinalIgnoreCase)).ToArray())
            heading.Remove();
        foreach (var paragraph in contentRoot.QuerySelectorAll("header p").Where(p => p.TextContent.Trim() == description).ToArray())
            paragraph.Remove();

        var body = NormalizeMarkdownDocument(RenderMarkdownChildren(contentRoot));
        body = RemoveMarkdownTitle(body, title);
        if (!string.IsNullOrWhiteSpace(description) && !body.StartsWith(description, StringComparison.Ordinal))
            body = description.Trim() + "\n\n" + body;
        if (spec.IncludeTitle && !string.IsNullOrWhiteSpace(title))
            body = "# " + title + "\n\n" + body;

        return body.Trim('\r', '\n') + Environment.NewLine;
    }

    private static string RemoveMarkdownTitle(string body, string? title)
    {
        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        // Only remove the opening source heading. Headings in code examples are content.
        var heading = Regex.Match(lines[0], @"^ {0,3}#[ \t]+(?<title>.*?)(?:[ \t]+#+)?[ \t]*$", RegexOptions.None, TimeSpan.FromSeconds(1));
        if (heading.Success && heading.Groups["title"].Value.Equals(title, StringComparison.OrdinalIgnoreCase))
            return string.Join("\n", lines.Skip(1)).TrimStart('\r', '\n');
        return body;
    }

    private static string RenderMarkdownChildren(INode node)
    {
        var sb = new StringBuilder();
        foreach (var child in node.ChildNodes)
            sb.Append(RenderMarkdownNode(child));
        return sb.ToString();
    }

    private static string RenderMarkdownNode(INode node)
    {
        if (node.NodeType == NodeType.Text)
            return NormalizeInlineWhitespace(node.TextContent);

        if (node is not IElement element)
            return string.Empty;

        var tag = element.TagName.ToLowerInvariant();
        if (element.HasAttribute("data-pf-markdown-source"))
            return Block(element.TextContent);
        return tag switch
        {
            "h1" => RenderHeading(element, 1),
            "h2" => RenderHeading(element, 2),
            "h3" => RenderHeading(element, 3),
            "h4" => RenderHeading(element, 4),
            "h5" => RenderHeading(element, 5),
            "h6" => RenderHeading(element, 6),
            "p" => Block(RenderMarkdownChildren(element)),
            "br" => Environment.NewLine,
            "strong" or "b" => WrapInline("**", RenderMarkdownChildren(element)),
            "em" or "i" => WrapInline("*", RenderMarkdownChildren(element)),
            "code" when !string.Equals(element.ParentElement?.TagName, "pre", StringComparison.OrdinalIgnoreCase) => "`" + element.TextContent.Trim() + "`",
            "pre" => RenderCodeBlock(element),
            "table" => RenderTable(element),
            "dt" => Block("**" + CollapseMarkdownInline(RenderMarkdownChildren(element)) + "**"),
            "dd" => Block(RenderMarkdownChildren(element)),
            "a" => RenderLink(element),
            "img" => RenderImage(element),
            "ul" => RenderList(element, ordered: false),
            "ol" => RenderList(element, ordered: true),
            "li" => RenderMarkdownChildren(element),
            "blockquote" => RenderBlockquote(element),
            "hr" => Environment.NewLine + Environment.NewLine + "---" + Environment.NewLine + Environment.NewLine,
            "script" or "style" or "noscript" or "svg" => string.Empty,
            _ => IsMarkdownBlockElement(tag) ? Block(RenderMarkdownChildren(element)) : RenderMarkdownChildren(element)
        };
    }

    private static string RenderHeading(IElement element, int level)
    {
        var text = CollapseMarkdownInline(RenderMarkdownChildren(element));
        return string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : Environment.NewLine + Environment.NewLine + new string('#', level) + " " + text + Environment.NewLine + Environment.NewLine;
    }

    private static string RenderCodeBlock(IElement element)
    {
        var text = element.TextContent.Trim('\r', '\n');
        var code = element.QuerySelector("code") ?? element;
        var language = code.ClassList.Concat(element.ClassList)
            .FirstOrDefault(name => name.StartsWith("language-", StringComparison.OrdinalIgnoreCase))?[9..] ?? string.Empty;
        var runs = Regex.Matches(text, "`+", RegexOptions.None, TimeSpan.FromSeconds(1));
        var fence = new string('`', Math.Max(3, runs.Select(match => match.Length + 1).DefaultIfEmpty(3).Max()));
        return string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : Block(fence + language + "\n" + text + "\n" + fence);
    }

    private static string RenderTable(IElement table)
    {
        var rows = table.QuerySelectorAll("tr").Where(row => row.Closest("table") == table)
            .Select(row => row.Children.Where(cell => cell.TagName is "TH" or "TD")
                .Select(cell => CollapseMarkdownInline(RenderMarkdownChildren(cell)).Replace("|", "\\|", StringComparison.Ordinal)).ToArray())
            .Where(row => row.Length > 0).ToArray();
        if (rows.Length == 0) return string.Empty;
        var width = rows.Max(row => row.Length);
        string Row(string[] cells) => "| " + string.Join(" | ", cells.Concat(Enumerable.Repeat(string.Empty, width - cells.Length))) + " |";
        return Block(Row(rows[0]) + "\n" + Row(Enumerable.Repeat("---", width).ToArray()) +
                     string.Concat(rows.Skip(1).Select(row => "\n" + Row(row))));
    }

    private static string RenderLink(IElement element)
    {
        var text = CollapseMarkdownInline(RenderMarkdownChildren(element));
        var href = element.GetAttribute("href")?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            text = href ?? string.Empty;
        return string.IsNullOrWhiteSpace(href) ? text : $"[{EscapeMarkdownLinkText(text)}]({href})";
    }

    private static string RenderImage(IElement element)
    {
        var src = element.GetAttribute("src")?.Trim();
        if (string.IsNullOrWhiteSpace(src))
            return string.Empty;
        var alt = element.GetAttribute("alt")?.Trim() ?? string.Empty;
        return $"![{EscapeMarkdownLinkText(alt)}]({src})";
    }

    private static string RenderList(IElement element, bool ordered)
    {
        var index = 1;
        var lines = new List<string>();
        foreach (var item in element.Children.Where(static child => child.TagName.Equals("li", StringComparison.OrdinalIgnoreCase)))
        {
            var text = NormalizeMarkdownDocument(RenderMarkdownChildren(item)).Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;
            var prefix = ordered ? $"{index}. " : "- ";
            lines.Add(prefix + text.Replace(Environment.NewLine, Environment.NewLine + "  ", StringComparison.Ordinal));
            index++;
        }

        return lines.Count == 0
            ? string.Empty
            : Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, lines) + Environment.NewLine + Environment.NewLine;
    }

    private static string RenderBlockquote(IElement element)
    {
        var text = NormalizeMarkdownDocument(RenderMarkdownChildren(element)).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var quoted = string.Join(Environment.NewLine, text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Select(line => "> " + line));
        return Environment.NewLine + Environment.NewLine + quoted + Environment.NewLine + Environment.NewLine;
    }

    private static string Block(string text)
    {
        var normalized = NormalizeMarkdownDocument(text);
        return string.IsNullOrWhiteSpace(normalized)
            ? string.Empty
            : Environment.NewLine + Environment.NewLine + normalized + Environment.NewLine + Environment.NewLine;
    }

    private static string WrapInline(string marker, string text)
    {
        var normalized = CollapseMarkdownInline(text);
        return string.IsNullOrWhiteSpace(normalized) ? string.Empty : marker + normalized + marker;
    }

    private static bool IsMarkdownBlockElement(string tag)
        => tag is "address" or "article" or "aside" or "div" or "dl" or "fieldset" or "figcaption" or "figure" or
            "footer" or "form" or "header" or "main" or "nav" or "section" or "table";

    private static string NormalizeInlineWhitespace(string text)
        => string.IsNullOrWhiteSpace(text) ? string.Empty : MarkdownWhitespaceRegex.Replace(text, " ");

    private static string CollapseMarkdownInline(string text)
        => MarkdownWhitespaceRegex.Replace(NormalizeMarkdownDocument(text).Replace(Environment.NewLine, " ", StringComparison.Ordinal), " ").Trim();

    private static string NormalizeMarkdownDocument(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = new List<string>();
        string? fence = null;
        var indentedCode = false;
        foreach (var line in normalized.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (fence is null && (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal)))
                fence = new string(trimmed[0], trimmed.TakeWhile(c => c == trimmed[0]).Count());
            else if (fence is not null && trimmed.StartsWith(fence, StringComparison.Ordinal) && trimmed.Trim(fence[0]).Length == 0)
                fence = null;
            if (fence is null && !string.IsNullOrWhiteSpace(line))
                indentedCode = line.StartsWith("    ", StringComparison.Ordinal) || line.StartsWith('\t');
            if (fence is null && !indentedCode && string.IsNullOrWhiteSpace(line) && lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
                continue;
            lines.Add(line);
        }
        var start = lines.FindIndex(line => !string.IsNullOrWhiteSpace(line));
        if (start < 0) return string.Empty;
        var end = lines.FindLastIndex(line => !string.IsNullOrWhiteSpace(line));
        return string.Join(Environment.NewLine, lines.Skip(start).Take(end - start + 1));
    }

    private static string EscapeMarkdownLinkText(string text)
        => text.Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal);

}
