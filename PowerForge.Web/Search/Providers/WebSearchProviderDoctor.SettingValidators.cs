namespace PowerForge.Web;

public static partial class WebSearchProviderDoctor
{
    private static void ValidateGoogleSearchConsole(
        IReadOnlyDictionary<string, string?> settings,
        string siteId,
        string? siteBaseUrl,
        string providerId,
        List<WebSearchProviderCheck> checks)
    {
        if (!settings.TryGetValue("property", out var property) || string.IsNullOrWhiteSpace(property))
            return;
        var value = property.Trim();
        if (value.StartsWith("sc-domain:", StringComparison.OrdinalIgnoreCase))
        {
            var domain = value["sc-domain:".Length..];
            if (!value.StartsWith("sc-domain:", StringComparison.Ordinal) ||
                domain.Length == 0 ||
                !domain.Contains('.') ||
                domain.Any(char.IsWhiteSpace) ||
                domain.Contains('/') ||
                domain.Contains(':') ||
                Uri.CheckHostName(domain) != UriHostNameType.Dns)
            {
                AddCheck(
                    checks,
                    "provider.gsc-property-invalid",
                    WebSearchProviderCheckSeverity.Error,
                    "Google Search Console domain property must use sc-domain:<domain> with a valid DNS name.",
                    siteId,
                    providerId);
                return;
            }

            if (TryGetHttpUrl(siteBaseUrl, out var domainSiteUri) &&
                !DomainPropertyCoversSite(domain, domainSiteUri!))
            {
                AddCheck(
                    checks,
                    "provider.gsc-property-site-mismatch",
                    WebSearchProviderCheckSeverity.Error,
                    "Google Search Console domain property does not cover the owning site baseUrl.",
                    siteId,
                    providerId);
            }
            return;
        }

        if (!TryGetHttpUrl(value, out var propertyUri))
        {
            AddCheck(
                checks,
                "provider.gsc-property-invalid",
                WebSearchProviderCheckSeverity.Error,
                "Google Search Console property must be sc-domain:<domain> or an absolute HTTP(S) URL-prefix property.",
                siteId,
                providerId);
            return;
        }

        if (TryGetHttpUrl(siteBaseUrl, out var prefixSiteUri) &&
            !UrlPrefixCoversSite(propertyUri!, prefixSiteUri!))
        {
            AddCheck(
                checks,
                "provider.gsc-property-site-mismatch",
                WebSearchProviderCheckSeverity.Error,
                "Google Search Console URL-prefix property does not cover the owning site baseUrl.",
                siteId,
                providerId);
        }
    }

    private static void ValidateBingWebmaster(
        IReadOnlyDictionary<string, string?> settings,
        string siteId,
        string? siteBaseUrl,
        string providerId,
        List<WebSearchProviderCheck> checks)
    {
        if (!settings.TryGetValue("siteUrl", out var value) || string.IsNullOrWhiteSpace(value))
            return;

        if (!TryGetHttpUrl(value, out var propertyUri))
        {
            AddCheck(
                checks,
                "provider.bing-site-url-invalid",
                WebSearchProviderCheckSeverity.Error,
                "Bing Webmaster siteUrl must be an absolute HTTP(S) URL.",
                siteId,
                providerId);
            return;
        }

        if (TryGetHttpUrl(siteBaseUrl, out var siteUri) &&
            !UrlPrefixCoversSite(propertyUri!, siteUri!))
        {
            AddCheck(
                checks,
                "provider.bing-site-url-mismatch",
                WebSearchProviderCheckSeverity.Error,
                "Bing Webmaster siteUrl does not cover the owning site baseUrl.",
                siteId,
                providerId);
        }
    }

    private static void ValidateCloudflare(
        IReadOnlyDictionary<string, string?> settings,
        string siteId,
        string? _,
        string providerId,
        List<WebSearchProviderCheck> checks)
    {
        if (settings.TryGetValue("zoneId", out var value) &&
            !string.IsNullOrWhiteSpace(value) &&
            !CloudflareZoneRegex.IsMatch(value.Trim()))
        {
            AddCheck(
                checks,
                "provider.cloudflare-zone-invalid",
                WebSearchProviderCheckSeverity.Error,
                "Cloudflare zoneId must be a 32-character hexadecimal zone identifier.",
                siteId,
                providerId);
        }
    }

    private static bool DomainPropertyCoversSite(string domain, Uri siteUri)
    {
        var host = siteUri.IdnHost;
        return host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
    }

    private static bool UrlPrefixCoversSite(Uri propertyUri, Uri siteUri) =>
        propertyUri.Scheme.Equals(siteUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
        propertyUri.IdnHost.Equals(siteUri.IdnHost, StringComparison.OrdinalIgnoreCase) &&
        propertyUri.Port == siteUri.Port &&
        string.IsNullOrEmpty(propertyUri.Query) &&
        string.IsNullOrEmpty(propertyUri.Fragment) &&
        siteUri.AbsolutePath.StartsWith(propertyUri.AbsolutePath, StringComparison.Ordinal);
}
