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
            !ExactSiteUrlMatches(propertyUri!, siteUri!))
        {
            AddCheck(
                checks,
                "provider.bing-site-url-mismatch",
                WebSearchProviderCheckSeverity.Error,
                "Bing Webmaster siteUrl must match the owning site baseUrl exactly.",
                siteId,
                providerId);
        }
    }

    private static void ValidateCloudflare(
        IReadOnlyDictionary<string, string?> settings,
        string siteId,
        string? siteBaseUrl,
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

        if (TryGetHttpUrl(siteBaseUrl, out var siteUri) && !siteUri!.IsDefaultPort)
        {
            AddCheck(
                checks,
                "provider.cloudflare-site-port-unsupported",
                WebSearchProviderCheckSeverity.Error,
                "Cloudflare traffic collection requires the owning site baseUrl to use the default HTTP(S) port.",
                siteId,
                providerId);
        }
    }

    private static bool DomainPropertyCoversSite(string domain, Uri siteUri)
    {
        var host = WebSearchProviderConfigurationFingerprint.NormalizeDnsHost(siteUri.IdnHost);
        var normalizedDomain = WebSearchProviderConfigurationFingerprint.NormalizeDnsHost(domain);
        return host.Equals(normalizedDomain, StringComparison.Ordinal) ||
               host.EndsWith("." + normalizedDomain, StringComparison.Ordinal);
    }

    private static bool UrlPrefixCoversSite(Uri propertyUri, Uri siteUri) =>
        propertyUri.Scheme.Equals(siteUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
        WebSearchProviderConfigurationFingerprint.NormalizeDnsHost(propertyUri.IdnHost)
            .Equals(WebSearchProviderConfigurationFingerprint.NormalizeDnsHost(siteUri.IdnHost), StringComparison.Ordinal) &&
        propertyUri.Port == siteUri.Port &&
        string.IsNullOrEmpty(propertyUri.Query) &&
        string.IsNullOrEmpty(propertyUri.Fragment) &&
        siteUri.AbsolutePath.StartsWith(propertyUri.AbsolutePath, StringComparison.Ordinal);

    private static bool ExactSiteUrlMatches(Uri propertyUri, Uri siteUri) =>
        propertyUri.Scheme.Equals(siteUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
        WebSearchProviderConfigurationFingerprint.NormalizeDnsHost(propertyUri.IdnHost)
            .Equals(WebSearchProviderConfigurationFingerprint.NormalizeDnsHost(siteUri.IdnHost), StringComparison.Ordinal) &&
        propertyUri.Port == siteUri.Port &&
        propertyUri.AbsolutePath.TrimEnd('/').Equals(siteUri.AbsolutePath.TrimEnd('/'), StringComparison.Ordinal) &&
        string.IsNullOrEmpty(propertyUri.Query) &&
        string.IsNullOrEmpty(propertyUri.Fragment);
}
