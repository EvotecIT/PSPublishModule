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
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(read, "EPPlus: not published \\(licence\\)\\."));
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
        // The override links the area's JSON relative to the area page; the index rebases it when loading the page.
        File.WriteAllText(Path.Combine(partials, "area.html"),
            "<section class=\"custom-area\"><h2>Area {{ $1.id }}: {{ $1.summaries.size }} comparisons</h2>" +
            "<a class=\"area-json\" href=\"../../../../data/benchmarks/{{ $1.file }}\">JSON</a><a class=\"area-top\" href=\"#top\">Top</a>" +
            "<a class=\"area-script\" href=\"javascript:void(0)\">Script</a>{{ $1.comparisonsHtml }}</section>");
        SiteSpec spec = WebSiteBuilderBenchmarkEvidenceTests.BuildSpec();
        spec.BenchmarkEvidence = [new BenchmarkEvidenceDataSpec { Name = "benchmarks", Catalog = "evidence/index.json", Output = "data/benchmarks", IndexOnly = true, AreaFragments = true }];

        string html = System.Net.WebUtility.HtmlDecode(WebSiteBuilderBenchmarkEvidenceTests.BuildAndRead(root, spec));

        string folder = WebSiteBuilder.BenchmarkFragmentFolder("benchmarks");
        Assert.Contains($"href=\"_powerforge/fragments/benchmarks/{folder}/csv.html\" data-pf-bench-site-relative>Open the csv results</a>", html, StringComparison.Ordinal);
        Assert.Contains("OfficeIMO is 1.40× faster than Sep", html, StringComparison.Ordinal);
        Assert.Contains("Not measured yet.", html, StringComparison.Ordinal);
        string fragments = Path.Combine(root, "_site", "_powerforge", "fragments", "benchmarks", folder);
        string area = System.Net.WebUtility.HtmlDecode(File.ReadAllText(Path.Combine(fragments, "csv.html")));
        // The area page stands on its own when the link is followed without the script.
        Assert.StartsWith("<!doctype html>", area, StringComparison.Ordinal);
        Assert.Contains("<meta charset=\"utf-8\" />", area, StringComparison.Ordinal);
        Assert.Contains("<title>csv benchmark results</title>", area, StringComparison.Ordinal);
        Assert.Contains("href=\"../../../../assets/powerforge/benchmarks.v1.css\"", area, StringComparison.Ordinal);
        Assert.Contains("<h2>Area csv: 1 comparisons</h2>", area, StringComparison.Ordinal);
        string json = System.Text.RegularExpressions.Regex.Match(area, "class=\"area-json\" href=\"(?<href>[^\"]+)\"").Groups["href"].Value;
        Assert.True(File.Exists(Path.GetFullPath(Path.Combine(fragments, json))), $"The area page links {json}, which does not exist.");
        Assert.Contains("id=\"benchmark-comparison-a\"", area, StringComparison.Ordinal);
        Assert.Contains("<p class=\"custom-run\">Windows</p>", area, StringComparison.Ordinal);
        Assert.Contains("<p class=\"custom-run\">Linux</p>", area, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(fragments, "pdf.html")));

        WebAuditResult audit = WebSiteAuditor.Audit(new WebAuditOptions { SiteRoot = Path.Combine(root, "_site"), CheckLinks = false, CheckAssets = false });
        Assert.Contains(audit.Issues, issue => (issue.Path ?? string.Empty).EndsWith("index.html", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(audit.Issues, issue => Path.GetFileName(issue.Path ?? string.Empty) is "csv.html" or "pdf.html");
    }

    [Fact]
    public void WebComponents_JsonOnlyPublicationRendersNoAreaFragments()
    {
        string root = CreateTempRoot();
        RecordComponentCatalog(root);
        WebSiteBuilderBenchmarkEvidenceTests.WritePage(root, "---\ntitle: Benchmarks\nslug: index\n---\n\nBenchmarks\n");
        WebSiteBuilderBenchmarkEvidenceTests.WriteTheme(root,
            "<html><head></head><body>{{ include \"powerforge/benchmarks/index\" data.benchmarks }}</body></html>");
        SiteSpec spec = WebSiteBuilderBenchmarkEvidenceTests.BuildSpec();
        spec.BenchmarkEvidence = [new BenchmarkEvidenceDataSpec { Name = "benchmarks", Catalog = "evidence/index.json", Output = "data/benchmarks", IndexOnly = true }];

        string html = WebSiteBuilderBenchmarkEvidenceTests.BuildAndRead(root, spec);

        Assert.True(File.Exists(Path.Combine(root, "_site", "data", "benchmarks", "csv.json")));
        Assert.False(Directory.Exists(Path.Combine(root, "_site", "_powerforge", "fragments", "benchmarks")));
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "_site", "data", "benchmarks"), "*.html"));
        Assert.DoesNotContain("data-pf-bench-area-src", html, StringComparison.Ordinal);
    }

    [Fact]
    public void WebComponents_AreaFragmentsRenderAreasLargerThanTheLoopLimit()
    {
        string root = CreateTempRoot();
        Directory.CreateDirectory(Path.Combine(root, "evidence"));
        BenchmarkJson.Write(Path.Combine(root, "evidence", "index.json"), new BenchmarkEvidenceCatalog
        {
            Areas = ["csv"],
            Comparisons = Enumerable.Range(0, 1001)
                .Select(index => new BenchmarkComparisonDefinition { ComparisonId = "c" + index, Area = "csv", Title = "Comparison " + index, Libraries = ["OfficeIMO", "Sep"] })
                .ToArray()
        });
        WebSiteBuilderBenchmarkEvidenceTests.WritePage(root, "---\ntitle: Benchmarks\nslug: index\n---\n\nBenchmarks\n");
        WebSiteBuilderBenchmarkEvidenceTests.WriteTheme(root, "<html><head></head><body>{{ content }}</body></html>");
        SiteSpec spec = WebSiteBuilderBenchmarkEvidenceTests.BuildSpec();
        spec.BenchmarkEvidence = [new BenchmarkEvidenceDataSpec { Name = "benchmarks", Catalog = "evidence/index.json", AreaFragments = true }];

        WebSiteBuilderBenchmarkEvidenceTests.BuildAndRead(root, spec);

        string area = File.ReadAllText(Path.Combine(root, "_site", "_powerforge", "fragments", "benchmarks", WebSiteBuilder.BenchmarkFragmentFolder("benchmarks"), "csv.html"));
        Assert.Equal(1001, System.Text.RegularExpressions.Regex.Matches(area, "<section class=\"pf-bench\"").Count);
    }

    [Fact]
    public void WebComponents_EntriesWhoseNamesLookAlikeKeepSeparateAreaPages()
    {
        string root = CreateTempRoot();
        RecordComponentCatalog(root);
        BenchmarkJson.Write(Path.Combine(root, "other", "index.json"), new BenchmarkEvidenceCatalog
        {
            Areas = ["csv"],
            Comparisons = [new BenchmarkComparisonDefinition { ComparisonId = "other-read", Area = "csv", Title = "Other catalog read", Libraries = ["OfficeIMO", "Sep"] }]
        });
        WebSiteBuilderBenchmarkEvidenceTests.WritePage(root, "---\ntitle: Home\nslug: index\n---\n\nHome\n");
        File.WriteAllText(Path.Combine(root, "content", "pages", "results.md"), "---\ntitle: Results\nslug: results\n---\n\nResults\n");
        WebSiteBuilderBenchmarkEvidenceTests.WriteTheme(root,
            "<html><head></head><body>{{ include \"powerforge/benchmarks/index\" data.benchmarks.csv }}{{ include \"powerforge/benchmarks/index\" data[\"benchmarks-csv\"] }}</body></html>");
        SiteSpec spec = WebSiteBuilderBenchmarkEvidenceTests.BuildSpec();
        spec.BenchmarkEvidence =
        [
            new BenchmarkEvidenceDataSpec { Name = "benchmarks.csv", Catalog = "evidence/index.json", AreaFragments = true },
            new BenchmarkEvidenceDataSpec { Name = "benchmarks-csv", Catalog = "other/index.json", AreaFragments = true }
        ];

        WebSiteBuilderBenchmarkEvidenceTests.BuildAndRead(root, spec);

        string first = WebSiteBuilder.BenchmarkFragmentFolder("benchmarks.csv");
        string second = WebSiteBuilder.BenchmarkFragmentFolder("benchmarks-csv");
        Assert.NotEqual(first, second);
        string fragments = Path.Combine(root, "_site", "_powerforge", "fragments", "benchmarks");
        string firstArea = File.ReadAllText(Path.Combine(fragments, first, "csv.html"));
        string secondArea = File.ReadAllText(Path.Combine(fragments, second, "csv.html"));
        Assert.Contains("CSV typed read", firstArea, StringComparison.Ordinal);
        Assert.DoesNotContain("Other catalog read", firstArea, StringComparison.Ordinal);
        Assert.Contains("Other catalog read", secondArea, StringComparison.Ordinal);
        // Links are made relative to each page, so a page below the site root reaches the same files.
        string results = File.ReadAllText(Path.Combine(root, "_site", "results", "index.html"));
        Assert.Contains($"href=\"../_powerforge/fragments/benchmarks/{first}/csv.html\"", results, StringComparison.Ordinal);
        Assert.Contains($"href=\"../_powerforge/fragments/benchmarks/{second}/csv.html\"", results, StringComparison.Ordinal);
    }

    [Fact]
    public void WebComponents_EntriesSharingAnAreaPageFolderFailBeforeWriting()
    {
        string root = CreateTempRoot();
        RecordComponentCatalog(root);
        WebSiteBuilderBenchmarkEvidenceTests.WritePage(root, "---\ntitle: Home\nslug: index\n---\n\nHome\n");
        WebSiteBuilderBenchmarkEvidenceTests.WriteTheme(root, "<html><head></head><body>{{ content }}</body></html>");
        SiteSpec spec = WebSiteBuilderBenchmarkEvidenceTests.BuildSpec();
        spec.BenchmarkEvidence =
        [
            new BenchmarkEvidenceDataSpec { Name = "benchmarks", Catalog = "evidence/index.json", AreaFragments = true },
            new BenchmarkEvidenceDataSpec { Name = "benchmarks", Catalog = "evidence/index.json", AreaFragments = true }
        ];

        var error = Assert.Throws<InvalidOperationException>(() => WebSiteBuilderBenchmarkEvidenceTests.BuildAndRead(root, spec));

        Assert.Contains("same folder", error.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(root, "_site", "_powerforge", "fragments", "benchmarks")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WebComponents_IndexRendersWithoutAreas(bool indexOnly)
    {
        string root = CreateTempRoot();
        Directory.CreateDirectory(Path.Combine(root, "evidence"));
        BenchmarkJson.Write(Path.Combine(root, "evidence", "index.json"), new BenchmarkEvidenceCatalog());
        WebSiteBuilderBenchmarkEvidenceTests.WritePage(root, "---\ntitle: Benchmarks\nslug: index\n---\n\nBenchmarks\n");
        WebSiteBuilderBenchmarkEvidenceTests.WriteTheme(root,
            "<html><head></head><body>{{ include \"powerforge/benchmarks/index\" data.benchmarks }}</body></html>");
        SiteSpec spec = WebSiteBuilderBenchmarkEvidenceTests.BuildSpec();
        spec.BenchmarkEvidence = [new BenchmarkEvidenceDataSpec { Name = "benchmarks", Catalog = "evidence/index.json", Output = "data/benchmarks", IndexOnly = indexOnly }];

        string html = WebSiteBuilderBenchmarkEvidenceTests.BuildAndRead(root, spec);

        Assert.Contains("<div class=\"pf-bench-index\" data-pf-bench-index>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("pf-bench-index__area", html, StringComparison.Ordinal);
        Assert.Contains("No benchmark results yet.", html, StringComparison.Ordinal);
    }

    [Fact]
    public void WebComponents_IndexTellsPublishedRunsWithoutHeadlinesFromUnpublishedOnes()
    {
        string root = CreateTempRoot();
        string catalogPath = Path.Combine(root, "evidence", "index.json");
        var service = new BenchmarkEvidenceCatalogService();
        BenchmarkEvidenceUpdateRequest partial = PublishRequest(MeasuredResult(("OfficeIMO", "Read", Spread(10, 10)), ("Sep", "Read", null)), TestDeclarations());
        partial.ResultArtifactPath = Path.Combine(root, "evidence", "windows.json");
        service.UpdateFile(catalogPath, partial);
        BenchmarkEvidenceUpdateRequest diagnostic = PublishRequest(MeasuredResult(("OfficeIMO", "Read", Spread(10, 10)), ("Sep", "Read", Spread(20, 10))), TestDeclarations());
        diagnostic.ComparisonId = "comparison-b";
        diagnostic.Profile = BenchmarkEvidenceProfile.Diagnostic;
        diagnostic.Publish = false;
        diagnostic.ResultPath = "windows-diagnostic.json";
        diagnostic.ResultArtifactPath = Path.Combine(root, "evidence", "windows-diagnostic.json");
        service.UpdateFile(catalogPath, diagnostic);
        WebSiteBuilderBenchmarkEvidenceTests.WritePage(root, "---\ntitle: Benchmarks\nslug: index\n---\n\nBenchmarks\n");
        WebSiteBuilderBenchmarkEvidenceTests.WriteTheme(root,
            "<html><head></head><body>{{ for s in data.benchmarks.areas[0].comparisons }}[{{ s.id }}]{{ end }}{{ include \"powerforge/benchmarks/index\" data.benchmarks }}</body></html>");
        SiteSpec spec = WebSiteBuilderBenchmarkEvidenceTests.BuildSpec();
        spec.BenchmarkEvidence = [new BenchmarkEvidenceDataSpec { Name = "benchmarks", Catalog = "evidence/index.json", Output = "data/benchmarks", IndexOnly = true }];

        string html = WebSiteBuilderBenchmarkEvidenceTests.BuildAndRead(root, spec);

        BenchmarkComparisonIndexView index = BenchmarkJson.Read<BenchmarkComparisonIndexView>(Path.Combine(root, "_site", "data", "benchmarks", "index.json"));
        BenchmarkComparisonSummaryView published = Assert.Single(index.Areas[0].Comparisons, value => value.Id == "comparison-a");
        Assert.True(published.Published);
        Assert.Empty(published.Headlines);
        Assert.False(Assert.Single(index.Areas[0].Comparisons, value => value.Id == "comparison-b").Published);
        Assert.Contains("No head-to-head result in the published run.", html, StringComparison.Ordinal);
        Assert.Contains("No published run yet.", html, StringComparison.Ordinal);
    }

    [Fact]
    public void WebComponents_StandaloneRunNamesLicenceRestrictedLibrariesWithoutTheirTimes()
    {
        string root = CreateTempRoot();
        RecordComponentCatalog(root);
        WebSiteBuilderBenchmarkEvidenceTests.WritePage(root, "---\ntitle: Benchmarks\nslug: index\n---\n\nBenchmarks\n");
        WebSiteBuilderBenchmarkEvidenceTests.WriteTheme(root,
            "<html><head></head><body>{{ c = data.benchmarks.comparisons[0] }}{{ include \"powerforge/benchmarks/run\" c.runs[0] c }}</body></html>");
        SiteSpec spec = WebSiteBuilderBenchmarkEvidenceTests.BuildSpec();
        spec.BenchmarkEvidence = [new BenchmarkEvidenceDataSpec { Name = "benchmarks", Catalog = "evidence/index.json" }];

        string html = System.Net.WebUtility.HtmlDecode(WebSiteBuilderBenchmarkEvidenceTests.BuildAndRead(root, spec));

        Assert.Contains("<li>EPPlus: not published (licence).</li>", html, StringComparison.Ordinal);
        Assert.Contains("<li>ClosedXML: not published. Cell failed", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<th scope=\"col\">EPPlus", html, StringComparison.Ordinal);
        Assert.DoesNotContain("5.45 ms", html, StringComparison.Ordinal);
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
        // Write is measured only by OfficeIMO and Sep, so the valid cells are listed explicitly.
        declarations.Comparisons[0].Cases = ["Read", "Write"];
        declarations.Comparisons[0].Cells =
        [
            new BenchmarkExpectedCell { Case = "Read" },
            new BenchmarkExpectedCell { Case = "Write", Library = "OfficeIMO" },
            new BenchmarkExpectedCell { Case = "Write", Library = "Sep" }
        ];
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
