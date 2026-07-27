namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    private static string BuildTaxonomyIndexDescription(
        SiteSpec spec,
        string taxonomyName,
        int termCount,
        int pageCount)
    {
        var siteName = string.IsNullOrWhiteSpace(spec.Name) ? "Site" : spec.Name.Trim();
        var taxonomyTitle = HumanizeSegment(taxonomyName);
        var topics = termCount == 1 ? "topic" : "topics";
        var pages = pageCount == 1 ? "page" : "pages";
        return FitTaxonomyDescription(
            $"Browse {siteName} content by {taxonomyTitle}, with {termCount} {topics} across {pageCount} published {pages}, including guides, articles, examples, and reference material.");
    }

    private static string BuildTaxonomyTermDescription(
        SiteSpec spec,
        string taxonomyName,
        string term,
        int pageCount)
    {
        var siteName = string.IsNullOrWhiteSpace(spec.Name) ? "Site" : spec.Name.Trim();
        var pages = pageCount == 1 ? "page" : "pages";
        var relationship = taxonomyName.Equals("categories", StringComparison.OrdinalIgnoreCase)
            ? $"in the {term} category"
            : $"tagged {term}";

        return FitTaxonomyDescription(
            $"Explore {pageCount} {siteName} {pages} {relationship}, with related guides, articles, examples, workflow notes, and reference material collected in one place.");
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
