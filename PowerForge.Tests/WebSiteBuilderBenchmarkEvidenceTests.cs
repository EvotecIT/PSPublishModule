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
            WriteMergeCatalog(root);
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
    public void Build_WritesBenchmarkAreaFilesAndGivesTemplatesTheIndex()
    {
        var root = CreateTempRoot();
        try
        {
            WriteMergeCatalog(root);
            WritePage(root, "---\ntitle: Benchmarks\nslug: index\n---\n\nBenchmarks\n");
            WriteTheme(root, "<html><body>{{ for area in data.benchmarks.areas }}{{ area.file }}: {{ area.comparisons[0].headlines[0] }}{{ end }}</body></html>");

            var spec = BuildSpec();
            spec.BenchmarkEvidence = new[] { new BenchmarkEvidenceDataSpec { Name = "benchmarks", Catalog = "evidence/index.json", Output = "data/benchmarks", IndexOnly = true } };

            var html = BuildAndRead(root, spec);

            Assert.Contains("pdf.json: OfficeIMO is 2.0× faster than PDFsharp", html, StringComparison.Ordinal);
            var output = Path.Combine(root, "_site", "data", "benchmarks");
            Assert.Equal("pdf.json", Assert.Single(BenchmarkJson.Read<BenchmarkComparisonIndexView>(Path.Combine(output, "index.json")).Areas).File);
            Assert.Equal("merge", Assert.Single(BenchmarkJson.Read<BenchmarkAreaView>(Path.Combine(output, "pdf.json")).Comparisons).Id);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Build_DoesNotWriteBenchmarkFilesThroughALinkedOutputDirectory()
    {
        var root = CreateTempRoot();
        try
        {
            WriteMergeCatalog(root);
            var catalog = Path.Combine(root, "evidence", "index.json");
            var original = File.ReadAllText(catalog);
            WritePage(root, "---\ntitle: Benchmarks\nslug: index\n---\n\nBenchmarks\n");
            WriteTheme(root, "<html><body>{{ content }}</body></html>");
            Directory.CreateDirectory(Path.Combine(root, "_site", "data"));
            CreateDirectoryLink(Path.Combine(root, "_site", "data", "benchmarks"), Path.Combine(root, "evidence"));
            var spec = BuildSpec();
            spec.BenchmarkEvidence = new[] { new BenchmarkEvidenceDataSpec { Name = "benchmarks", Catalog = "evidence/index.json", Output = "data/benchmarks" } };

            var error = Assert.Throws<InvalidOperationException>(() => BuildAndRead(root, spec));

            Assert.Contains("symbolic link or reparse point", error.Message, StringComparison.Ordinal);
            Assert.Equal(original, File.ReadAllText(catalog));
        }
        finally
        {
            var link = Path.Combine(root, "_site", "data", "benchmarks");
            if (Directory.Exists(link))
                Directory.Delete(link);
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(null, true, "needs Output")]
    [InlineData("../outside", false, "inside the site output")]
    [InlineData("../_SITE/benchmarks", false, "inside the site output")]
    public void Build_RejectsBenchmarkOutputsThatCannotBeServed(string? output, bool indexOnly, string message)
    {
        var root = CreateTempRoot();
        try
        {
            WriteMergeCatalog(root);
            WritePage(root, "---\ntitle: Benchmarks\nslug: index\n---\n\nBenchmarks\n");
            WriteTheme(root, "<html><body>{{ content }}</body></html>");
            var spec = BuildSpec();
            spec.BenchmarkEvidence = new[] { new BenchmarkEvidenceDataSpec { Name = "benchmarks", Catalog = "evidence/index.json", Output = output, IndexOnly = indexOnly } };

            var error = Assert.Throws<InvalidOperationException>(() => BuildAndRead(root, spec));

            Assert.Contains(message, error.Message, StringComparison.Ordinal);
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

    [Theory]
    [InlineData("<pre class=\"sample\"><code class=\"sample language-powershell\">Get-Date</code></pre>")]
    [InlineData("<pre><code class='language-powershell'>Get-Date</code></pre>")]
    public void Build_DetectsLanguageClassesInAnyQuotingOrOrder(string code)
    {
        var root = CreateTempRoot();
        try
        {
            WritePage(root, "---\ntitle: Home\nslug: index\n---\n\nNo code in the content.\n");
            WriteTheme(root, "<html><head></head><body>{{ content }}" + code + "</body></html>");

            Assert.Contains("prism-autoloader", BuildAndRead(root, BuildSpec()), StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Build_KeepsPrismAssetsThatTheLayoutAlreadyLoads()
    {
        var root = CreateTempRoot();
        try
        {
            WritePage(root, "---\ntitle: Home\nslug: index\n---\n\nNo code in the content.\n");
            WriteTheme(root, "<html><head><link rel=\"stylesheet\" href=\"/css/prism-theme.css\"></head><body><pre><code class=\"language-powershell\">Get-Date</code></pre><script src=\"/vendor/prism.js\"></script></body></html>");

            var html = BuildAndRead(root, BuildSpec());

            Assert.DoesNotContain("prism-autoloader", html, StringComparison.Ordinal);
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "prism[^\"]*\\.css").Cast<System.Text.RegularExpressions.Match>());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Build_AddsOnlyPrismScriptsWhenTheLayoutHasAPrismTheme()
    {
        var root = CreateTempRoot();
        try
        {
            WritePage(root, "---\ntitle: Home\nslug: index\n---\n\nNo code in the content.\n");
            WriteTheme(root, "<html><head><link rel=\"stylesheet\" href=\"/css/prism-theme.css\"></head><body><pre><code class=\"language-powershell\">Get-Date</code></pre></body></html>");

            var html = BuildAndRead(root, BuildSpec());

            Assert.Contains("prism-autoloader", html, StringComparison.Ordinal);
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "<link[^>]*prism").Cast<System.Text.RegularExpressions.Match>());
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

    internal static SiteSpec BuildSpec() => new()
    {
        Name = "Benchmark evidence test",
        BaseUrl = "https://example.test",
        ContentRoot = "content",
        ThemesRoot = "themes",
        DefaultTheme = "t",
        ThemeEngine = "scriban",
        Collections = new[] { new CollectionSpec { Name = "pages", Input = "content/pages", Output = "/" } }
    };

    internal static string BuildAndRead(string root, SiteSpec spec)
    {
        var configPath = Path.Combine(root, "site.json");
        File.WriteAllText(configPath, "{}");
        var plan = WebSitePlanner.Plan(spec, configPath);
        var outPath = Path.Combine(root, "_site");
        WebSiteBuilder.Build(spec, plan, outPath);
        return File.ReadAllText(Path.Combine(outPath, "index.html"));
    }

    private static void WriteMergeCatalog(string root)
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
                new BenchmarkEvidenceEntry { ComparisonId = "merge", Area = "pdf", Platform = "windows", Profile = BenchmarkEvidenceProfile.Full, Publish = true, ResultPath = "/data/merge-windows-full.json", ArtifactRelativePath = "merge-windows-full.json" }
            }
        });
    }

    internal static void WritePage(string root, string markdown)
    {
        var pages = Path.Combine(root, "content", "pages");
        Directory.CreateDirectory(pages);
        File.WriteAllText(Path.Combine(pages, "index.md"), markdown);
    }

    internal static void WriteTheme(string root, string layout)
    {
        var themeRoot = Path.Combine(root, "themes", "t");
        Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
        File.WriteAllText(Path.Combine(themeRoot, "theme.manifest.json"),
            """
            { "schemaVersion": 2, "contractVersion": 2, "name": "t", "engine": "scriban", "layoutsPath": "layouts", "defaultLayout": "base" }
            """);
        File.WriteAllText(Path.Combine(themeRoot, "layouts", "base.html"), layout);
    }

    // Windows symbolic links need a privilege; a junction is the link type any user can create there.
    private static void CreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        process.WaitForExit();
        Assert.True(Directory.Exists(link), "The junction was not created.");
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-bench-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
