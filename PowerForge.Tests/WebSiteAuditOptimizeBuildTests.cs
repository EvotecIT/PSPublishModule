using PowerForge.Web;

public class WebSiteAuditOptimizeBuildTests
{
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
            Assert.Contains(result.Warnings, warning => warning.Contains("nav missing required links", StringComparison.OrdinalIgnoreCase));
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

            Assert.True(result.UpdatedCount >= 1);
            Assert.Equal(1, result.HtmlFileCount);
            Assert.True(result.HtmlMinifiedCount >= 1);
            Assert.True(result.CssMinifiedCount >= 1);
            Assert.True(result.JsMinifiedCount >= 1);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_WritesRoot404HtmlForNotFoundSlug()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var contentRoot = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(contentRoot);
            File.WriteAllText(Path.Combine(contentRoot, "404.md"),
                """
                ---
                title: Page not found
                slug: 404
                ---

                # Not found
                """);

            var spec = new SiteSpec
            {
                Name = "Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                TrailingSlash = TrailingSlashMode.Always,
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "pages",
                        Input = "content/pages",
                        Output = "/"
                    }
                },
                Navigation = new NavigationSpec
                {
                    Menus = new[]
                    {
                        new MenuSpec
                        {
                            Name = "main",
                            Items = new[] { new MenuItemSpec { Title = "Home", Url = "/" } }
                        }
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var outputRoot = Path.Combine(root, "_site");

            WebSiteBuilder.Build(spec, plan, outputRoot);

            Assert.True(File.Exists(Path.Combine(outputRoot, "404.html")));
            Assert.False(File.Exists(Path.Combine(outputRoot, "404", "index.html")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
