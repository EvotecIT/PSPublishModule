using System.Text;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    private sealed class ApiSuiteContext
    {
        public string? Title { get; init; }
        public string? HomeUrl { get; init; }
        public string? HomeLabel { get; init; }
        public string? SearchUrl { get; init; }
        public string? XrefMapUrl { get; init; }
        public string? CoverageUrl { get; init; }
        public string? RelatedContentUrl { get; init; }
        public string? NarrativeUrl { get; init; }
        public string? CurrentId { get; init; }
        public IReadOnlyList<WebApiDocsSuiteEntry> Entries { get; init; } = Array.Empty<WebApiDocsSuiteEntry>();
        public bool HasEntries => Entries.Count > 0;
    }

    private static ApiSuiteContext? BuildApiSuiteContext(WebApiDocsOptions options, string? currentBaseUrl = null)
    {
        if (options is null)
            return null;

        var entries = options.ApiSuiteEntries
            .Where(static entry => entry is not null && !string.IsNullOrWhiteSpace(entry.Href))
            .Select(static entry => new WebApiDocsSuiteEntry
            {
                Id = (entry.Id ?? string.Empty).Trim(),
                Label = string.IsNullOrWhiteSpace(entry.Label) ? (entry.Id ?? string.Empty).Trim() : entry.Label.Trim(),
                Href = entry.Href.Trim(),
                Summary = string.IsNullOrWhiteSpace(entry.Summary) ? null : entry.Summary.Trim(),
                Order = entry.Order
            })
            .Where(static entry => !string.IsNullOrWhiteSpace(entry.Label) && !string.IsNullOrWhiteSpace(entry.Href))
            .GroupBy(static entry => entry.Id.Length == 0 ? entry.Href : entry.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static entry => entry.Order ?? int.MaxValue)
            .ThenBy(static entry => entry.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (entries.Length <= 1)
            return null;

        var currentId = TrimOptionalString(options.ApiSuiteCurrentId);
        if (string.IsNullOrWhiteSpace(currentId) && !string.IsNullOrWhiteSpace(currentBaseUrl))
        {
            var normalizedCurrentUrl = EnsureTrailingSlash(currentBaseUrl);
            currentId = entries
                .FirstOrDefault(entry => string.Equals(EnsureTrailingSlash(entry.Href), normalizedCurrentUrl, StringComparison.OrdinalIgnoreCase))
                ?.Id;
        }

        return new ApiSuiteContext
        {
            Title = TrimOptionalString(options.ApiSuiteTitle),
            HomeUrl = TrimOptionalString(options.ApiSuiteHomeUrl),
            HomeLabel = TrimOptionalString(options.ApiSuiteHomeLabel) ?? "API suite home",
            SearchUrl = TrimOptionalString(options.ApiSuiteSearchUrl),
            XrefMapUrl = TrimOptionalString(options.ApiSuiteXrefMapUrl),
            CoverageUrl = TrimOptionalString(options.ApiSuiteCoverageUrl),
            RelatedContentUrl = TrimOptionalString(options.ApiSuiteRelatedContentUrl),
            NarrativeUrl = TrimOptionalString(options.ApiSuiteNarrativeUrl),
            CurrentId = currentId,
            Entries = entries
        };
    }

    private static object? BuildApiSuiteJson(WebApiDocsOptions options, string? currentBaseUrl = null)
    {
        var suite = BuildApiSuiteContext(options, currentBaseUrl);
        if (suite is null || !suite.HasEntries)
            return null;

        return new Dictionary<string, object?>
        {
            ["title"] = suite.Title,
            ["homeUrl"] = suite.HomeUrl,
            ["homeLabel"] = suite.HomeLabel,
            ["searchUrl"] = suite.SearchUrl,
            ["xrefMapUrl"] = suite.XrefMapUrl,
            ["coverageUrl"] = suite.CoverageUrl,
            ["relatedContentUrl"] = suite.RelatedContentUrl,
            ["narrativeUrl"] = suite.NarrativeUrl,
            ["currentId"] = suite.CurrentId,
            ["entries"] = suite.Entries.Select(static entry => new Dictionary<string, object?>
            {
                ["id"] = entry.Id,
                ["label"] = entry.Label,
                ["href"] = entry.Href,
                ["summary"] = entry.Summary,
                ["order"] = entry.Order
            }).ToList()
        };
    }

    private static void AppendApiSuiteSidebar(StringBuilder sb, ApiSuiteContext? suite, string currentBaseUrl)
    {
        if (sb is null)
            return;

        var html = new HtmlFragmentBuilder(initialIndent: 4);
        AppendApiSuiteSidebar(html, suite, currentBaseUrl);
        if (!html.IsEmpty)
            sb.AppendLine(html.ToString().TrimEnd());
    }

    private static void AppendApiSuiteSidebar(HtmlFragmentBuilder html, ApiSuiteContext? suite, string currentBaseUrl)
    {
        if (html is null || suite is null || !suite.HasEntries)
            return;

        var currentUrl = EnsureTrailingSlash(currentBaseUrl);
        var title = string.IsNullOrWhiteSpace(suite.Title) ? "API suite" : suite.Title;

        html.Line("<section class=\"api-suite-switcher\">");
        using (html.Indent())
        {
            html.Line($"<div class=\"api-suite-switcher-title\">{System.Web.HttpUtility.HtmlEncode(title)}</div>");
            if (!string.IsNullOrWhiteSpace(suite.HomeUrl))
            {
                html.Line($"<a class=\"api-suite-home\" href=\"{System.Web.HttpUtility.HtmlAttributeEncode(suite.HomeUrl)}\">{System.Web.HttpUtility.HtmlEncode(suite.HomeLabel ?? "API suite home")}</a>");
            }
            html.Line("<div class=\"api-suite-list\">");
            using (html.Indent())
            {
                foreach (var entry in suite.Entries)
                {
                    var href = EnsureTrailingSlash(entry.Href);
                    var isCurrent = (!string.IsNullOrWhiteSpace(suite.CurrentId) && string.Equals(entry.Id, suite.CurrentId, StringComparison.OrdinalIgnoreCase)) ||
                                    string.Equals(href, currentUrl, StringComparison.OrdinalIgnoreCase);
                    var active = isCurrent ? " active" : string.Empty;
                    html.Line($"<a class=\"api-suite-item{active}\" href=\"{System.Web.HttpUtility.HtmlAttributeEncode(entry.Href)}\">");
                    using (html.Indent())
                    {
                        html.Line($"<strong>{System.Web.HttpUtility.HtmlEncode(entry.Label)}</strong>");
                        if (!string.IsNullOrWhiteSpace(entry.Summary))
                            html.Line($"<span>{System.Web.HttpUtility.HtmlEncode(entry.Summary)}</span>");
                    }
                    html.Line("</a>");
                }
            }
            html.Line("</div>");
        }
        html.Line("</section>");
    }

    private static void AppendApiSuiteOverview(StringBuilder sb, ApiSuiteContext? suite, string currentBaseUrl, bool includeHeading = true)
    {
        if (sb is null)
            return;

        var html = new HtmlFragmentBuilder(initialIndent: 6);
        AppendApiSuiteOverview(html, suite, currentBaseUrl, includeHeading);
        if (!html.IsEmpty)
            sb.AppendLine(html.ToString().TrimEnd());
    }

    private static void AppendApiSuiteOverview(HtmlFragmentBuilder html, ApiSuiteContext? suite, string currentBaseUrl, bool includeHeading = true)
    {
        if (html is null || suite is null || !suite.HasEntries)
            return;

        var currentUrl = EnsureTrailingSlash(currentBaseUrl);
        var title = string.IsNullOrWhiteSpace(suite.Title) ? "API suite" : suite.Title;

        html.Line("<section class=\"api-suite-overview\">");
        using (html.Indent())
        {
            if (includeHeading)
            {
                html.Line($"<h2>{System.Web.HttpUtility.HtmlEncode(title)}</h2>");
                html.Line("<p class=\"section-desc\">Browse related project or module APIs from one shared suite.</p>");
            }

            html.Line("<div class=\"api-suite-grid\">");
            using (html.Indent())
            {
                foreach (var entry in suite.Entries)
                {
                    var href = EnsureTrailingSlash(entry.Href);
                    var isCurrent = (!string.IsNullOrWhiteSpace(suite.CurrentId) && string.Equals(entry.Id, suite.CurrentId, StringComparison.OrdinalIgnoreCase)) ||
                                    string.Equals(href, currentUrl, StringComparison.OrdinalIgnoreCase);
                    var active = isCurrent ? " active" : string.Empty;
                    html.Line($"<a class=\"api-suite-card{active}\" href=\"{System.Web.HttpUtility.HtmlAttributeEncode(entry.Href)}\">");
                    using (html.Indent())
                    {
                        html.Line($"<strong>{System.Web.HttpUtility.HtmlEncode(entry.Label)}</strong>");
                        if (!string.IsNullOrWhiteSpace(entry.Summary))
                            html.Line($"<span>{System.Web.HttpUtility.HtmlEncode(entry.Summary)}</span>");
                        if (isCurrent)
                            html.Line("<em>Current API</em>");
                    }
                    html.Line("</a>");
                }
            }
            html.Line("</div>");

            if (!string.IsNullOrWhiteSpace(suite.SearchUrl))
            {
                html.Line($"<div class=\"api-suite-search\" data-suite-search-url=\"{System.Web.HttpUtility.HtmlAttributeEncode(suite.SearchUrl)}\">");
                using (html.Indent())
                {
                    html.Line("<label class=\"filter-label\" for=\"api-suite-search-input\">Search across suite</label>");
                    html.Line("<input id=\"api-suite-search-input\" class=\"api-suite-search-input\" type=\"search\" placeholder=\"Search all APIs in this suite...\" autocomplete=\"off\" />");
                    html.Line("<div class=\"api-suite-search-hint\">Search across related project APIs without leaving this portal.</div>");
                    if (suite.Entries.Count > 1)
                    {
                        html.Line("<div class=\"api-suite-search-filters\" role=\"tablist\" aria-label=\"Filter suite search by API\">");
                        using (html.Indent())
                        {
                            html.Line("<button class=\"api-suite-search-filter active\" type=\"button\" data-suite-search-filter=\"\">All APIs</button>");
                            foreach (var entry in suite.Entries)
                            {
                                html.Line($"<button class=\"api-suite-search-filter\" type=\"button\" data-suite-search-filter=\"{System.Web.HttpUtility.HtmlAttributeEncode(entry.Id)}\">{System.Web.HttpUtility.HtmlEncode(entry.Label)}</button>");
                            }
                        }
                        html.Line("</div>");
                    }
                    html.Line("<div class=\"api-suite-search-status\" aria-live=\"polite\"></div>");
                    html.Line("<div class=\"api-suite-search-results\" hidden></div>");
                }
                html.Line("</div>");
            }
        }
        html.Line("</section>");
    }

    private static string? TrimOptionalString(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
