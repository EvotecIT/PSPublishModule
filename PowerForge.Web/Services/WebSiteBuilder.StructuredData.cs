using System.Text.Json;

namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    private static string BuildStructuredDataHtml(SiteSpec spec, ContentItem item, BreadcrumbItem[] breadcrumbs)
    {
        if (spec.StructuredData is null || !spec.StructuredData.Enabled)
            return string.Empty;

        // Structured data defaults to enabled when site structured data is enabled.
        // Authors can opt out per page with: meta.structured_data: false
        if (TryGetMetaBool(item.Meta, "structured_data", out var enabled) && !enabled)
            return string.Empty;

        var localization = ResolveLocalizationConfig(spec);
        var languageBaseUrl = ResolveLanguageBaseUrl(spec, localization, item.Language);
        var baseUrl = languageBaseUrl?.TrimEnd('/') ?? string.Empty;
        var scripts = new List<string>();

        // A BreadcrumbList represents a path through a hierarchy. A single home-page
        // item is not a useful trail and is not eligible for Google's breadcrumb
        // presentation, which requires at least two ListItem entries.
        if (spec.StructuredData.Breadcrumbs && breadcrumbs.Length > 1)
        {
            var items = breadcrumbs
                .Select((crumb, index) => new Dictionary<string, object?>
                {
                    ["@type"] = "ListItem",
                    ["position"] = index + 1,
                    ["name"] = crumb.Title ?? string.Empty,
                    ["item"] = ResolveAbsolutePublicUrl(spec, localization, item.Language, crumb.Url)
                })
                .ToArray();

            scripts.Add(BuildJsonLdScript(new Dictionary<string, object?>
            {
                ["@context"] = "https://schema.org",
                ["@type"] = "BreadcrumbList",
                ["itemListElement"] = items
            }));
        }

        var pageUrl = ResolveAbsolutePublicUrl(spec, localization, item.Language, ResolveCanonicalRoute(spec, item));
        if (spec.StructuredData.Website && (item.Kind == PageKind.Home || spec.StructuredData.WebPage))
        {
            var webSiteModel = new Dictionary<string, object?>
            {
                ["@context"] = "https://schema.org",
                ["@type"] = "WebSite",
                ["name"] = spec.Name,
                ["url"] = string.IsNullOrWhiteSpace(baseUrl) ? pageUrl : baseUrl
            };

            if (spec.StructuredData.WebPage)
            {
                webSiteModel["@id"] = StructuredEntityId(baseUrl, "website");
                webSiteModel["publisher"] = BuildStructuredPublisher(spec, languageBaseUrl);
            }

            if (spec.Features?.Any(feature => feature.Equals("search", StringComparison.OrdinalIgnoreCase)) == true)
            {
                var searchUrl = ResolveAbsoluteUrl(languageBaseUrl, "/search/?q={search_term_string}");
                if (!string.IsNullOrWhiteSpace(searchUrl))
                {
                    webSiteModel["potentialAction"] = new Dictionary<string, object?>
                    {
                        ["@type"] = "SearchAction",
                        ["target"] = searchUrl,
                        ["query-input"] = "required name=search_term_string"
                    };
                }
            }

            scripts.Add(BuildJsonLdScript(webSiteModel));
        }

        if (spec.StructuredData.Organization && (item.Kind == PageKind.Home || spec.StructuredData.WebPage))
        {
            var organizationModel = BuildStructuredPublisher(spec, languageBaseUrl);
            organizationModel["@context"] = "https://schema.org";
            if (!string.IsNullOrWhiteSpace(spec.StructuredData.OrganizationLegalName))
                organizationModel["legalName"] = spec.StructuredData.OrganizationLegalName;
            if (!string.IsNullOrWhiteSpace(spec.StructuredData.OrganizationDescription))
                organizationModel["description"] = spec.StructuredData.OrganizationDescription;

            var logoSource = string.IsNullOrWhiteSpace(spec.StructuredData.OrganizationLogo)
                ? spec.Social?.Image
                : spec.StructuredData.OrganizationLogo;
            var logo = ResolveAbsoluteUrl(languageBaseUrl, logoSource);
            if (!string.IsNullOrWhiteSpace(logo))
                organizationModel["logo"] = logo;

            if (spec.StructuredData.OrganizationSameAs is { Length: > 0 })
            {
                var sameAs = spec.StructuredData.OrganizationSameAs
                    .Where(link => !string.IsNullOrWhiteSpace(link))
                    .Select(link => ResolveAbsoluteUrl(languageBaseUrl, link))
                    .Where(link => !string.IsNullOrWhiteSpace(link))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (sameAs.Length > 0)
                    organizationModel["sameAs"] = sameAs;
            }

            var contactPoint = new Dictionary<string, object?>
            {
                ["@type"] = "ContactPoint"
            };
            var hasContactData = false;
            if (!string.IsNullOrWhiteSpace(spec.StructuredData.OrganizationEmail))
            {
                contactPoint["email"] = spec.StructuredData.OrganizationEmail;
                hasContactData = true;
            }

            if (!string.IsNullOrWhiteSpace(spec.StructuredData.OrganizationTelephone))
            {
                contactPoint["telephone"] = spec.StructuredData.OrganizationTelephone;
                hasContactData = true;
            }

            var contactUrl = ResolveAbsoluteUrl(languageBaseUrl, spec.StructuredData.OrganizationContactUrl);
            if (!string.IsNullOrWhiteSpace(contactUrl))
            {
                contactPoint["url"] = contactUrl;
                hasContactData = true;
            }

            if (hasContactData)
            {
                contactPoint["contactType"] = "customer support";
                organizationModel["contactPoint"] = new[] { contactPoint };
            }

            scripts.Add(BuildJsonLdScript(organizationModel));
        }

        var articleType = ResolveStructuredArticleType(spec.StructuredData, item);
        if (!string.IsNullOrWhiteSpace(articleType))
        {
            var articleScript = BuildArticleStructuredDataScript(spec, item, pageUrl, articleType);
            if (!string.IsNullOrWhiteSpace(articleScript))
                scripts.Add(articleScript);
        }

        var faqModel = spec.StructuredData.FaqPage ? BuildFaqStructuredDataModel(item, pageUrl) : null;
        if (spec.StructuredData.WebPage)
        {
            var pageModel = BuildWebPageStructuredData(spec, item, pageUrl, baseUrl);
            if (faqModel is not null)
            {
                if (string.Equals(pageModel["@type"] as string, "ContactPage", StringComparison.Ordinal))
                {
                    pageModel["@type"] = new[] { "ContactPage", "FAQPage" };
                    pageModel["about"] = pageModel["mainEntity"];
                }
                else
                    pageModel["@type"] = "FAQPage";
                pageModel["mainEntity"] = faqModel["mainEntity"];
                if (faqModel.TryGetValue("name", out var faqName))
                    pageModel["name"] = faqName;
                if (faqModel.TryGetValue("description", out var faqDescription))
                    pageModel["description"] = faqDescription;
            }
            scripts.Add(BuildJsonLdScript(pageModel));
        }
        else if (faqModel is not null)
            scripts.Add(BuildJsonLdScript(faqModel));

        if (spec.StructuredData.HowTo)
        {
            var howToScript = BuildHowToStructuredDataScript(item, pageUrl);
            if (!string.IsNullOrWhiteSpace(howToScript))
                scripts.Add(howToScript);
        }

        if (spec.StructuredData.SoftwareApplication)
        {
            var softwareScript = BuildSoftwareApplicationStructuredDataScript(spec, item, pageUrl);
            if (!string.IsNullOrWhiteSpace(softwareScript))
                scripts.Add(softwareScript);
        }

        if (spec.StructuredData.Product)
        {
            var productScript = BuildProductStructuredDataScript(spec, item, pageUrl);
            if (!string.IsNullOrWhiteSpace(productScript))
                scripts.Add(productScript);
        }

        if (spec.StructuredData.SoftwareSourceCode)
        {
            var sourceScript = BuildSoftwareSourceCodeStructuredDataScript(spec, item, pageUrl, baseUrl);
            if (!string.IsNullOrWhiteSpace(sourceScript))
                scripts.Add(sourceScript);
        }

        return scripts.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, scripts.Where(static script => !string.IsNullOrWhiteSpace(script)));
    }

    private static string BuildJsonLdScript(IReadOnlyDictionary<string, object?> payload)
    {
        return PowerForge.Web.Protocols.StructuredData.JsonLdWriter.WriteScript(payload);
    }

}
