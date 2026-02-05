using PowerForge.Web;

namespace PowerForge.Tests;

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

    [Fact]
    public void Build_NotFoundBundleResources_AreCopiedNextToRoot404Page()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-build-404-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var contentRoot = Path.Combine(root, "content", "pages", "404");
            Directory.CreateDirectory(contentRoot);
            File.WriteAllText(Path.Combine(contentRoot, "index.md"),
                """
                ---
                title: Page not found
                ---

                ![Missing page](./notfound.png)
                """);
            File.WriteAllBytes(Path.Combine(contentRoot, "notfound.png"), new byte[] { 1, 2, 3, 4 });

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
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var outputRoot = Path.Combine(root, "_site");

            WebSiteBuilder.Build(spec, plan, outputRoot);

            Assert.True(File.Exists(Path.Combine(outputRoot, "404.html")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "notfound.png")));
            Assert.False(File.Exists(Path.Combine(outputRoot, "404", "notfound.png")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_FailOnWarnings_TriggersGateError()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body>
                  <nav><a href="/docs/">Docs</a></nav>
                </body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                NavRequiredLinks = new[] { "/" },
                CheckLinks = false,
                CheckAssets = false,
                FailOnWarnings = true
            });

            Assert.False(result.Success);
            Assert.Contains(result.Errors, error => error.Contains("fail-on-warnings", StringComparison.OrdinalIgnoreCase));
            Assert.True(result.WarningCount > 0);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_Baseline_SuppressesExistingIssuesForFailOnNew()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-baseline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body>
                  <nav><a href="/docs/">Docs</a></nav>
                </body>
                </html>
                """);

            var summaryPath = Path.Combine(root, "audit-baseline.json");
            var first = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                NavRequiredLinks = new[] { "/" },
                CheckLinks = false,
                CheckAssets = false,
                SummaryPath = summaryPath
            });

            Assert.True(first.WarningCount > 0);
            Assert.True(File.Exists(summaryPath));

            var second = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                NavRequiredLinks = new[] { "/" },
                CheckLinks = false,
                CheckAssets = false,
                BaselinePath = summaryPath,
                FailOnNewIssues = true
            });

            Assert.True(second.Success);
            Assert.Equal(0, second.NewIssueCount);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_BaselinePathOutsideRoot_Throws()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-baseline-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><html><head><title>Home</title></head><body></body></html>");
            var outsideBaselinePath = Path.Combine(Path.GetTempPath(), "pf-web-audit-outside-" + Guid.NewGuid().ToString("N") + ".json");

            Assert.Throws<InvalidOperationException>(() => WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                CheckLinks = false,
                CheckAssets = false,
                BaselinePath = outsideBaselinePath
            }));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_UsesCanonicalNavPathForConsistency()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-canonical-nav-" + Guid.NewGuid().ToString("N"));
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

            File.WriteAllText(Path.Combine(root, "404.html"),
                """
                <!doctype html>
                <html>
                <head><title>Not found</title></head>
                <body>
                  <header><nav><a href="/docs/">Docs</a></nav></header>
                </body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                NavSelector = "header nav",
                NavCanonicalPath = "index.html",
                CheckLinks = false,
                CheckAssets = false
            });

            Assert.True(result.Success);
            Assert.Equal(1, result.NavMismatchCount);
            Assert.Contains(result.Warnings, warning => warning.Contains("nav differs from baseline", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_DetectsInvalidUtf8()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-utf8-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var invalidBytes = new byte[] { 0x3C, 0x68, 0x74, 0x6D, 0x6C, 0x3E, 0xC3, 0x28, 0x3C, 0x2F, 0x68, 0x74, 0x6D, 0x6C, 0x3E };
            File.WriteAllBytes(Path.Combine(root, "index.html"), invalidBytes);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                CheckLinks = false,
                CheckAssets = false,
                CheckTitles = false,
                CheckHtmlStructure = false
            });

            Assert.False(result.Success);
            Assert.Contains(result.Errors, error => error.Contains("invalid UTF-8", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Errors, error => error.Contains("byte offset", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Issues, issue => issue.Category.Equals("utf8", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
