using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    private const int MaxPreconnectHints = 4;

    private static readonly Regex LinkTagRegex = new(
        "<link\\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);
    private static readonly Regex AssetTagRegex = new(
        "<(script|img|source)\\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);
    private static readonly Regex HeadOpenRegex = new(
        "<head\\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);
    private static readonly Regex HeadCloseRegex = new(
        "</head>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);
    private static readonly Regex HtmlCommentRegex = new(
        "<!--[\\s\\S]*?-->",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);
    private static readonly Regex AttributeRegex = new(
        "\\b(?<name>[a-zA-Z_:][\\w:.-]*)\\s*=\\s*(?:\"(?<dq>[^\"]*)\"|'(?<sq>[^']*)'|(?<bare>[^\\s\"'`=<>]+))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly HashSet<string> FetchDrivingLinkRelTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "stylesheet",
        "preload",
        "modulepreload",
        "prefetch",
        "icon",
        "apple-touch-icon",
        "mask-icon",
        "manifest"
    };

    internal static string OptimizeNetworkHints(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return html;
        if (!TryGetHeadContentBounds(html, out var headContentStart, out var headContentLength, out var headCloseIndex))
            return html;

        var headContent = html.Substring(headContentStart, headContentLength);
        var requiredOrigins = CollectRequiredNetworkOrigins(html, headContent);
        var hintedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rewrittenHeadContent = RewriteHintTagsInHeadContent(headContent, requiredOrigins, hintedOrigins);
        var resultHeadContent = rewrittenHeadContent;

        var missingOrigins = requiredOrigins
            .Where(origin => !hintedOrigins.Contains(origin))
            .OrderBy(origin => origin, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var availableSlots = Math.Max(0, MaxPreconnectHints - hintedOrigins.Count);
        if (availableSlots > 0 && missingOrigins.Count > 0)
        {
            var injected = string.Concat(
                missingOrigins
                    .Take(availableSlots)
                    .Select(origin =>
                        $"<link rel=\"preconnect\" href=\"{WebUtility.HtmlEncode(origin)}\" crossorigin=\"anonymous\" />"));
            if (!string.IsNullOrWhiteSpace(injected))
                resultHeadContent += injected;
        }

        if (string.Equals(headContent, resultHeadContent, StringComparison.Ordinal))
            return html;

        return string.Concat(
            html.AsSpan(0, headContentStart),
            resultHeadContent,
            html.AsSpan(headCloseIndex));
    }

    private static bool TryGetHeadContentBounds(string html, out int contentStart, out int contentLength, out int headCloseIndex)
    {
        contentStart = 0;
        contentLength = 0;
        headCloseIndex = 0;
        var openMatch = HeadOpenRegex.Match(html);
        if (!openMatch.Success)
            return false;

        var closeMatch = HeadCloseRegex.Match(html, openMatch.Index + openMatch.Length);
        if (!closeMatch.Success)
            return false;

        contentStart = openMatch.Index + openMatch.Length;
        headCloseIndex = closeMatch.Index;
        contentLength = Math.Max(0, headCloseIndex - contentStart);
        return true;
    }

    private static string RewriteHintTagsInHeadContent(
        string headContent,
        IReadOnlySet<string> requiredOrigins,
        ISet<string> hintedOrigins)
    {
        var rewritten = new StringBuilder(headContent.Length);
        var cursor = 0;
        foreach (Match comment in HtmlCommentRegex.Matches(headContent))
        {
            if (comment.Index > cursor)
            {
                var nonComment = headContent.Substring(cursor, comment.Index - cursor);
                rewritten.Append(RewriteHintTagsInSegment(nonComment, requiredOrigins, hintedOrigins));
            }

            rewritten.Append(comment.Value);
            cursor = comment.Index + comment.Length;
        }

        if (cursor < headContent.Length)
            rewritten.Append(RewriteHintTagsInSegment(headContent[cursor..], requiredOrigins, hintedOrigins));

        return rewritten.ToString();
    }

    private static string RewriteHintTagsInSegment(
        string segment,
        IReadOnlySet<string> requiredOrigins,
        ISet<string> hintedOrigins)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return segment;

        return LinkTagRegex.Replace(segment, match =>
        {
            var attributes = ParseTagAttributes(match.Value);
            if (!attributes.TryGetValue("rel", out var relValue))
                return match.Value;
            if (!ContainsRelToken(relValue, "preconnect") && !ContainsRelToken(relValue, "dns-prefetch"))
                return match.Value;
            if (!attributes.TryGetValue("href", out var href) || !TryGetExternalOrigin(href, out var origin))
                return match.Value;

            if (requiredOrigins.Contains(origin))
            {
                hintedOrigins.Add(origin);
                return match.Value;
            }

            return string.Empty;
        });
    }

    private static HashSet<string> CollectRequiredNetworkOrigins(string fullHtml, string headContent)
    {
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var headWithoutComments = StripHtmlComments(headContent);
        foreach (Match match in LinkTagRegex.Matches(headWithoutComments))
        {
            var attributes = ParseTagAttributes(match.Value);
            if (!attributes.TryGetValue("href", out var href) || string.IsNullOrWhiteSpace(href))
                continue;
            if (!ShouldConsiderLinkTag(attributes))
                continue;

            if (TryGetExternalOrigin(href, out var origin))
                required.Add(origin);
        }

        var htmlWithoutComments = StripHtmlComments(fullHtml);
        foreach (Match match in AssetTagRegex.Matches(htmlWithoutComments))
        {
            var attributes = ParseTagAttributes(match.Value);
            if (attributes.TryGetValue("src", out var src) && TryGetExternalOrigin(src, out var srcOrigin))
                required.Add(srcOrigin);

            if (attributes.TryGetValue("srcset", out var srcset))
            {
                foreach (var candidate in ParseSrcSet(srcset))
                {
                    if (TryGetExternalOrigin(candidate, out var srcsetOrigin))
                        required.Add(srcsetOrigin);
                }
            }
        }

        if (required.Contains("https://fonts.googleapis.com"))
            required.Add("https://fonts.gstatic.com");

        return required;
    }

    private static bool ShouldConsiderLinkTag(IReadOnlyDictionary<string, string> attributes)
    {
        if (!attributes.TryGetValue("rel", out var relValue) || string.IsNullOrWhiteSpace(relValue))
            return false;

        if (ContainsRelToken(relValue, "preconnect") || ContainsRelToken(relValue, "dns-prefetch"))
            return false;

        var tokens = relValue.Split(new[] { ' ', '\t', '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Any(token => FetchDrivingLinkRelTokens.Contains(token));
    }

    private static string StripHtmlComments(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;
        return HtmlCommentRegex.Replace(input, string.Empty);
    }

    private static Dictionary<string, string> ParseTagAttributes(string tag)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(tag))
            return attributes;

        foreach (Match match in AttributeRegex.Matches(tag))
        {
            if (!match.Success)
                continue;

            var name = match.Groups["name"].Value;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var value = match.Groups["dq"].Success
                ? match.Groups["dq"].Value
                : match.Groups["sq"].Success
                    ? match.Groups["sq"].Value
                    : match.Groups["bare"].Value;
            attributes[name] = value;
        }

        return attributes;
    }

    private static bool ContainsRelToken(string? relValue, string token)
    {
        if (string.IsNullOrWhiteSpace(relValue) || string.IsNullOrWhiteSpace(token))
            return false;

        var parts = relValue.Split(new[] { ' ', '\t', '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(part => part.Equals(token, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> ParseSrcSet(string srcset)
    {
        if (string.IsNullOrWhiteSpace(srcset))
            yield break;

        // srcset is a comma-separated candidate list: "url descriptor, url descriptor".
        var parts = srcset.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;
            var spaceIndex = trimmed.IndexOf(' ');
            if (spaceIndex > 0)
                trimmed = trimmed[..spaceIndex];
            if (!string.IsNullOrWhiteSpace(trimmed))
                yield return trimmed;
        }
    }

    private static bool TryGetExternalOrigin(string? value, out string origin)
    {
        origin = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var candidate = value.Trim();
        if (candidate.StartsWith("//", StringComparison.Ordinal))
            candidate = "https:" + candidate;

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            return false;
        if (string.IsNullOrWhiteSpace(uri.Host))
            return false;
        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        origin = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return !string.IsNullOrWhiteSpace(origin);
    }
}
