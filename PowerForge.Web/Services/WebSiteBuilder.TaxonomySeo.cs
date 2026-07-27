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
            return BuildLocalizedTaxonomyDescription(
                spec,
                taxonomyName,
                null,
                language,
                termCount,
                pageCount,
                items);

        var siteName = string.IsNullOrWhiteSpace(spec.Name) ? "Site" : spec.Name.Trim();
        var taxonomyTitle = HumanizeSegment(taxonomyName);
        var terms = termCount == 1 ? "term" : "terms";
        var pages = pageCount == 1 ? "page" : "pages";
        return FitTaxonomyDescription(
            $"Browse {pageCount} published {siteName} {pages} through {termCount} {taxonomyTitle} {terms}, with each term linking to its matching content on the site.",
            ensureEnglishMinimum: true);
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
            return BuildLocalizedTaxonomyDescription(
                spec,
                taxonomyName,
                term,
                language,
                null,
                pageCount,
                items);

        var siteName = string.IsNullOrWhiteSpace(spec.Name) ? "Site" : spec.Name.Trim();
        var pages = pageCount == 1 ? "page" : "pages";
        var taxonomyTitle = HumanizeSegment(taxonomyName);
        var relationship = taxonomyName.Equals("categories", StringComparison.OrdinalIgnoreCase)
            ? $"in the {term} category"
            : taxonomyName.Equals("tags", StringComparison.OrdinalIgnoreCase)
                ? $"tagged {term}"
                : $"filed under {term} in the {taxonomyTitle} taxonomy";

        return FitTaxonomyDescription(
            $"Explore {pageCount} published {siteName} {pages} {relationship}. This taxonomy page groups the matching content across the available result set.",
            ensureEnglishMinimum: true);
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

    private static string BuildLocalizedTaxonomyDescription(
        SiteSpec spec,
        string taxonomyName,
        string? term,
        string language,
        int? termCount,
        int pageCount,
        IReadOnlyList<ContentItem> items)
    {
        var siteName = string.IsNullOrWhiteSpace(spec.Name) ? "Site" : spec.Name.Trim();
        var taxonomyTitle = HumanizeSegment(taxonomyName);
        var scope = string.IsNullOrWhiteSpace(term)
            ? $"{taxonomyTitle} — {siteName}"
            : $"{term.Trim()} — {siteName}";
        var fragments = items
            .Select(static item =>
            {
                var title = item.Title?.Trim() ?? string.Empty;
                var description = item.Description?.Trim() ?? string.Empty;
                var body = BuildSnippet(item.HtmlContent, 220);
                return string.Join(". ", new[] { title, description, body }
                    .Where(static value => value.Length > 0)
                    .Distinct(StringComparer.Ordinal));
            })
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var description = string.Join(". ", new[] { scope, string.Join(". ", fragments) }
            .Where(static value => value.Length > 0));
        description = EnsureLocalizedTaxonomyMinimum(
            description,
            spec,
            language,
            termCount,
            pageCount,
            items);

        return FitTaxonomyDescription(
            description,
            ensureEnglishMinimum: false);
    }

    private static string EnsureLocalizedTaxonomyMinimum(
        string description,
        SiteSpec spec,
        string language,
        int? termCount,
        int pageCount,
        IReadOnlyList<ContentItem> items)
    {
        const int minimumLength = 120;
        if (description.Length >= minimumLength)
            return description;

        // Keep the fallback language-neutral and source-derived rather than inventing
        // translated prose. Route and taxonomy facts are only added for sparse pages.
        var details = new[]
            {
                language.Trim(),
                termCount?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                pageCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                spec.BaseUrl?.Trim()
            }
            .Concat(items.SelectMany(static item => new[]
            {
                item.Collection?.Trim(),
                item.Slug?.Trim(),
                item.OutputPath?.Trim(),
                item.Date?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                string.Join(", ", item.Tags ?? Array.Empty<string>()),
                string.Join(", ", item.Categories ?? Array.Empty<string>())
            }))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return details.Length == 0
            ? description
            : $"{description.TrimEnd(' ', ',', ';', ':', '-', '.', '!', '?')}. {string.Join(" · ", details)}";
    }

    private static string FitTaxonomyDescription(string description, bool ensureEnglishMinimum)
    {
        const int minimumLength = 120;
        const int maximumLength = 160;
        if (ensureEnglishMinimum && description.Length < minimumLength)
            description = $"{description.TrimEnd(' ', ',', ';', ':', '-', '.', '!', '?')}. Browse the grouped results and open any matching page from this index.";

        if (description.Length <= maximumLength)
            return description;

        var wordBoundary = description.LastIndexOf(' ', maximumLength - 1);
        if (wordBoundary < minimumLength)
            wordBoundary = ClampTaxonomyDescriptionToUnicodeScalarBoundary(description, maximumLength - 1);

        return description[..wordBoundary].TrimEnd(' ', ',', ';', ':', '-', '.', '!', '?') + ".";
    }

    private static int ClampTaxonomyDescriptionToUnicodeScalarBoundary(string value, int boundary)
    {
        var safeBoundary = Math.Clamp(boundary, 0, value.Length);
        if (safeBoundary > 0 &&
            safeBoundary < value.Length &&
            char.IsHighSurrogate(value[safeBoundary - 1]) &&
            char.IsLowSurrogate(value[safeBoundary]))
        {
            safeBoundary--;
        }

        return safeBoundary;
    }
}
