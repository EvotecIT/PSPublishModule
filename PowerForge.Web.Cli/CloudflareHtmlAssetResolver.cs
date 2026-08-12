using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using AngleSharp.Dom;
using HtmlTinkerX;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

/// <summary>Discovers same-origin deployed assets from HTML so fingerprinted URLs can be cache-verified.</summary>
internal static class CloudflareHtmlAssetResolver
{
    internal static string[] Resolve(
        string baseUrl,
        IReadOnlyCollection<string> htmlSources,
        IReadOnlyCollection<string> assetPathPatterns,
        int timeoutMs)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var siteBase))
            throw new InvalidOperationException($"cloudflare: invalid baseUrl '{baseUrl}' for HTML asset discovery.");

        var sources = htmlSources
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(value => ResolveSameOriginUri(siteBase, value, "HTML discovery source"))
            .Distinct()
            .ToArray();
        var patterns = assetPathPatterns
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizePattern)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sources.Length == 0 || patterns.Length == 0)
            throw new InvalidOperationException("cloudflare: HTML asset discovery requires both 'discoverAssetsFrom' and 'assetPathPatterns'.");

        using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(Math.Clamp(timeoutMs, 1000, 120000)) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PowerForge.Web.CloudflareVerify/1.0");
        var matches = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            using var response = http.GetAsync(source).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"cloudflare: HTML asset discovery failed for '{source}' (HTTP {(int)response.StatusCode}).");

            var html = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var document = HtmlParser.ParseWithAngleSharp(html);
            foreach (var element in document.QuerySelectorAll("[src],[href]"))
            {
                foreach (var attributeName in new[] { "src", "href" })
                {
                    var value = element.GetAttribute(attributeName);
                    if (string.IsNullOrWhiteSpace(value))
                        continue;
                    if (!Uri.TryCreate(source, value, out var asset) || !HasSameOrigin(siteBase, asset))
                        continue;

                    var path = Uri.UnescapeDataString(asset.AbsolutePath);
                    foreach (var pattern in patterns.Where(pattern => WebGlobMatcher.IsMatch(pattern, path)))
                        matches.TryAdd(pattern, asset.GetLeftPart(UriPartial.Path));
                }
            }
        }

        var missing = patterns.Where(pattern => !matches.ContainsKey(pattern)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"cloudflare: deployed HTML did not reference an asset matching: {string.Join(", ", missing)}.");

        return matches.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static Uri ResolveSameOriginUri(Uri siteBase, string value, string label)
    {
        if (!Uri.TryCreate(siteBase, value.Trim(), out var resolved) || !HasSameOrigin(siteBase, resolved))
            throw new InvalidOperationException($"cloudflare: {label} '{value}' must resolve under {siteBase.GetLeftPart(UriPartial.Authority)}.");
        return resolved;
    }

    private static string NormalizePattern(string value)
    {
        var normalized = value.Trim().Replace('\\', '/');
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = "/" + normalized;
        if (normalized.Contains("..", StringComparison.Ordinal) || normalized.Any(char.IsControl))
            throw new InvalidOperationException($"cloudflare: unsafe asset path pattern '{value}'.");
        return normalized;
    }

    private static bool HasSameOrigin(Uri expected, Uri actual) =>
        string.Equals(expected.Scheme, actual.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(expected.Host, actual.Host, StringComparison.OrdinalIgnoreCase) &&
        expected.Port == actual.Port;
}
