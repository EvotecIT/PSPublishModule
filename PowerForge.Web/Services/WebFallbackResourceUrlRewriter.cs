using AngleSharp.Dom;
using HtmlTinkerX;

namespace PowerForge.Web;

/// <summary>Rebases only parsed root-relative resource URL attributes on localized fallback pages.</summary>
internal static class WebFallbackResourceUrlRewriter
{
    private static readonly string[] UrlAttributes =
    [
        "action", "data", "formaction", "href", "poster", "src"
    ];

    internal static string Rewrite(
        string html,
        string sourceRoute,
        string targetRoute,
        IReadOnlyList<PageResource>? resources)
    {
        if (string.IsNullOrEmpty(html) || resources is null || resources.Count == 0)
            return html;

        var sourceBase = "/" + sourceRoute.Trim('/');
        var targetBase = "/" + targetRoute.Trim('/');
        if (string.Equals(sourceBase, targetBase, StringComparison.Ordinal))
            return html;

        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var resource in resources)
        {
            if (string.IsNullOrWhiteSpace(resource.RelativePath))
                continue;
            var encodedPath = string.Join(
                "/",
                resource.RelativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .Select(Uri.EscapeDataString));
            if (encodedPath.Length == 0)
                continue;
            replacements[sourceBase.TrimEnd('/') + "/" + encodedPath] =
                targetBase.TrimEnd('/') + "/" + encodedPath;
        }
        if (replacements.Count == 0)
            return html;

        var document = HtmlParser.ParseWithAngleSharp(html);
        foreach (var element in document.All)
        {
            foreach (var attributeName in UrlAttributes)
            {
                var value = element.GetAttribute(attributeName);
                if (string.IsNullOrWhiteSpace(value))
                    continue;
                var rewritten = RewriteRootRelativeUrl(value, replacements);
                if (!string.Equals(value, rewritten, StringComparison.Ordinal))
                    element.SetAttribute(attributeName, rewritten);
            }

            var sourceSet = element.GetAttribute("srcset");
            if (!string.IsNullOrWhiteSpace(sourceSet))
            {
                var rewritten = RewriteSourceSet(sourceSet, replacements);
                if (!string.Equals(sourceSet, rewritten, StringComparison.Ordinal))
                    element.SetAttribute("srcset", rewritten);
            }

            var inlineStyle = element.GetAttribute("style");
            if (!string.IsNullOrWhiteSpace(inlineStyle))
            {
                var rewritten = RewriteCssUrls(inlineStyle, replacements);
                if (!string.Equals(inlineStyle, rewritten, StringComparison.Ordinal))
                    element.SetAttribute("style", rewritten);
            }
        }
        foreach (var style in document.QuerySelectorAll("style"))
        {
            var css = style.TextContent;
            var rewritten = RewriteCssUrls(css, replacements);
            if (!string.Equals(css, rewritten, StringComparison.Ordinal))
                style.TextContent = rewritten;
        }
        return document.Body?.InnerHtml ?? html;
    }

    private static string RewriteRootRelativeUrl(
        string value,
        IReadOnlyDictionary<string, string> replacements)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed[0] != '/' || trimmed.StartsWith("//", StringComparison.Ordinal))
            return value;

        var suffixIndex = trimmed.IndexOfAny(['?', '#']);
        var path = suffixIndex < 0 ? trimmed : trimmed.Substring(0, suffixIndex);
        if (!replacements.TryGetValue(path, out var replacement))
            return value;
        var suffix = suffixIndex < 0 ? string.Empty : trimmed.Substring(suffixIndex);
        var leading = value.Substring(0, value.IndexOf(trimmed, StringComparison.Ordinal));
        var trailing = value.Substring(leading.Length + trimmed.Length);
        return leading + replacement + suffix + trailing;
    }

    private static string RewriteSourceSet(
        string value,
        IReadOnlyDictionary<string, string> replacements)
    {
        var output = new System.Text.StringBuilder(value.Length);
        var index = 0;
        while (index < value.Length)
        {
            if (value[index] != '/' ||
                index + 1 >= value.Length ||
                value[index + 1] == '/' ||
                index > 0 && !char.IsWhiteSpace(value[index - 1]) && value[index - 1] != ',')
            {
                output.Append(value[index++]);
                continue;
            }

            var start = index;
            while (index < value.Length && !char.IsWhiteSpace(value[index]) && value[index] != ',')
                index++;
            output.Append(RewriteRootRelativeUrl(value.Substring(start, index - start), replacements));
        }
        return output.ToString();
    }

    private static string RewriteCssUrls(
        string css,
        IReadOnlyDictionary<string, string> replacements)
    {
        var output = new System.Text.StringBuilder(css.Length);
        var copiedThrough = 0;
        var quote = '\0';
        var escaped = false;
        var inComment = false;
        for (var index = 0; index < css.Length; index++)
        {
            var character = css[index];
            var next = index + 1 < css.Length ? css[index + 1] : '\0';
            if (inComment)
            {
                if (character == '*' && next == '/')
                {
                    inComment = false;
                    index++;
                }
                continue;
            }
            if (quote != '\0')
            {
                if (escaped)
                    escaped = false;
                else if (character == '\\')
                    escaped = true;
                else if (character == quote)
                    quote = '\0';
                continue;
            }
            if (character == '/' && next == '*')
            {
                inComment = true;
                index++;
                continue;
            }
            if (character is '\'' or '"')
            {
                quote = character;
                continue;
            }
            if (!IsUrlFunctionAt(css, index, out var valueStart, out var valueEnd))
                continue;

            var original = css.Substring(valueStart, valueEnd - valueStart);
            var rewritten = RewriteRootRelativeUrl(original, replacements);
            if (!string.Equals(original, rewritten, StringComparison.Ordinal))
            {
                output.Append(css, copiedThrough, valueStart - copiedThrough);
                output.Append(rewritten);
                copiedThrough = valueEnd;
            }
            index = valueEnd;
        }
        if (copiedThrough == 0)
            return css;
        output.Append(css, copiedThrough, css.Length - copiedThrough);
        return output.ToString();
    }

    private static bool IsUrlFunctionAt(string css, int index, out int valueStart, out int valueEnd)
    {
        valueStart = 0;
        valueEnd = 0;
        if (index > 0 && (char.IsLetterOrDigit(css[index - 1]) || css[index - 1] is '-' or '_') ||
            index + 3 > css.Length ||
            !string.Equals(css.Substring(index, 3), "url", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var cursor = index + 3;
        while (cursor < css.Length && char.IsWhiteSpace(css[cursor]))
            cursor++;
        if (cursor >= css.Length || css[cursor] != '(')
            return false;
        cursor++;
        while (cursor < css.Length && char.IsWhiteSpace(css[cursor]))
            cursor++;
        var valueQuote = cursor < css.Length && css[cursor] is '\'' or '"' ? css[cursor++] : '\0';
        valueStart = cursor;
        var escaped = false;
        while (cursor < css.Length)
        {
            var character = css[cursor];
            if (escaped)
            {
                escaped = false;
                cursor++;
                continue;
            }
            if (character == '\\')
            {
                escaped = true;
                cursor++;
                continue;
            }
            if (valueQuote != '\0' ? character == valueQuote : character == ')')
                break;
            cursor++;
        }
        if (cursor >= css.Length)
            return false;
        valueEnd = cursor;
        if (valueQuote != '\0')
        {
            cursor++;
            while (cursor < css.Length && char.IsWhiteSpace(css[cursor]))
                cursor++;
            if (cursor >= css.Length || css[cursor] != ')')
                return false;
        }
        return true;
    }
}
