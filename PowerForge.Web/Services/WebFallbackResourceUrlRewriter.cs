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
}
