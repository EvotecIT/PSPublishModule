using PowerForge.Web;
using ImageMagick;
using System.Xml.Linq;

namespace PowerForge.Tests;

public partial class WebSiteAuditOptimizeBuildTests
{
    [Fact]
    public void Build_ResolvesLocalizationRoutesAndRuntime()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-localization-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var docsEnPath = Path.Combine(root, "content", "docs", "en");
            var docsPlPath = Path.Combine(root, "content", "docs", "pl");
            Directory.CreateDirectory(docsEnPath);
            Directory.CreateDirectory(docsPlPath);
            File.WriteAllText(Path.Combine(docsEnPath, "index.md"),
                """
                ---
                title: Documentation
                ---

                # Docs
                """);
            File.WriteAllText(Path.Combine(docsPlPath, "index.md"),
                """
                ---
                title: Dokumentacja
                ---

                # Dokumentacja
                """);

            var themeRoot = Path.Combine(root, "themes", "localization-test");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "docs.html"),
                """
                <!doctype html>
                <html>
                <body>
                  <div id="current">{{ current_language.code }}</div>
                  {{ for lang in languages }}<a class="lang" data-code="{{ lang.code }}" href="{{ lang.url }}">{{ lang.label }}</a>{{ end }}
                  {{ content }}
                </body>
                </html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                """
                {
                  "name": "localization-test",
                  "engine": "scriban",
                  "defaultLayout": "docs"
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Localization Runtime Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "localization-test",
                ThemesRoot = "themes",
                TrailingSlash = TrailingSlashMode.Always,
                Localization = new LocalizationSpec
                {
                    Enabled = true,
                    DefaultLanguage = "en",
                    PrefixDefaultLanguage = false,
                    DetectFromPath = true,
                    Languages = new[]
                    {
                        new LanguageSpec { Code = "en", Label = "English", Default = true },
                        new LanguageSpec { Code = "pl", Label = "Polski" }
                    }
                },
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "docs",
                        Input = "content/docs",
                        Output = "/docs",
                        DefaultLayout = "docs"
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var outPath = Path.Combine(root, "_site");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteBuilder.Build(spec, plan, outPath);

            Assert.True(File.Exists(Path.Combine(result.OutputPath, "docs", "index.html")));
            Assert.True(File.Exists(Path.Combine(result.OutputPath, "pl", "docs", "index.html")));

            var polishOutput = File.ReadAllText(Path.Combine(result.OutputPath, "pl", "docs", "index.html"));
            Assert.Contains("<div id=\"current\">pl</div>", polishOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("data-code=\"en\" href=\"/docs/\"", polishOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("data-code=\"pl\" href=\"/pl/docs/\"", polishOutput, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_LocalizationSwitcher_DoesNotCrossProjectForSameTranslationKey()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-localization-project-scope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var projectARoot = Path.Combine(root, "projects", "ProjectA");
            var projectBRoot = Path.Combine(root, "projects", "ProjectB");
            var aEn = Path.Combine(projectARoot, "content", "docs", "en");
            var aPl = Path.Combine(projectARoot, "content", "docs", "pl");
            var bEn = Path.Combine(projectBRoot, "content", "docs", "en");
            var bPl = Path.Combine(projectBRoot, "content", "docs", "pl");
            Directory.CreateDirectory(aEn);
            Directory.CreateDirectory(aPl);
            Directory.CreateDirectory(bEn);
            Directory.CreateDirectory(bPl);

            File.WriteAllText(Path.Combine(projectARoot, "project.json"), """{ "name": "ProjectA", "slug": "a" }""");
            File.WriteAllText(Path.Combine(projectBRoot, "project.json"), """{ "name": "ProjectB", "slug": "b" }""");

            File.WriteAllText(Path.Combine(aEn, "index.md"), "---\ntitle: A EN\n---\n\nA EN");
            File.WriteAllText(Path.Combine(aPl, "index.md"), "---\ntitle: A PL\n---\n\nA PL");
            File.WriteAllText(Path.Combine(bEn, "index.md"), "---\ntitle: B EN\n---\n\nB EN");
            File.WriteAllText(Path.Combine(bPl, "index.md"), "---\ntitle: B PL\n---\n\nB PL");

            var themeRoot = Path.Combine(root, "themes", "localization-project-test");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "docs.html"),
                """
                <!doctype html>
                <html>
                <body>
                  {{ for lang in languages }}<a class="lang" data-code="{{ lang.code }}" href="{{ lang.url }}">{{ lang.label }}</a>{{ end }}
                  {{ content }}
                </body>
                </html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                """
                {
                  "name": "localization-project-test",
                  "engine": "scriban",
                  "defaultLayout": "docs"
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Localization Project Scope Test",
                BaseUrl = "https://example.test",
                ProjectsRoot = "projects",
                DefaultTheme = "localization-project-test",
                ThemesRoot = "themes",
                TrailingSlash = TrailingSlashMode.Always,
                Localization = new LocalizationSpec
                {
                    Enabled = true,
                    DefaultLanguage = "en",
                    PrefixDefaultLanguage = false,
                    DetectFromPath = true,
                    Languages = new[]
                    {
                        new LanguageSpec { Code = "en", Label = "English", Default = true },
                        new LanguageSpec { Code = "pl", Label = "Polski" }
                    }
                },
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "docs",
                        Input = "projects/*/content/docs",
                        Output = "/{project}/docs",
                        DefaultLayout = "docs"
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var outPath = Path.Combine(root, "_site");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteBuilder.Build(spec, plan, outPath);

            var projectAPlHtml = File.ReadAllText(Path.Combine(result.OutputPath, "pl", "a", "docs", "index.html"));
            Assert.Contains("data-code=\"en\" href=\"/a/docs/\"", projectAPlHtml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("data-code=\"en\" href=\"/b/docs/\"", projectAPlHtml, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_FlagsMissingRequiredNavLinks()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body>
                  <header><nav><a href="/">Home</a><a href="/docs/">Docs</a></nav></header>
                </body>
                </html>
                """);

            File.WriteAllText(Path.Combine(root, "docs.html"),
                """
                <!doctype html>
                <html>
                <head><title>Docs</title></head>
                <body>
                  <header><nav><a href="/docs/">Docs</a></nav></header>
                </body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                NavSelector = "header nav",
                NavRequiredLinks = new[] { "/" },
                CheckLinks = false,
                CheckAssets = false,
                CheckTitles = true,
                CheckHtmlStructure = true
            });

            Assert.True(result.Success);
            Assert.Equal(2, result.NavCheckedCount);
            Assert.Equal(0, result.NavIgnoredCount);
            Assert.Contains(result.Warnings, warning => warning.Contains("nav missing required links", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_TracksNavIgnoredPages()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-nav-ignore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body><header><nav><a href="/">Home</a></nav></header></body>
                </html>
                """);

            var apiRoot = Path.Combine(root, "api");
            Directory.CreateDirectory(apiRoot);
            File.WriteAllText(Path.Combine(apiRoot, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Api</title></head>
                <body><header><nav><a href="/api/">API</a></nav></header></body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                IgnoreNavFor = new[] { "api/**" },
                CheckLinks = false,
                CheckAssets = false
            });

            Assert.True(result.Success);
            Assert.Equal(1, result.NavCheckedCount);
            Assert.Equal(1, result.NavIgnoredCount);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_RespectsMaxHtmlFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-max-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "a.html"),
                """
                <!doctype html>
                <html>
                <head><title>A</title></head>
                <body><header><nav><a href="/">Home</a></nav></header></body>
                </html>
                """);
            File.WriteAllText(Path.Combine(root, "b.html"),
                """
                <!doctype html>
                <html>
                <head><title>B</title></head>
                <body><header><nav><a href="/">Home</a></nav></header></body>
                </html>
                """);
            File.WriteAllText(Path.Combine(root, "c.html"),
                """
                <!doctype html>
                <html>
                <head><title>C</title></head>
                <body><header><nav><a href="/">Home</a></nav></header></body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                MaxHtmlFiles = 1,
                CheckLinks = false,
                CheckAssets = false
            });

            Assert.Equal(3, result.HtmlFileCount);
            Assert.Equal(1, result.HtmlSelectedFileCount);
            Assert.Equal(1, result.PageCount);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_FailsWhenNavCoverageIsBelowThreshold()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-nav-coverage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body><header><nav><a href="/">Home</a></nav></header></body>
                </html>
                """);

            var apiRoot = Path.Combine(root, "api");
            Directory.CreateDirectory(apiRoot);
            File.WriteAllText(Path.Combine(apiRoot, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Api</title></head>
                <body><header><nav><a href="/api/">API</a></nav></header></body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                IgnoreNavFor = new[] { "api/**" },
                MinNavCoveragePercent = 80,
                CheckLinks = false,
                CheckAssets = false
            });

            Assert.False(result.Success);
            Assert.Equal(50.0, result.NavCoveragePercent, 2);
            Assert.Contains(result.Errors, error => error.Contains("min-nav-coverage", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_FailsWhenRequiredRouteIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-required-routes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body><header><nav><a href="/">Home</a></nav></header></body>
                </html>
                """);
            File.WriteAllText(Path.Combine(root, "404.html"),
                """
                <!doctype html>
                <html>
                <head><title>Not found</title></head>
                <body><header><nav><a href="/">Home</a></nav></header></body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                RequiredRoutes = new[] { "/", "/404.html", "/api/" },
                CheckLinks = false,
                CheckAssets = false
            });

            Assert.False(result.Success);
            Assert.Equal(3, result.RequiredRouteCount);
            Assert.Equal(1, result.MissingRequiredRouteCount);
            Assert.Contains(result.Errors, error => error.Contains("required route '/api/' is missing", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void OptimizeDetailed_ReturnsPerStageCounters()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-opt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                  <head>
                    <title>Test</title>
                    <link rel="stylesheet" href="/app.css" />
                  </head>
                  <body>
                    <script src="/site.js"></script>
                    <h1> Hello </h1>
                  </body>
                </html>
                """);

            File.WriteAllText(Path.Combine(root, "app.css"),
                """
                body {
                  color: red;
                  margin: 0;
                }
                """);

            File.WriteAllText(Path.Combine(root, "site.js"),
                """
                function x() {
                    console.log("test");
                }
                x();
                """);

            var result = WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions
            {
                SiteRoot = root,
                MinifyHtml = true,
                MinifyCss = true,
                MinifyJs = true
            });

            Assert.Equal(3, result.UpdatedCount);
            Assert.Equal(1, result.HtmlFileCount);
            Assert.Equal(1, result.HtmlMinifiedCount);
            Assert.Equal(1, result.CssMinifiedCount);
            Assert.Equal(1, result.JsMinifiedCount);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void OptimizeDetailed_HashedAssetCount_TracksFilesNotAliasMapEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-opt-hash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                  <head><link rel="stylesheet" href="/app.css" /></head>
                  <body><script src="/site.js"></script></body>
                </html>
                """);
            File.WriteAllText(Path.Combine(root, "app.css"), "body { color: red; }");
            File.WriteAllText(Path.Combine(root, "site.js"), "console.log('ok');");

            var result = WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions
            {
                SiteRoot = root,
                HashAssets = true,
                HashExtensions = new[] { ".css", ".js" }
            });

            Assert.Equal(2, result.HashedAssetCount);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void OptimizeDetailed_WritesReportWithUpdatedFilesAndByteSavings()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-opt-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                  <head><title>Test</title><link rel="stylesheet" href="/app.css" /></head>
                  <body><script src="/site.js"></script><h1> Hello </h1></body>
                </html>
                """);
            File.WriteAllText(Path.Combine(root, "app.css"), "body { color: red; margin: 0; }");
            File.WriteAllText(Path.Combine(root, "site.js"), "function x(){ console.log('ok'); } x();");

            var result = WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions
            {
                SiteRoot = root,
                MinifyHtml = true,
                MinifyCss = true,
                MinifyJs = true,
                ReportPath = "_reports/optimize-report.json"
            });

            Assert.False(string.IsNullOrWhiteSpace(result.ReportPath));
            Assert.True(File.Exists(result.ReportPath!));
            Assert.NotEmpty(result.UpdatedFiles);
            Assert.True(result.HtmlBytesSaved >= 0);
            Assert.True(result.CssBytesSaved >= 0);
            Assert.True(result.JsBytesSaved >= 0);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void OptimizeDetailed_OptimizesImagesAndTracksSavings()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-opt-images-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var imagePath = Path.Combine(root, "hero.png");
            using (var image = new MagickImage(MagickColors.DeepSkyBlue, 512, 256))
            {
                image.Comment = new string('x', 40000);
                image.Write(imagePath, MagickFormat.Png);
            }

            var originalBytes = new FileInfo(imagePath).Length;

            var result = WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions
            {
                SiteRoot = root,
                OptimizeImages = true,
                ImageExtensions = new[] { ".png" },
                ImageStripMetadata = true
            });

            Assert.Equal(1, result.ImageFileCount);
            Assert.Equal(1, result.ImageOptimizedCount);
            Assert.True(result.ImageBytesBefore >= originalBytes);
            Assert.True(result.ImageBytesSaved > 0);
            Assert.True(result.ImageBytesAfter < result.ImageBytesBefore);
            Assert.Single(result.OptimizedImages);
            Assert.Contains(result.UpdatedFiles, path => path.Equals("hero.png", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void OptimizeDetailed_GeneratesNextGenAndResponsiveVariantsWithHtmlHints()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-opt-img-variants-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                  <head><title>Images</title></head>
                  <body>
                    <img src="/hero.png" alt="Hero" />
                  </body>
                </html>
                """);

            var imagePath = Path.Combine(root, "hero.png");
            using (var image = new MagickImage(MagickColors.DeepSkyBlue, 1400, 700))
            {
                image.Comment = new string('x', 50000);
                image.Write(imagePath, MagickFormat.Png);
            }

            var result = WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions
            {
                SiteRoot = root,
                OptimizeImages = true,
                ImageExtensions = new[] { ".png" },
                ImageGenerateWebp = true,
                ImagePreferNextGen = true,
                ResponsiveImageWidths = new[] { 480, 960 },
                EnhanceImageTags = true
            });

            Assert.True(result.ImageVariantCount > 0);
            Assert.True(result.ImageHtmlRewriteCount > 0);
            Assert.True(result.ImageHintedCount > 0);
            Assert.Contains(result.GeneratedImageVariants, entry => entry.Width.HasValue);

            var html = File.ReadAllText(Path.Combine(root, "index.html"));
            Assert.Contains("srcset=", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("width=\"1400\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("height=\"700\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("loading=\"lazy\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("decoding=\"async\"", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void OptimizeDetailed_FlagsImageBudgetExceeded()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-opt-img-budget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var imagePath = Path.Combine(root, "budget.png");
            using (var image = new MagickImage(MagickColors.Gold, 1024, 1024))
            {
                image.Write(imagePath, MagickFormat.Png);
            }

            var result = WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions
            {
                SiteRoot = root,
                OptimizeImages = true,
                ImageExtensions = new[] { ".png" },
                ImageMaxBytesPerFile = 100,
                ImageMaxTotalBytes = 100
            });

            Assert.True(result.ImageBudgetExceeded);
            Assert.NotEmpty(result.ImageBudgetWarnings);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void OptimizeDetailed_RecordsImageFailures()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-opt-img-fail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var imagePath = Path.Combine(root, "bad.png");
            File.WriteAllBytes(imagePath, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }); // not a valid PNG

            var result = WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions
            {
                SiteRoot = root,
                OptimizeImages = true,
                ImageExtensions = new[] { ".png" }
            });

            Assert.Equal(1, result.ImageFileCount);
            Assert.Equal(0, result.ImageOptimizedCount);
            Assert.Equal(1, result.ImageFailedCount);
            Assert.Single(result.ImageFailures);
            Assert.Equal("bad.png", result.ImageFailures[0].Path, ignoreCase: true);
            Assert.False(string.IsNullOrWhiteSpace(result.ImageFailures[0].Error));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
