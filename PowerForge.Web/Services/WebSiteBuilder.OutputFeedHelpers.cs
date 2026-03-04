using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    private static string ResolveMetaDescriptionDefault(SiteSpec spec, ContentItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Description))
            return item.Description.Trim();

        var socialDescription = GetMetaString(item.Meta, "social_description");
        if (!string.IsNullOrWhiteSpace(socialDescription))
            return socialDescription.Trim();

        var snippet = BuildSnippet(item.HtmlContent, 180);
        if (!string.IsNullOrWhiteSpace(snippet))
            return snippet.Trim();

        return string.IsNullOrWhiteSpace(spec.Name)
            ? "Documentation and reference."
            : $"{spec.Name} documentation and reference.";
    }

    private static IEnumerable<string> ResolveRssCategories(ContentItem item)
    {
        var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tag in item.Tags ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(tag))
                categories.Add(tag.Trim());
        }

        foreach (var category in item.Categories ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(category))
                categories.Add(category.Trim());
        }

        foreach (var value in GetTaxonomyValues(item, new TaxonomySpec { Name = "categories" }))
        {
            if (!string.IsNullOrWhiteSpace(value))
                categories.Add(value.Trim());
        }

        return categories.OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
    }
}
