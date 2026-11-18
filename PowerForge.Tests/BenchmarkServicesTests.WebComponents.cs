using PowerForge.Web;

namespace PowerForge.Tests;

// The built-in website components render the comparison view a theme gets from BenchmarkEvidence.
public sealed partial class BenchmarkServicesTests
{
    [Fact]
    public void WebComponents_RenderPublishedRunsWithIntervalsNotesAndStates()
    {
        string root = CreateTempRoot();
        RecordComponentCatalog(root);
        WebSiteBuilderBenchmarkEvidenceTests.WritePage(root, "---\ntitle: Benchmarks\nslug: index\n---\n\nBenchmarks\n");
        WebSiteBuilderBenchmarkEvidenceTests.WriteTheme(root,
            "<html><head></head><body>{{ for c in data.benchmarks.comparisons }}{{ include \"powerforge/benchmarks/comparison\" c }}{{ end }}</body></html>");
        SiteSpec spec = WebSiteBuilderBenchmarkEvidenceTests.BuildSpec();
        spec.BenchmarkEvidence = [new BenchmarkEvidenceDataSpec { Name = "benchmarks", Catalog = "evidence/index.json" }];

        string html = System.Net.WebUtility.HtmlDecode(WebSiteBuilderBenchmarkEvidenceTests.BuildAndRead(root, spec));

        string read = Section(html, "benchmark-comparison-a");
        Assert.Contains("data-pf-bench-tab=\"windows-net10.0-standard\"", read, StringComparison.Ordinal);
        Assert.Contains("data-pf-bench-tab=\"linux-net10.0-standard\"", read, StringComparison.Ordinal);
        Assert.Contains("data-pf-bench-default=\"windows-net10.0-standard\"", read, StringComparison.Ordinal);
        Assert.Contains("<code>Rows=100000</code>", read, StringComparison.Ordinal);
        Assert.Contains("95% CI ", read, StringComparison.Ordinal);
        Assert.Contains("is-even", read, StringComparison.Ordinal);
        Assert.Contains("<span class=\"pf-bench__relative\">even</span>", read, StringComparison.Ordinal);
        Assert.Contains("<span class=\"pf-bench__relative\">fastest</span>", read, StringComparison.Ordinal);
        Assert.Contains("EPPlus: not published (licence).", read, StringComparison.Ordinal);
        Assert.Contains("ClosedXML: not published. Cell failed", read, StringComparison.Ordinal);
        Assert.Contains("macOS · net10.0 · not measured", read, StringComparison.Ordinal);
        Assert.DoesNotContain("windows-net10.0-diagnostic", read, StringComparison.Ordinal);
        Assert.DoesNotContain("<th scope=\"col\">EPPlus", read, StringComparison.Ordinal);

        string single = Section(html, "benchmark-single-library");
        Assert.Contains("No published run yet.", single, StringComparison.Ordinal);
        Assert.Contains("Single-library comparison", single, StringComparison.Ordinal);

        Assert.Contains("assets/powerforge/benchmarks.v1.css", html, StringComparison.Ordinal);
        Assert.Contains("assets/powerforge/benchmarks.v1.js", html, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "_site", "assets", "powerforge", "benchmarks.v1.js")));
    }

    [Fact]
    public void WebComponents_IndexLoadsRenderedAreaFilesAndThemesCanReplaceAComponent()
    {
        string root = CreateTempRoot();
        RecordComponentCatalog(root);
        WebSiteBuilderBenchmarkEvidenceTests.WritePage(root, "---\ntitle: Benchmarks\nslug: index\n---\n\nBenchmarks\n");
        WebSiteBuilderBenchmarkEvidenceTests.WriteTheme(root,
            "<html><head></head><body>{{ include \"powerforge/benchmarks/index\" data.benchmarks }}</body></html>");
        string partials = Path.Combine(root, "themes", "t", "partials", "powerforge", "benchmarks");
        Directory.CreateDirectory(partials);
        File.WriteAllText(Path.Combine(partials, "run.html"), "<p class=\"custom-run\">{{ $1.platformLabel }}</p>");
        SiteSpec spec = WebSiteBuilderBenchmarkEvidenceTests.BuildSpec();
        spec.BenchmarkEvidence = [new BenchmarkEvidenceDataSpec { Name = "benchmarks", Catalog = "evidence/index.json", Output = "data/benchmarks", IndexOnly = true }];

        string html = System.Net.WebUtility.HtmlDecode(WebSiteBuilderBenchmarkEvidenceTests.BuildAndRead(root, spec));

        Assert.Contains("data-pf-bench-area-src=\"data/benchmarks/csv.html\"", html, StringComparison.Ordinal);
        Assert.Contains("OfficeIMO is 1.40× faster than Sep", html, StringComparison.Ordinal);
        Assert.Contains("Not measured yet.", html, StringComparison.Ordinal);
        string area = System.Net.WebUtility.HtmlDecode(File.ReadAllText(Path.Combine(root, "_site", "data", "benchmarks", "csv.html")));
        Assert.Contains("id=\"benchmark-comparison-a\"", area, StringComparison.Ordinal);
        Assert.Contains("<p class=\"custom-run\">Windows</p>", area, StringComparison.Ordinal);
        Assert.Contains("<p class=\"custom-run\">Linux</p>", area, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "_site", "data", "benchmarks", "pdf.html")));
    }

    private static string Section(string html, string id)
    {
        int start = html.IndexOf("id=\"" + id + "\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Section {id} was not rendered.");
        int end = html.IndexOf("</section>", start, StringComparison.Ordinal);
        return html.Substring(start, end - start);
    }

    /// <summary>
    /// Published Windows and Linux lanes and an unpublished diagnostic lane for one comparison; Sep is within
    /// OfficeIMO's interval in one case, EPPlus is licence-restricted and ClosedXML failed. The PDF comparison is
    /// declared but never measured.
    /// </summary>
    private static void RecordComponentCatalog(string root)
    {
        string catalogPath = Path.Combine(root, "evidence", "index.json");
        BenchmarkDeclarationDocument declarations = ViewDeclarations();
        declarations.Libraries = [.. declarations.Libraries, new BenchmarkLibraryDeclaration { Id = "ClosedXML", Package = "ClosedXML", Version = "0.105.0", License = "MIT", Restriction = BenchmarkLibraryRestriction.Open }];
        declarations.Comparisons[0].Libraries = ["OfficeIMO", "Sep", "EPPlus", "ClosedXML"];
        var service = new BenchmarkEvidenceCatalogService();
        foreach (string platform in new[] { "windows", "linux" })
        {
            BenchmarkEvidenceUpdateRequest request = PublishRequest(ComponentResult(platform), declarations);
            request.ResultPath = platform + ".json";
            request.ResultArtifactPath = Path.Combine(root, "evidence", platform + ".json");
            service.UpdateFile(catalogPath, request);
        }
        BenchmarkEvidenceUpdateRequest diagnostic = PublishRequest(ComponentResult("windows"), declarations);
        diagnostic.Profile = BenchmarkEvidenceProfile.Diagnostic;
        diagnostic.Publish = false;
        diagnostic.ResultPath = "windows-diagnostic.json";
        diagnostic.ResultArtifactPath = Path.Combine(root, "evidence", "windows-diagnostic.json");
        service.UpdateFile(catalogPath, diagnostic);
    }

    private static BenchmarkRunResult ComponentResult(string platform)
    {
        BenchmarkRunResult result = MeasuredResult(
            ("OfficeIMO", "Read", Spread(10, 10)), ("Sep", "Read", Spread(20, 10)), ("EPPlus", "Read", Spread(5, 10)), ("ClosedXML", "Read", null),
            ("OfficeIMO", "Write", Spread(10, 10)), ("Sep", "Write", Spread(10.3, 10)));
        string os = platform == "linux" ? "Linux" : "Windows";
        foreach (BenchmarkSample sample in result.Samples)
        {
            sample.Os = os;
            sample.Variables["Rows"] = "100000";
        }
        result.Summary = new BenchmarkSummaryService().Summarize(result.Samples);
        result.Environment!.OsFamily = os;
        result.Environment.OsDescription = platform == "linux" ? "Ubuntu 24.04" : "Windows 11 24H2";
        result.Metadata["benchmark.package.closedxml"] = "0.105.0";
        result.Metadata["benchmark.package.closedxml.sha256"] = "cc33";
        return result;
    }
}
