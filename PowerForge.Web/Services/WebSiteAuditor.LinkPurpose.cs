using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

/// <summary>Audits generated HTML output using static checks.</summary>
public static partial class WebSiteAuditor
{
    private static readonly Uri LinkPurposeUnknownOrigin = new("https://powerforge-audit.invalid/");

    private sealed class LinkPurposeSiteContext
    {
        public Uri? BaseUri { get; init; }
        public IReadOnlyDictionary<string, Uri> LanguageBaseUris { get; init; } = new Dictionary<string, Uri>();
    }

    /// <summary>Reads declared global and enabled language URLs once, without making an undeclared canonical authoritative.</summary>
    private static LinkPurposeSiteContext? LoadLinkPurposeSiteContext(string siteRoot)
    {
        try
        {
            var specPath = Path.Combine(siteRoot, "_powerforge", "site-spec.json");
            if (!File.Exists(specPath))
                return null;

            var spec = JsonSerializer.Deserialize<SiteSpec>(File.ReadAllText(specPath), WebJson.Options);
            if (spec is null)
                return null;

            var languageBases = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
            if (spec.Localization?.Enabled == true)
            {
                foreach (var language in spec.Localization.Languages)
                {
                    if (language is null || language.Disabled)
                        continue;
                    var code = WebSiteBuilder.NormalizeLanguageToken(language.Code);
                    var baseUri = ParseLinkPurposeHttpUri(language.BaseUrl);
                    if (!string.IsNullOrWhiteSpace(code) && baseUri is not null)
                        languageBases.TryAdd(code, baseUri);
                }
            }

            return new LinkPurposeSiteContext
            {
                BaseUri = ParseLinkPurposeHttpUri(spec.BaseUrl),
                LanguageBaseUris = languageBases
            };
        }
        catch
        {
            return null;
        }
    }

    private static void ValidateLinkPurposeConsistency(
        AngleSharp.Dom.IDocument doc,
        string relativePath,
        LinkPurposeSiteContext? siteContext,
        IReadOnlyDictionary<string, string[]> redirectMap,
        Action<string, string, string?, string, string?> addIssue)
    {
        var labelTargets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var labelDisplay = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var languagePrefix = ResolveAuditLanguagePrefix(relativePath);
        var baseUri = ResolveLinkPurposeBaseUri(doc, relativePath, siteContext, out var siteOrigin);

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
        LinkPurposeSiteContext? siteContext,
        out Uri? siteOrigin)
    {
        Uri? canonicalUri = null;
        if (siteContext is null || siteContext.BaseUri is null || siteContext.LanguageBaseUris.Count > 0)
        {
            var canonicals = GetHeadLinkValues(doc, "canonical");
            if (canonicals.Length == 1)
                canonicalUri = ParseLinkPurposeHttpUri(canonicals[0]);
        }

        Uri? declaredBase = null;
        var languageCode = WebSiteBuilder.NormalizeLanguageToken(doc.DocumentElement?.GetAttribute("lang"));
        if (siteContext is not null)
        {
            if (siteContext.LanguageBaseUris.TryGetValue(languageCode, out var languageBase))
                declaredBase = languageBase;
            else if (canonicalUri is not null)
            {
                declaredBase = siteContext.LanguageBaseUris.Values
                    .Where(baseUri => LinkPurposeBaseMatchesUri(baseUri, canonicalUri))
                    .OrderByDescending(baseUri => baseUri.AbsolutePath.TrimEnd('/').Length)
                    .FirstOrDefault();
            }
            declaredBase ??= siteContext.BaseUri;
        }

        siteOrigin = declaredBase ?? canonicalUri;
        // A canonical supplies only an origin; its path can intentionally name a different document.
        var pageBase = declaredBase?.AbsoluteUri
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

    private static bool LinkPurposeBaseMatchesUri(Uri declaredBase, Uri uri)
    {
        if (!LinkPurposeOriginsMatch(declaredBase, uri))
            return false;
        var basePath = declaredBase.AbsolutePath.TrimEnd('/');
        return basePath.Length == 0 ||
               uri.AbsolutePath.Equals(basePath, StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.StartsWith(basePath + "/", StringComparison.OrdinalIgnoreCase);
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
        if (IsLocalLinkPurposeDestination(destination, siteOrigin, hasExplicitOrigin))
        {
            var target = ResolveRedirectTarget(destination.AbsolutePath, redirectMap, languagePrefix);
            // The bounded resolver has already followed redirects; format its result without following them again.
            if (!Uri.TryCreate(destination, target, out var finalDestination) || !IsLinkPurposeHttpUri(finalDestination))
                return NormalizeNavHref(target);
            var targetHasExplicitOrigin = target.StartsWith("//", StringComparison.Ordinal) ||
                                          ParseLinkPurposeHttpUri(target) is not null;
            return FormatLinkPurposeDestination(finalDestination, siteOrigin, targetHasExplicitOrigin);
        }

        return FormatLinkPurposeDestination(destination, siteOrigin, hasExplicitOrigin);
    }

    private static string FormatLinkPurposeDestination(Uri destination, Uri? siteOrigin, bool hasExplicitOrigin)
    {
        if (IsLocalLinkPurposeDestination(destination, siteOrigin, hasExplicitOrigin))
            return NormalizeNavHref(destination.AbsolutePath);

        var path = destination.AbsolutePath;
        if (path.Length > 1 && path.EndsWith("/", StringComparison.Ordinal))
            path = path.TrimEnd('/');
        return $"{destination.Scheme}://{destination.Host}{(destination.IsDefaultPort ? string.Empty : ":" + destination.Port)}{path}";
    }

    private static bool IsLocalLinkPurposeDestination(Uri destination, Uri? siteOrigin, bool hasExplicitOrigin) =>
        siteOrigin is not null
            ? LinkPurposeOriginsMatch(destination, siteOrigin)
            : !hasExplicitOrigin && LinkPurposeOriginsMatch(destination, LinkPurposeUnknownOrigin);

    /// <summary>Matches local redirect metadata to resolved escaped paths without decoding reserved separators or external URLs.</summary>
    private static string NormalizeLinkPurposeRedirectHref(string? href)
    {
        var normalized = NormalizeNavHref(href);
        if (string.IsNullOrWhiteSpace(normalized) || WebLinkClassifier.IsExternalLink(normalized))
            return normalized;

        return Uri.TryCreate(LinkPurposeUnknownOrigin, normalized, out var destination) && IsLinkPurposeHttpUri(destination)
            ? NormalizeNavHref(destination.AbsolutePath)
            : normalized;
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
