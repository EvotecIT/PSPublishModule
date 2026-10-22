using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

/// <summary>Audits generated HTML output using static checks.</summary>
public static partial class WebSiteAuditor
{
    private static readonly Uri LinkPurposeUnknownOrigin = new("https://powerforge-audit.invalid/");

    /// <summary>Reads the declared site URL once; absent or unusable metadata leaves page canonical fallback available.</summary>
    private static Uri? LoadLinkPurposeSiteBaseUri(string siteRoot)
    {
        try
        {
            var specPath = Path.Combine(siteRoot, "_powerforge", "site-spec.json");
            if (!File.Exists(specPath))
                return null;

            var spec = JsonSerializer.Deserialize<SiteSpec>(File.ReadAllText(specPath), WebJson.Options);
            return ParseLinkPurposeHttpUri(spec?.BaseUrl);
        }
        catch
        {
            return null;
        }
    }

    private static void ValidateLinkPurposeConsistency(
        AngleSharp.Dom.IDocument doc,
        string relativePath,
        Uri? siteBaseUri,
        IReadOnlyDictionary<string, string[]> redirectMap,
        Action<string, string, string?, string, string?> addIssue)
    {
        var labelTargets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var labelDisplay = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var languagePrefix = ResolveAuditLanguagePrefix(relativePath);
        var baseUri = ResolveLinkPurposeBaseUri(doc, relativePath, siteBaseUri, out var siteOrigin);

        foreach (var anchor in doc.QuerySelectorAll("a[href]"))
        {
            var href = anchor.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href) || ShouldSkipLink(href))
                continue;

            var label = GetAccessibleLinkLabel(anchor);
            if (string.IsNullOrWhiteSpace(label))
                continue;

            var destination = NormalizeLinkPurposeDestination(href, baseUri, siteOrigin, redirectMap, languagePrefix);
            if (string.IsNullOrWhiteSpace(destination))
                continue;

            var normalizedLabel = label.Trim();
            if (!labelTargets.TryGetValue(normalizedLabel, out var targets))
            {
                targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                labelTargets[normalizedLabel] = targets;
                labelDisplay[normalizedLabel] = normalizedLabel;
            }
            targets.Add(destination);
        }

        foreach (var pair in labelTargets)
        {
            if (pair.Value.Count <= 1)
                continue;

            var targets = pair.Value
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var label = labelDisplay.TryGetValue(pair.Key, out var value)
                ? value
                : pair.Key;

            addIssue("warning", "link-purpose", relativePath,
                $"link label '{label}' points to multiple destinations: {string.Join(", ", targets)}.",
                $"link-purpose:{label}:{string.Join("|", targets)}");
        }
    }

    /// <summary>Uses the actual output route, preserving a declared project prefix and raw HTML base URL semantics.</summary>
    private static Uri ResolveLinkPurposeBaseUri(
        AngleSharp.Dom.IDocument doc,
        string relativePath,
        Uri? siteBaseUri,
        out Uri? siteOrigin)
    {
        siteOrigin = siteBaseUri;
        if (siteOrigin is null)
        {
            var canonicals = GetHeadLinkValues(doc, "canonical");
            if (canonicals.Length == 1)
                siteOrigin = ParseLinkPurposeHttpUri(canonicals[0]);
        }

        // A canonical supplies only an origin; its path can intentionally name a different document.
        var pageBase = siteBaseUri?.AbsoluteUri
            ?? siteOrigin?.GetLeftPart(UriPartial.Authority)
            ?? LinkPurposeUnknownOrigin.AbsoluteUri;
        var pageUri = new Uri(CombineUrl(pageBase, ToRoutePath(relativePath)));
        var rawBaseHref = doc.QuerySelector("base[href]")?.GetAttribute("href");
        if (!string.IsNullOrWhiteSpace(rawBaseHref) &&
            Uri.TryCreate(pageUri, rawBaseHref, out var htmlBaseUri) &&
            IsLinkPurposeHttpUri(htmlBaseUri))
        {
            return htmlBaseUri;
        }

        return pageUri;
    }

    private static string GetAccessibleLinkLabel(AngleSharp.Dom.IElement anchor)
    {
        var ariaLabel = anchor.GetAttribute("aria-label");
        if (!string.IsNullOrWhiteSpace(ariaLabel))
            return Regex.Replace(ariaLabel, "\\s+", " ").Trim();

        var text = anchor.TextContent;
        if (!string.IsNullOrWhiteSpace(text))
            return Regex.Replace(text, "\\s+", " ").Trim();

        var title = anchor.GetAttribute("title");
        if (!string.IsNullOrWhiteSpace(title))
            return Regex.Replace(title, "\\s+", " ").Trim();

        return string.Empty;
    }

    /// <summary>Compares resolved destinations and applies local redirects only when their origin is proven local.</summary>
    private static string NormalizeLinkPurposeDestination(
        string href,
        Uri baseUri,
        Uri? siteOrigin,
        IReadOnlyDictionary<string, string[]> redirectMap,
        string languagePrefix)
    {
        var normalized = StripQueryAndFragment(href).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        if (!Uri.TryCreate(baseUri, normalized, out var destination) || !IsLinkPurposeHttpUri(destination))
            return NormalizeNavHref(normalized);

        var hasExplicitOrigin = normalized.StartsWith("//", StringComparison.Ordinal) ||
                                ParseLinkPurposeHttpUri(normalized) is not null;
        var isLocal = siteOrigin is not null
            ? LinkPurposeOriginsMatch(destination, siteOrigin)
            : !hasExplicitOrigin && LinkPurposeOriginsMatch(destination, LinkPurposeUnknownOrigin);
        if (isLocal)
            return ResolveRedirectTarget(destination.AbsolutePath, redirectMap, languagePrefix);

        var path = destination.AbsolutePath;
        if (path.Length > 1 && path.EndsWith("/", StringComparison.Ordinal))
            path = path.TrimEnd('/');
        return $"{destination.Scheme}://{destination.Host}{(destination.IsDefaultPort ? string.Empty : ":" + destination.Port)}{path}";
    }

    private static Uri? ParseLinkPurposeHttpUri(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && IsLinkPurposeHttpUri(uri)
            ? uri
            : null;
    }

    private static bool IsLinkPurposeHttpUri(Uri uri) =>
        uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
        uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static bool LinkPurposeOriginsMatch(Uri left, Uri right) =>
        left.Scheme.Equals(right.Scheme, StringComparison.OrdinalIgnoreCase) &&
        left.Host.Equals(right.Host, StringComparison.OrdinalIgnoreCase) &&
        left.Port == right.Port;
}
