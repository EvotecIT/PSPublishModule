using PowerForge.Web;
using System.Diagnostics;

namespace PowerForge.Tests;

public class WebSiteBuilderSafetyAndParityTests
{
    [Fact]
    public void Build_BlocksIncludePathTraversalOutsideAllowedRoots()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-include-safety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var parent = Directory.GetParent(root)?.FullName ?? root;
            var secretPath = Path.Combine(parent, "secret-include.md");
            File.WriteAllText(secretPath, "SECRET_PAYLOAD");

            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                """
                ---
                title: Home
                slug: index
                ---

                {{< include path="../secret-include.md" >}}
                """);

            var spec = BuildBasicSpec("content/pages", "/");
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteBuilder.Build(spec, plan, Path.Combine(root, "_site"));
            var html = File.ReadAllText(Path.Combine(result.OutputPath, "index.html"));

            Assert.DoesNotContain("SECRET_PAYLOAD", html, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_SkipsStaticAssetDestinationTraversalOutsideOutputRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-static-safety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                """
                ---
                title: Home
                slug: index
                ---

                Home
                """);

            var staticPath = Path.Combine(root, "static");
            Directory.CreateDirectory(staticPath);
            File.WriteAllText(Path.Combine(staticPath, "safe.txt"), "SAFE");

            var spec = BuildBasicSpec("content/pages", "/");
            spec.StaticAssets = new[]
            {
                new StaticAssetSpec
                {
                    Source = "static/safe.txt",
                    Destination = "../escaped.txt"
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var plan = WebSitePlanner.Plan(spec, configPath);
            var outPath = Path.Combine(root, "_site");
            WebSiteBuilder.Build(spec, plan, outPath);

            Assert.False(File.Exists(Path.Combine(root, "escaped.txt")));
            Assert.False(File.Exists(Path.Combine(outPath, "escaped.txt")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_CopiesStaticAssetDirectoryToOutputRootWhenDestinationMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-static-root-copy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                """
                ---
                title: Home
                slug: index
                ---

                Home
                """);

            var staticCssPath = Path.Combine(root, "static", "css");
            Directory.CreateDirectory(staticCssPath);
            File.WriteAllText(Path.Combine(staticCssPath, "app.css"), "body { color: #111; }");

            var spec = BuildBasicSpec("content/pages", "/");
            spec.StaticAssets = new[]
            {
                new StaticAssetSpec
                {
                    Source = "static"
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var plan = WebSitePlanner.Plan(spec, configPath);
            var outPath = Path.Combine(root, "_site");
            WebSiteBuilder.Build(spec, plan, outPath);

            Assert.True(File.Exists(Path.Combine(outPath, "css", "app.css")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_RespectsCollectionIncludePatternsLikeBuild()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-include-parity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var docsPath = Path.Combine(root, "content", "docs");
            Directory.CreateDirectory(docsPath);
            File.WriteAllText(Path.Combine(docsPath, "visible.md"),
                """
                ---
                title: Visible
                slug: same
                ---

                Visible
                """);
            File.WriteAllText(Path.Combine(docsPath, "hidden.md"),
                """
                ---
                title: Hidden
                slug: same
                ---

                Hidden
                """);

            var spec = BuildBasicSpec("content/docs", "/docs");
            spec.Collections[0].Include = new[] { "visible.md" };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var plan = WebSitePlanner.Plan(spec, configPath);
            var verify = WebSiteVerifier.Verify(spec, plan);

            Assert.True(verify.Success);
            Assert.DoesNotContain(verify.Errors, error => error.Contains("Duplicate route", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_RespectsContentRootsLikeBuild()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-contentroots-parity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var docsPath = Path.Combine(root, "external", "docs");
            Directory.CreateDirectory(docsPath);
            File.WriteAllText(Path.Combine(docsPath, "index.md"),
                """
                ---
                title: External Docs
                slug: index
                ---

                Docs
                """);

            var spec = new SiteSpec
            {
                Name = "Verify ContentRoots Parity Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                ContentRoots = new[] { "external" },
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "docs",
                        Input = "docs",
                        Output = "/docs"
                    }
                }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var plan = WebSitePlanner.Plan(spec, configPath);
            var verify = WebSiteVerifier.Verify(spec, plan);
            var build = WebSiteBuilder.Build(spec, plan, Path.Combine(root, "_site"));

            Assert.True(verify.Success);
            Assert.DoesNotContain(verify.Warnings, warning =>
                warning.Contains("Collection 'docs' has no files.", StringComparison.OrdinalIgnoreCase));
            Assert.True(File.Exists(Path.Combine(build.OutputPath, "docs", "index.html")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_DoesNotWarn_WhenCssStrategyIsExplicitBlocking()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-css-strategy-blocking-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var writer = new StringWriter();
        var listener = new TextWriterTraceListener(writer);
        var listeners = Trace.Listeners.Cast<TraceListener>().ToArray();
        var previousAutoFlush = Trace.AutoFlush;

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                """
                ---
                title: Home
                slug: index
                ---

                Home
                """);

            var spec = BuildBasicSpec("content/pages", "/");
            spec.AssetRegistry = new AssetRegistrySpec
            {
                CssStrategy = "blocking",
                Bundles = new[]
                {
                    new AssetBundleSpec
                    {
                        Name = "global",
                        Css = new[] { "/assets/app.css" }
                    }
                },
                RouteBundles = new[]
                {
                    new RouteBundleSpec
                    {
                        Match = "/**",
                        Bundles = new[] { "global" }
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var plan = WebSitePlanner.Plan(spec, configPath);

            Trace.Listeners.Clear();
            Trace.Listeners.Add(listener);
            Trace.AutoFlush = true;

            var build = WebSiteBuilder.Build(spec, plan, Path.Combine(root, "_site"));
            var html = File.ReadAllText(Path.Combine(build.OutputPath, "index.html"));

            listener.Flush();
            var traceOutput = writer.ToString();
            Assert.Contains("<link rel=\"stylesheet\" href=\"/assets/app.css\" />", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Unknown CssStrategy", traceOutput, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            listener.Flush();
            listener.Close();
            Trace.Listeners.Clear();
            foreach (var existing in listeners)
                Trace.Listeners.Add(existing);
            Trace.AutoFlush = previousAutoFlush;

            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_EmitsTraceWarning_WhenHeadFileCannotBeResolved()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-headfile-warning-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var writer = new StringWriter();
        var listener = new TextWriterTraceListener(writer);
        var listeners = Trace.Listeners.Cast<TraceListener>().ToArray();
        var previousAutoFlush = Trace.AutoFlush;

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                """
                ---
                title: Home
                slug: index
                meta.head_file: shared/head/pages/missing.head.html
                ---

                Home
                """);

            var spec = BuildBasicSpec("content/pages", "/");
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var plan = WebSitePlanner.Plan(spec, configPath);

            Trace.Listeners.Clear();
            Trace.Listeners.Add(listener);
            Trace.AutoFlush = true;

            var build = WebSiteBuilder.Build(spec, plan, Path.Combine(root, "_site"));
            Assert.True(File.Exists(Path.Combine(build.OutputPath, "index.html")));

            listener.Flush();
            var traceOutput = writer.ToString();
            Assert.Contains("unable to resolve head_file", traceOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("shared/head/pages/missing.head.html", traceOutput, StringComparison.Ordinal);
        }
        finally
        {
            listener.Flush();
            listener.Close();
            Trace.Listeners.Clear();
            foreach (var existing in listeners)
                Trace.Listeners.Add(existing);
            Trace.AutoFlush = previousAutoFlush;

            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_Fails_WhenHeadFileCannotBeResolved()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-headfile-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                """
                ---
                title: Home
                slug: index
                meta.head_file: shared/head/pages/missing.head.html
                ---

                Home
                """);

            var spec = BuildBasicSpec("content/pages", "/");
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var plan = WebSitePlanner.Plan(spec, configPath);
            var verify = WebSiteVerifier.Verify(spec, plan);

            Assert.False(verify.Success);
            Assert.Contains(verify.Errors, error => error.Contains("meta.head_file", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_RendersHeadLinks_WithAsAndCustomAttributes()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-head-links-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                """
                ---
                title: Home
                slug: index
                ---

                Home
                """);

            var spec = BuildBasicSpec("content/pages", "/");
            spec.Head = new HeadSpec
            {
                Links = new[]
                {
                    new HeadLinkSpec
                    {
                        Rel = "preload",
                        Href = "/fonts/test.woff2",
                        As = "font",
                        Type = "font/woff2",
                        Crossorigin = "anonymous",
                        Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["title"] = "Test font",
                            ["data-asset-role"] = "font-preload"
                        }
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var plan = WebSitePlanner.Plan(spec, configPath);
            var build = WebSiteBuilder.Build(spec, plan, Path.Combine(root, "_site"));
            var html = File.ReadAllText(Path.Combine(build.OutputPath, "index.html"));

            Assert.Contains("rel=\"preload\"", html, StringComparison.Ordinal);
            Assert.Equal(1, CountOccurrences(html, "href=\"/fonts/test.woff2\"", StringComparison.Ordinal));
            Assert.Contains("as=\"font\"", html, StringComparison.Ordinal);
            Assert.Contains("data-asset-role=\"font-preload\"", html, StringComparison.Ordinal);
            Assert.Contains("title=\"Test font\"", html, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_RendersHeadStylesheetsBeforeRouteStyles_WithoutDuplicating()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-head-stylesheet-slot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                """
                ---
                title: Home
                slug: index
                ---

                Home
                """);

            var spec = BuildBasicSpec("content/pages", "/");
            spec.Head = new HeadSpec
            {
                Links = new[]
                {
                    new HeadLinkSpec
                    {
                        Rel = "stylesheet",
                        Href = "/fonts/site-fonts.css",
                        Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["data-asset-role"] = "font-stylesheet"
                        }
                    },
                    new HeadLinkSpec
                    {
                        Rel = "icon",
                        Href = "/favicon.ico"
                    }
                }
            };
            spec.AssetRegistry = new AssetRegistrySpec
            {
                Bundles = new[]
                {
                    new AssetBundleSpec
                    {
                        Name = "global",
                        Css = new[] { "/assets/app.css" }
                    }
                },
                RouteBundles = new[]
                {
                    new RouteBundleSpec
                    {
                        Match = "/**",
                        Bundles = new[] { "global" }
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");

            var plan = WebSitePlanner.Plan(spec, configPath);
            var build = WebSiteBuilder.Build(spec, plan, Path.Combine(root, "_site"));
            var html = File.ReadAllText(Path.Combine(build.OutputPath, "index.html"));

            var fontIndex = html.IndexOf("href=\"/fonts/site-fonts.css\"", StringComparison.Ordinal);
            var appIndex = html.IndexOf("href=\"/assets/app.css\"", StringComparison.Ordinal);

            Assert.True(fontIndex >= 0, "Expected the head stylesheet to render through the CSS asset slot.");
            Assert.True(appIndex >= 0, "Expected the route stylesheet to render.");
            Assert.True(fontIndex < appIndex, "Head stylesheets should be emitted before route bundle styles.");
            Assert.Equal(1, CountOccurrences(html, "href=\"/fonts/site-fonts.css\"", StringComparison.Ordinal));
            Assert.Equal(1, CountOccurrences(html, "href=\"/assets/app.css\"", StringComparison.Ordinal));
            Assert.Contains("data-asset-role=\"font-stylesheet\"", html, StringComparison.Ordinal);
            Assert.Contains("href=\"/favicon.ico\"", html, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static SiteSpec BuildBasicSpec(string input, string output)
    {
        return new SiteSpec
        {
            Name = "Web Safety Test",
            BaseUrl = "https://example.test",
            ContentRoot = "content",
            Collections = new[]
            {
                new CollectionSpec
                {
                    Name = "pages",
                    Input = input,
                    Output = output
                }
            }
        };
    }

    private static int CountOccurrences(string text, string value, StringComparison comparison)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, comparison)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}
