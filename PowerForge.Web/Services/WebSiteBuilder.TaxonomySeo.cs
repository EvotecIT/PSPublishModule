namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    private static string BuildTaxonomyIndexDescription(
        SiteSpec spec,
        string taxonomyName,
        string language,
        int termCount,
        int pageCount,
        IReadOnlyList<ContentItem> items)
    {
        if (!IsEnglishTaxonomyLanguage(language))
            return BuildLocalizedTaxonomyDescription(items);

        var siteName = string.IsNullOrWhiteSpace(spec.Name) ? "Site" : spec.Name.Trim();
        var taxonomyTitle = HumanizeSegment(taxonomyName);
        var terms = termCount == 1 ? "term" : "terms";
        var pages = pageCount == 1 ? "page" : "pages";
        return FitTaxonomyDescription(
            $"Browse {pageCount} published {siteName} {pages} through {termCount} {taxonomyTitle} {terms}, with each term linking to its matching content on the site.");
    }

    private static string BuildTaxonomyTermDescription(
        SiteSpec spec,
        string taxonomyName,
        string term,
        string language,
        int pageCount,
        IReadOnlyList<ContentItem> items)
    {
        if (!IsEnglishTaxonomyLanguage(language))
            return BuildLocalizedTaxonomyDescription(items);

        var siteName = string.IsNullOrWhiteSpace(spec.Name) ? "Site" : spec.Name.Trim();
        var pages = pageCount == 1 ? "page" : "pages";
        var taxonomyTitle = HumanizeSegment(taxonomyName);
        var relationship = taxonomyName.Equals("categories", StringComparison.OrdinalIgnoreCase)
            ? $"in the {term} category"
            : taxonomyName.Equals("tags", StringComparison.OrdinalIgnoreCase)
                ? $"tagged {term}"
                : $"filed under {term} in the {taxonomyTitle} taxonomy";

        return FitTaxonomyDescription(
            $"Explore {pageCount} published {siteName} {pages} {relationship}. This taxonomy page collects the matching content and links to each available page in one place.");
    }

    private static bool IsEnglishTaxonomyLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return true;

        var normalized = language.Trim();
        var separator = normalized.IndexOfAny(['-', '_']);
        if (separator >= 0)
            normalized = normalized[..separator];
        return normalized.Equals("en", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildLocalizedTaxonomyDescription(IReadOnlyList<ContentItem> items)
    {
        var fragments = items
            .Select(static item =>
            {
                var title = item.Title?.Trim() ?? string.Empty;
                var description = item.Description?.Trim() ?? string.Empty;
                if (description.Length == 0)
                    return title;
                return title.Length == 0 ? description : $"{title}: {description}";
            })
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return FitTaxonomyDescription(string.Join(". ", fragments));
    }

    private static string FitTaxonomyDescription(string description)
    {
        const int maximumLength = 160;
        if (description.Length <= maximumLength)
            return description;

        var wordBoundary = description.LastIndexOf(' ', maximumLength - 1);
        if (wordBoundary < 120)
            wordBoundary = maximumLength - 1;

        return description[..wordBoundary].TrimEnd(' ', ',', ';', ':', '-', '.', '!', '?') + ".";
    }
}
