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
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var siteBase) ||
            (siteBase.Scheme != Uri.UriSchemeHttp && siteBase.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException($"cloudflare: invalid baseUrl '{baseUrl}' for HTML asset discovery.");
        siteBase = EnsureDirectoryUri(siteBase);

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

        using var redirectHandler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(redirectHandler) { Timeout = TimeSpan.FromMilliseconds(Math.Clamp(timeoutMs, 1000, 120000)) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PowerForge.Web.CloudflareVerify/1.0");
        var matches = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            using var response = GetSameOrigin(http, siteBase, source, out var finalSource);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"cloudflare: HTML asset discovery failed for '{source}' (HTTP {(int)response.StatusCode}).");

            var html = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var document = HtmlParser.ParseWithAngleSharp(html);
            var documentBase = finalSource;
            var configuredBase = document.QuerySelector("base[href]")?.GetAttribute("href");
            if (!string.IsNullOrWhiteSpace(configuredBase) && Uri.TryCreate(finalSource, configuredBase, out var resolvedBase))
                documentBase = resolvedBase;
            foreach (var element in document.QuerySelectorAll("[src],[href]"))
            {
                foreach (var attributeName in new[] { "src", "href" })
                {
                    var value = element.GetAttribute(attributeName);
                    if (string.IsNullOrWhiteSpace(value))
                        continue;
                    if (!Uri.TryCreate(documentBase, value, out var asset) || !HasSameOrigin(siteBase, asset))
                        continue;

                    var path = Uri.UnescapeDataString(asset.AbsolutePath);
                    var verificationUri = new UriBuilder(asset) { Fragment = string.Empty }.Uri.AbsoluteUri;
                    foreach (var pattern in patterns.Where(pattern => WebGlobMatcher.IsMatch(pattern, path)))
                        matches.TryAdd(pattern, verificationUri);
                }
            }
        }

        var missing = patterns.Where(pattern => !matches.ContainsKey(pattern)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"cloudflare: deployed HTML did not reference an asset matching: {string.Join(", ", missing)}.");

        return matches.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static HttpResponseMessage GetSameOrigin(HttpClient http, Uri siteBase, Uri source, out Uri finalSource)
    {
        var current = source;
        for (var redirectCount = 0; redirectCount <= 10; redirectCount++)
        {
            var response = http.GetAsync(current).GetAwaiter().GetResult();
            if (!IsRedirect(response.StatusCode))
            {
                finalSource = response.RequestMessage?.RequestUri ?? current;
                return response;
            }

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null || !Uri.TryCreate(current, location, out var next))
                throw new InvalidOperationException($"cloudflare: HTML asset discovery received a redirect without a valid Location from '{current}'.");
            if (!HasSameOrigin(siteBase, next))
                throw new InvalidOperationException($"cloudflare: HTML asset discovery for '{source}' redirected outside the configured site origin.");
            current = next;
        }

        throw new InvalidOperationException($"cloudflare: HTML asset discovery for '{source}' exceeded 10 redirects.");
    }

    private static bool IsRedirect(System.Net.HttpStatusCode statusCode) =>
        statusCode is System.Net.HttpStatusCode.MovedPermanently or
            System.Net.HttpStatusCode.Redirect or
            System.Net.HttpStatusCode.RedirectMethod or
            System.Net.HttpStatusCode.TemporaryRedirect or
            System.Net.HttpStatusCode.PermanentRedirect;

    private static Uri ResolveSameOriginUri(Uri siteBase, string value, string label)
    {
        var candidate = value.Trim();
        Uri? resolved;
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var absolute))
        {
            resolved = absolute;
        }
        else
        {
            // Pipeline paths are site-relative: a leading slash means the root of
            // the configured deployment, not the hostname root. This matters for
            // GitHub Pages project sites such as https://host/project/.
            var relative = candidate.TrimStart('/');
            resolved = relative.Length == 0 ? siteBase : new Uri(siteBase, relative);
        }

        if (!HasSameOrigin(siteBase, resolved))
            throw new InvalidOperationException($"cloudflare: {label} '{value}' must resolve under {siteBase.GetLeftPart(UriPartial.Authority)}.");
        return resolved;
    }

    private static Uri EnsureDirectoryUri(Uri value)
    {
        var builder = new UriBuilder(value) { Fragment = string.Empty, Query = string.Empty };
        if (!builder.Path.EndsWith("/", StringComparison.Ordinal))
            builder.Path += "/";
        return builder.Uri;
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
