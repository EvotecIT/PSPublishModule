using System.Net;
using System.Text.Json;
using HtmlTinkerX;

namespace PowerForge.Web;

public static partial class WebSitemapGenerator
{
    private static (string? Canonical, string? PublicationDate, bool NoIndex) ReadCanonicalAndPublication(string html, string documentUrl)
    {
        var document = HtmlParser.ParseWithHtmlAgilityPack(html);
        string? canonical = null;
        string? publicationDate = null;
        var noIndex = false;
        foreach (var node in document.DocumentNode.Descendants())
        {
            if (node.Name.Equals("link", StringComparison.OrdinalIgnoreCase) &&
                node.GetAttributeValue("rel", "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("canonical", StringComparer.OrdinalIgnoreCase))
                canonical ??= WebUtility.HtmlDecode(node.GetAttributeValue("href", ""));
            if (node.Name.Equals("meta", StringComparison.OrdinalIgnoreCase))
            {
                var name = node.GetAttributeValue("property", node.GetAttributeValue("name", node.GetAttributeValue("itemprop", "")));
                if (name.Equals("article:published_time", StringComparison.OrdinalIgnoreCase) || name.Equals("datePublished", StringComparison.OrdinalIgnoreCase))
                    publicationDate ??= WebUtility.HtmlDecode(node.GetAttributeValue("content", ""));
                if (RobotsNoIndexNames.Contains(name, StringComparer.OrdinalIgnoreCase) &&
                    node.GetAttributeValue("content", "").Split(new[] { ',', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Contains("noindex", StringComparer.OrdinalIgnoreCase)) noIndex = true;
            }
            if (node.Name.Equals("script", StringComparison.OrdinalIgnoreCase) &&
                node.GetAttributeValue("type", "").Equals("application/ld+json", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var json = JsonDocument.Parse(node.InnerText);
                    publicationDate ??= ReadJsonPublicationDate(json.RootElement);
                }
                catch (JsonException) { /* Invalid optional structured data is not a publication date. */ }
            }
        }
        if (!string.IsNullOrWhiteSpace(canonical) && Uri.TryCreate(documentUrl, UriKind.Absolute, out var documentUri))
        {
            var baseHref = document.DocumentNode.Descendants("base")
                .Select(node => node.Attributes["href"]?.Value).FirstOrDefault(value => value is not null);
            var canonicalBase = Uri.TryCreate(documentUri, WebUtility.HtmlDecode(baseHref), out var documentBase)
                ? documentBase : documentUri;
            if (Uri.TryCreate(canonicalBase, canonical, out var resolved)) canonical = resolved.AbsoluteUri;
        }
        return (canonical, publicationDate, noIndex);
    }

    private static string? ReadJsonPublicationDate(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("datePublished", out var date) && date.ValueKind == JsonValueKind.String) return date.GetString();
            if (element.TryGetProperty("@graph", out var graph)) return ReadJsonPublicationDate(graph);
        }
        if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray())
                if (ReadJsonPublicationDate(child) is { } date) return date;
        return null;
    }

    private static void ApplySitemapUrlPolicy(string siteRoot, string baseUrl, Dictionary<string, WebSitemapEntry> entries, WebSitemapOptions options,
        IReadOnlyDictionary<string, (string? Canonical, string? PublicationDate, bool NoIndex)> renderedSignals)
    {
        var exclusions = BuildExcludePatterns(options);
        foreach (var pair in entries.ToArray())
        {
            var entry = pair.Value;
            if (entry.Path.EndsWith("/404.html", StringComparison.OrdinalIgnoreCase) ||
                IsExcludedHtml(entry.Path, exclusions) ||
                (entry.NoIndex && !options.IncludeNoIndexHtml && !options.IncludeNoIndexPages))
            {
                entries.Remove(pair.Key);
                continue;
            }
            // Read the actual rendered signal even when older builder metadata lacks it.
            var hasSignals = renderedSignals.TryGetValue(entry.Path, out var signals);
            if (!hasSignals && TryResolveHtmlFileForRoute(siteRoot, entry.Path, out var htmlPath) && TryReadHtmlContent(htmlPath) is { } html)
            {
                signals = ReadCanonicalAndPublication(html, ResolveAbsoluteUrl(baseUrl, entry.Path));
                hasSignals = true;
            }
            if (hasSignals)
            {
                entry.Canonical = string.IsNullOrWhiteSpace(signals.Canonical) ? entry.Canonical : signals.Canonical;
                entry.PublicationDate ??= signals.PublicationDate;
                if (signals.NoIndex && !options.IncludeNoIndexHtml && !options.IncludeNoIndexPages)
                {
                    entries.Remove(pair.Key);
                    continue;
                }
            }
            if (string.IsNullOrWhiteSpace(entry.Canonical)) continue;
            var routeUrl = ResolveAbsoluteUrl(baseUrl, entry.Path);
            var canonicalUrl = ResolveAbsoluteUrl(baseUrl, entry.Canonical);
            if (!Uri.TryCreate(routeUrl, UriKind.Absolute, out var route) ||
                !Uri.TryCreate(canonicalUrl, UriKind.Absolute, out var canonical) || !route.Equals(canonical))
                entries.Remove(pair.Key);
        }
    }
}
