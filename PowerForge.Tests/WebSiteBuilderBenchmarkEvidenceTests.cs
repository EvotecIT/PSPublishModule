using PowerForge.Web;

namespace PowerForge.Tests;

public class WebSiteBuilderBenchmarkEvidenceTests
{
    [Fact]
    public void Build_ExposesBenchmarkEvidenceAsComparisonViewData()
    {
        var root = CreateTempRoot();
        try
        {
            var evidence = Path.Combine(root, "evidence");
            Directory.CreateDirectory(evidence);
            BenchmarkJson.Write(Path.Combine(evidence, "merge-windows-full.json"), new BenchmarkRunResult
            {
                Suite = "merge",
                Summary = new[]
                {
                    new BenchmarkSummaryRow { Scenario = "OfficeIMO", Status = "Succeeded", MedianMs = 1 },
                    new BenchmarkSummaryRow { Scenario = "PdfSharp", Status = "Succeeded", MedianMs = 2 }
                }
            });
            BenchmarkJson.Write(Path.Combine(evidence, "index.json"), new BenchmarkEvidenceCatalog
            {
                Areas = new[] { "pdf" },
                Comparisons = new[] { new BenchmarkComparisonDefinition { ComparisonId = "merge", Area = "pdf", Title = "PDF merging", Libraries = new[] { "OfficeIMO", "PDFsharp" } } },
                Entries = new[]
                {
                    new BenchmarkEvidenceEntry { ComparisonId = "merge", Area = "pdf", Platform = "windows", RunMode = "full", Publish = true, ResultPath = "/data/merge-windows-full.json", ArtifactRelativePath = "merge-windows-full.json" }
                }
            });
            WritePage(root, "---\ntitle: Benchmarks\nslug: index\n---\n\nBenchmarks\n");
            WriteTheme(root, "<html><body>{{ for comparison in data.benchmarks.comparisons }}{{ comparison.title }}: {{ comparison.runs[0].headToHead[0].text }}{{ end }}</body></html>");

            var spec = BuildSpec();
            spec.BenchmarkEvidence = new[] { new BenchmarkEvidenceDataSpec { Name = "benchmarks", Catalog = "evidence/index.json" } };

            var html = BuildAndRead(root, spec);

            Assert.Contains("PDF merging: OfficeIMO is 2.0× faster than PDFsharp", html, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Build_InjectsPrismWhenOnlyTheLayoutRendersHighlightedCode()
    {
        var root = CreateTempRoot();
        try
        {
            WritePage(root, "---\ntitle: Home\nslug: index\n---\n\nNo code in the content.\n");
            WriteTheme(root, "<html><head></head><body>{{ content }}<pre><code class=\"language-powershell\">Get-Date</code></pre></body></html>");

            var html = BuildAndRead(root, BuildSpec());

            Assert.Contains("prism-autoloader", html, StringComparison.Ordinal);
            Assert.True(html.IndexOf("prism-autoloader", StringComparison.Ordinal) < html.IndexOf("</body>", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Render_UsesTheLoopLimitFromTheSiteSpec()
    {
        const string template = "{{ for i in 1..1500 }}x{{ end }}";
        Assert.Throws<Scriban.Syntax.ScriptRuntimeException>(() =>
            new ScribanTemplateEngine().Render(template, new ThemeRenderContext(), _ => null));

        var context = new ThemeRenderContext { Site = new SiteSpec { Templates = new TemplateLimitsSpec { LoopLimit = 0 } } };
        Assert.Equal(1500, new ScribanTemplateEngine().Render(template, context, _ => null).Length);
    }

    private static SiteSpec BuildSpec() => new()
    {
        Name = "Benchmark evidence test",
        BaseUrl = "https://example.test",
        ContentRoot = "content",
        ThemesRoot = "themes",
        DefaultTheme = "t",
        ThemeEngine = "scriban",
        Collections = new[] { new CollectionSpec { Name = "pages", Input = "content/pages", Output = "/" } }
    };

    private static string BuildAndRead(string root, SiteSpec spec)
    {
        var configPath = Path.Combine(root, "site.json");
        File.WriteAllText(configPath, "{}");
        var plan = WebSitePlanner.Plan(spec, configPath);
        var outPath = Path.Combine(root, "_site");
        WebSiteBuilder.Build(spec, plan, outPath);
        return File.ReadAllText(Path.Combine(outPath, "index.html"));
    }

    private static void WritePage(string root, string markdown)
    {
        var pages = Path.Combine(root, "content", "pages");
        Directory.CreateDirectory(pages);
        File.WriteAllText(Path.Combine(pages, "index.md"), markdown);
    }

    private static void WriteTheme(string root, string layout)
    {
        var themeRoot = Path.Combine(root, "themes", "t");
        Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
        File.WriteAllText(Path.Combine(themeRoot, "theme.manifest.json"),
            """
            { "schemaVersion": 2, "contractVersion": 2, "name": "t", "engine": "scriban", "layoutsPath": "layouts", "defaultLayout": "base" }
            """);
        File.WriteAllText(Path.Combine(themeRoot, "layouts", "base.html"), layout);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-bench-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
