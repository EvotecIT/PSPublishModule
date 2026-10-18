namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    private static SiteSpec? LoadApiStructuredDataSite(WebApiDocsOptions options, ApiSocialProfile social)
    {
        var path = FirstNonEmpty(options.SiteConfigPath, options.NavJsonPath);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        var (site, _) = WebSiteSpecLoader.LoadWithPath(path, WebJson.Options);
        if (site.StructuredData?.Enabled != true)
            return null;

        // Use the same public origin as the canonical link, including a CLI override.
        if (!string.IsNullOrWhiteSpace(social.SiteBaseUrl))
            site.BaseUrl = social.SiteBaseUrl;
        return site;
    }

    private static string BuildApiPageHeadHtml(
        string head,
        SiteSpec? site,
        WebApiDocsOptions options,
        ApiSocialProfile social,
        string title,
        string description,
        string route,
        string? entryName = null)
    {
        if (site is null)
            return head;

        var url = ResolveApiAbsoluteUrl(social.SiteBaseUrl, route);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var absolute) ||
            (absolute.Scheme != Uri.UriSchemeHttps && absolute.Scheme != Uri.UriSchemeHttp))
            return head;

        var page = new ContentItem
        {
            Title = title,
            Description = description,
            OutputPath = route,
            Canonical = url,
            Language = options.LanguageCode ?? site.Localization?.DefaultLanguage ?? string.Empty
        };
        var breadcrumbs = new List<BreadcrumbItem>
        {
            new() { Title = "Home", Url = site.BaseUrl?.TrimEnd('/') + "/" },
            new() { Title = options.Title, Url = NormalizeApiIndexRoute(options.BaseUrl), IsCurrent = entryName is null }
        };
        if (entryName is not null)
            breadcrumbs.Add(new BreadcrumbItem { Title = entryName, Url = url, IsCurrent = true });

        // Keep publisher identity, feature flags and JSON escaping in the site renderer.
        var structuredData = WebSiteBuilder.RenderStructuredData(site, page, breadcrumbs.ToArray(), null);
        return JoinHtmlFragments(head, structuredData);
    }
}
