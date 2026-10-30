namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    /// <summary>Uses a published translation of an internal contact page, retaining its existing destination when absent.</summary>
    private static string ResolveStructuredContactUrl(SiteSpec spec, ContentItem item,
        ResolvedLocalizationConfig localization, IReadOnlyList<ContentItem> allItems)
    {
        var configured = spec.StructuredData?.OrganizationContactUrl;
        var baseUrl = ResolveLanguageBaseUrl(spec, localization, item.Language);
        if (string.IsNullOrWhiteSpace(configured) || IsAbsoluteHttpUrl(configured) ||
            configured.StartsWith("//", StringComparison.Ordinal) || IsNonRouteReference(configured))
            return ResolveAbsoluteUrl(baseUrl, configured);

        var suffixIndex = configured.IndexOfAny(['?', '#']);
        var path = suffixIndex < 0 ? configured : configured[..suffixIndex];
        var suffix = suffixIndex < 0 ? string.Empty : configured[suffixIndex..];
        var source = allItems.FirstOrDefault(page => !page.Draft &&
            string.Equals(NormalizeRouteForMatch(page.OutputPath), NormalizeRouteForMatch(path), StringComparison.OrdinalIgnoreCase));
        if (source is null)
            return ResolveAbsoluteUrl(baseUrl, configured);

        var route = ResolveLocalizedPageUrl(spec, localization, source, allItems,
            item.Language, source.Language);
        if (IsAbsoluteHttpUrl(route) || TryResolveExplicitLocalizedRoute(spec, localization, source, item.Language, out _))
        {
            // An explicit translated URL owns its query/fragment. Only inherit the
            // configured contact suffix when that destination does not supply one.
            var routeSuffixIndex = route.IndexOfAny(['?', '#']);
            var routePath = routeSuffixIndex < 0 ? route : route[..routeSuffixIndex];
            var routeSuffix = routeSuffixIndex < 0 ? suffix : route[routeSuffixIndex..];
            return ResolveAbsolutePublicUrl(spec, localization, item.Language, routePath) + routeSuffix;
        }
        var destination = allItems.FirstOrDefault(page => !page.Draft &&
            string.Equals(NormalizeRouteForMatch(page.OutputPath), NormalizeRouteForMatch(route), StringComparison.OrdinalIgnoreCase));
        if (destination is not null)
            return ResolveAbsolutePublicUrl(spec, localization, destination.Language, destination.OutputPath) + suffix;

        // Missing translations must not create a contact link to an unbuilt locale route.
        return ResolveAbsolutePublicUrl(spec, localization, source.Language, source.OutputPath) + suffix;
    }

    private static string StructuredPublisherName(SiteSpec spec) =>
        FirstStructuredValue(spec.StructuredData?.OrganizationName, spec.StructuredData?.OrganizationLegalName, spec.Name);

    private static string FirstStructuredValue(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string StructuredPublisherUrl(SiteSpec spec, string? baseUrl) =>
        ResolveAbsoluteUrl(baseUrl, FirstStructuredValue(spec.StructuredData?.OrganizationUrl, baseUrl));

    private static string StructuredEntityId(string? url, string fragment) =>
        string.IsNullOrWhiteSpace(url) ? string.Empty : url.TrimEnd('/') + "/#" + fragment;

    private static Dictionary<string, object?> StructuredReference(string id) => new() { ["@id"] = id };

    private static Dictionary<string, object?> BuildStructuredPublisher(SiteSpec spec, string? baseUrl)
    {
        var publisher = new Dictionary<string, object?> { ["@type"] = "Organization", ["name"] = StructuredPublisherName(spec) };
        var url = StructuredPublisherUrl(spec, baseUrl);
        if (!string.IsNullOrWhiteSpace(url))
        {
            publisher["url"] = url;
            if (spec.StructuredData?.WebPage == true)
                publisher["@id"] = StructuredEntityId(url, "organization");
        }
        return publisher;
    }

    private static Dictionary<string, object?> BuildWebPageStructuredData(SiteSpec spec, ContentItem item, string pageUrl, string baseUrl)
    {
        var requestedType = ReadMetaString(item.Meta, "schema.page_type");
        var page = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = requestedType == "ContactPage" ? "ContactPage" : "WebPage",
            ["@id"] = pageUrl + "#webpage",
            ["url"] = pageUrl,
            ["name"] = item.Title
        };
        if (!string.IsNullOrWhiteSpace(item.Description))
            page["description"] = item.Description;
        if (!string.IsNullOrWhiteSpace(item.Language))
            page["inLanguage"] = item.Language;
        if (spec.StructuredData?.Website == true && !string.IsNullOrWhiteSpace(baseUrl))
            page["isPartOf"] = StructuredReference(StructuredEntityId(baseUrl, "website"));
        if (requestedType == "ContactPage")
            page["mainEntity"] = BuildStructuredPublisher(spec, baseUrl);
        else if (spec.StructuredData?.SoftwareApplication == true && HasAnyMetaValue(item.Meta, "software", "softwareapplication"))
            page["mainEntity"] = StructuredReference(FirstStructuredValue(ReadMetaString(item.Meta, "software.id"), pageUrl + "#software"));
        else if (spec.StructuredData?.SoftwareSourceCode == true && HasAnyMetaValue(item.Meta, "source_code"))
            page["mainEntity"] = StructuredReference(FirstStructuredValue(ReadMetaString(item.Meta, "source_code.id"), pageUrl + "#source-code"));
        else if (spec.StructuredData is not null && !string.IsNullOrWhiteSpace(ResolveStructuredArticleType(spec.StructuredData, item)))
            page["mainEntity"] = StructuredReference(pageUrl + "#article");
        return page;
    }

    private static string BuildSoftwareSourceCodeStructuredDataScript(SiteSpec spec, ContentItem item, string pageUrl, string baseUrl)
    {
        if (!HasAnyMetaValue(item.Meta, "source_code"))
            return string.Empty;
        var model = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "SoftwareSourceCode",
            ["name"] = FirstStructuredValue(ReadMetaString(item.Meta, "source_code.name"), item.Title),
            ["url"] = pageUrl,
            ["publisher"] = BuildStructuredPublisher(spec, baseUrl)
        };
        var sourceCodeId = ReadMetaString(item.Meta, "source_code.id");
        if (!string.IsNullOrWhiteSpace(sourceCodeId) || spec.StructuredData?.WebPage == true)
            model["@id"] = FirstStructuredValue(sourceCodeId, pageUrl + "#source-code");
        if (spec.StructuredData?.WebPage == true)
            model["mainEntityOfPage"] = StructuredReference(pageUrl + "#webpage");
        foreach (var (key, property) in new[] { ("description", "description"), ("code_repository", "codeRepository"),
                     ("programming_language", "programmingLanguage"), ("runtime_platform", "runtimePlatform"), ("license", "license") })
        {
            var value = ReadMetaString(item.Meta, "source_code." + key);
            if (!string.IsNullOrWhiteSpace(value))
                model[property] = property is "codeRepository" or "license" ? ResolveAbsoluteUrl(baseUrl, value) : value;
        }
        return BuildJsonLdScript(model);
    }
}
