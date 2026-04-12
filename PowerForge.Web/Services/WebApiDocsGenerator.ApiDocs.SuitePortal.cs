using System.Text;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    /// <summary>Generates a standalone landing/search portal for a multi-project API suite.</summary>
    public static WebApiDocsSuitePortalResult GenerateSuitePortal(WebApiDocsOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.OutputPath))
            throw new ArgumentException("OutputPath is required.", nameof(options));

        var outputPath = Path.GetFullPath(options.OutputPath);
        Directory.CreateDirectory(outputPath);

        var suite = BuildApiSuiteContext(options, options.BaseUrl);
        if (suite is null || !suite.HasEntries)
            throw new InvalidOperationException("GenerateSuitePortal requires at least two suite entries.");

        var warnings = new List<string>();
        var head = GetApiDocsResolvedHeadHtml(options);
        var header = LoadOptionalHtml(options.HeaderHtmlPath);
        var footer = LoadOptionalHtml(options.FooterHtmlPath);
        ApplyNavFallback(options, warnings, ref header, ref footer);
        ApplyNavTokens(options, warnings, ref header, ref footer);
        var bodyClass = ResolveBodyClass(options.BodyClass);
        var criticalCss = ResolveCriticalCss(options, warnings);
        var cssLinks = BuildCssLinks(options.CssHref);
        var fallbackCss = LoadAsset(options, "fallback.css", null);
        var cssBlock = BuildCssBlockWithFallback(fallbackCss, cssLinks);
        var docsScript = WrapScript(LoadAsset(options, "docs.js", options.DocsScriptPath));
        var social = ResolveApiSocialProfile(options);
        var baseUrl = NormalizeApiRoute(options.BaseUrl);
        var title = string.IsNullOrWhiteSpace(options.Title) ? (suite.Title ?? "API Suite") : options.Title.Trim();

        var template = LoadTemplate(options, "suite-portal.html", null);
        var html = ApplyTemplate(template, new Dictionary<string, string?>
        {
            ["TITLE"] = System.Web.HttpUtility.HtmlEncode(title),
            ["DESCRIPTION_META"] = BuildDescriptionMetaTag($"Search and browse the {title} API suite."),
            ["OPEN_GRAPH_META"] = BuildApiOpenGraphMetaTags(options, social, title, $"Search and browse the {title} API suite.", baseUrl),
            ["HEAD_HTML"] = head,
            ["CRITICAL_CSS"] = criticalCss,
            ["CSS"] = cssBlock,
            ["HEADER"] = header,
            ["FOOTER"] = footer,
            ["BODY_CLASS"] = bodyClass,
            ["MAIN"] = BuildSuitePortalMain(title, suite, baseUrl),
            ["DOCS_SCRIPT"] = docsScript
        });

        var indexPath = Path.Combine(outputPath, "index.html");
        File.WriteAllText(indexPath, html, Encoding.UTF8);

        var jsonPath = Path.Combine(outputPath, "index.json");
        WriteJson(jsonPath, new Dictionary<string, object?>
        {
            ["generatedAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["kind"] = "api-suite-portal",
            ["title"] = title,
            ["baseUrl"] = baseUrl,
            ["suite"] = BuildApiSuiteJson(options, baseUrl)
        });

        return new WebApiDocsSuitePortalResult
        {
            OutputPath = outputPath,
            IndexPath = indexPath,
            JsonPath = jsonPath,
            EntryCount = suite.Entries.Count,
            Warnings = warnings.ToArray()
        };
    }

    private static string BuildSuitePortalMain(string title, ApiSuiteContext suite, string baseUrl)
    {
        var html = new HtmlFragmentBuilder();
        html.Line("<div class=\"api-suite-portal ev-page-body\">");
        using (html.Indent())
        {
            html.Line("<header class=\"ev-docs-header api-overview-header\">");
            using (html.Indent())
            {
                html.Line("<p class=\"ev-eyebrow\">API Suite</p>");
                html.Line($"<h1>{System.Web.HttpUtility.HtmlEncode(title)}</h1>");
                html.Line("<p class=\"lead\">Browse every API in this suite from one landing page, then search across all related symbols.</p>");
            }
            html.Line("</header>");
            html.Line("<div class=\"api-overview-main api-overview-main--full\">");
            using (html.Indent())
            {
                AppendApiSuiteNarrativeSummary(html, suite);
                AppendApiSuiteCoverageSummary(html, suite);
                AppendApiSuiteRelatedContentSummary(html, suite);
                AppendApiSuiteOverview(html, suite, EnsureTrailingSlash(baseUrl), includeHeading: false);
                AppendApiSuiteArtifactLinks(html, suite);
            }
            html.Line("</div>");
        }
        html.Line("</div>");
        return html.ToString().TrimEnd();
    }

    private static void AppendApiSuiteNarrativeSummary(HtmlFragmentBuilder html, ApiSuiteContext suite)
    {
        if (html is null || suite is null || string.IsNullOrWhiteSpace(suite.NarrativeUrl))
            return;

        html.Line($"<section class=\"api-suite-narrative\" data-suite-narrative-url=\"{System.Web.HttpUtility.HtmlAttributeEncode(suite.NarrativeUrl)}\">");
        using (html.Indent())
        {
            html.Line("<h2>Start Here</h2>");
            html.Line("<p class=\"section-desc\">Follow the curated learning path for this API ecosystem before diving into individual reference pages.</p>");
            html.Line("<div class=\"api-suite-narrative-status\" aria-live=\"polite\">Loading suite guidance...</div>");
            html.Line("<div class=\"api-suite-narrative-summary\" hidden></div>");
            html.Line("<div class=\"api-suite-narrative-sections\" hidden></div>");
        }
        html.Line("</section>");
    }

    private static void AppendApiSuiteCoverageSummary(HtmlFragmentBuilder html, ApiSuiteContext suite)
    {
        if (html is null || suite is null || string.IsNullOrWhiteSpace(suite.CoverageUrl))
            return;

        html.Line($"<section class=\"api-suite-coverage-summary\" data-suite-coverage-url=\"{System.Web.HttpUtility.HtmlAttributeEncode(suite.CoverageUrl)}\">");
        using (html.Indent())
        {
            html.Line("<h2>Coverage Signals</h2>");
            html.Line("<p class=\"section-desc\">Track whether the important entry points across this suite are well documented and supported by guidance.</p>");
            html.Line("<div class=\"api-suite-coverage-status\" aria-live=\"polite\">Loading suite coverage summary...</div>");
            html.Line("<div class=\"api-suite-coverage-grid\" hidden></div>");
        }
        html.Line("</section>");
    }

    private static void AppendApiSuiteRelatedContentSummary(HtmlFragmentBuilder html, ApiSuiteContext suite)
    {
        if (html is null || suite is null || string.IsNullOrWhiteSpace(suite.RelatedContentUrl))
            return;

        html.Line($"<section class=\"api-suite-related-content\" data-suite-related-content-url=\"{System.Web.HttpUtility.HtmlAttributeEncode(suite.RelatedContentUrl)}\">");
        using (html.Indent())
        {
            html.Line("<h2>Guides & Samples</h2>");
            html.Line("<p class=\"section-desc\">Curated walkthroughs and examples collected across every API in this suite.</p>");
            html.Line("<div class=\"api-suite-related-content-status\" aria-live=\"polite\">Loading curated guides...</div>");
            html.Line("<div class=\"api-suite-related-content-list\" hidden></div>");
        }
        html.Line("</section>");
    }

    private static void AppendApiSuiteArtifactLinks(HtmlFragmentBuilder html, ApiSuiteContext suite)
    {
        if (html is null || suite is null)
            return;

        var links = new List<(string Label, string Url)>
        {
            ("Suite narrative", suite.NarrativeUrl ?? string.Empty),
            ("Suite coverage report", suite.CoverageUrl ?? string.Empty),
            ("Suite xref map", suite.XrefMapUrl ?? string.Empty),
            ("Suite search index", suite.SearchUrl ?? string.Empty)
        }
        .Where(static item => !string.IsNullOrWhiteSpace(item.Url))
        .ToArray();

        if (links.Length == 0)
            return;

        html.Line("<section class=\"api-suite-artifacts\">");
        using (html.Indent())
        {
            html.Line("<h2>Suite Assets</h2>");
            html.Line("<p class=\"section-desc\">Shared machine-readable outputs for search, xref, and coverage consumers.</p>");
            html.Line("<div class=\"api-suite-artifact-list\">");
            using (html.Indent())
            {
                foreach (var link in links)
                {
                    html.Line($"<a class=\"api-suite-artifact\" href=\"{System.Web.HttpUtility.HtmlAttributeEncode(link.Url)}\">{System.Web.HttpUtility.HtmlEncode(link.Label)}</a>");
                }
            }
            html.Line("</div>");
        }
        html.Line("</section>");
    }
}
