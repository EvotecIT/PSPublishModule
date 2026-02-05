using PowerForge.Web;

public class WebSiteVerifierTests
{
    [Fact]
    public void Verify_WarnsOnMarkdownRawHtmlHygiene()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var docsPath = Path.Combine(root, "content", "docs");
            Directory.CreateDirectory(docsPath);
            File.WriteAllText(Path.Combine(docsPath, "index.md"),
                """
                ---
                title: Home
                slug: index
                ---

                <p><strong>Hello</strong> world</p>
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "docs",
                        Input = "content/docs",
                        Output = "/docs"
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(result.Success);
            Assert.Contains(result.Warnings, warning => warning.Contains("Markdown hygiene", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_WarnsWhen404RouteHasNoAssetBundleMatch()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-404-" + Guid.NewGuid().ToString("N"));
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
            File.WriteAllText(Path.Combine(pagesPath, "404.md"),
                """
                ---
                title: Not found
                slug: 404
                ---

                Not found
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "pages",
                        Input = "content/pages",
                        Output = "/"
                    }
                },
                AssetRegistry = new AssetRegistrySpec
                {
                    Bundles = new[]
                    {
                        new AssetBundleSpec
                        {
                            Name = "global",
                            Css = new[] { "/css/app.css" },
                            Js = new[] { "/js/site.js" }
                        }
                    },
                    RouteBundles = new[]
                    {
                        new RouteBundleSpec
                        {
                            Match = "/",
                            Bundles = new[] { "global" }
                        }
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(result.Success);
            Assert.Contains(result.Warnings, warning =>
                warning.Contains("Route '/404", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("does not match any AssetRegistry.RouteBundles entry", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Verify_DoesNotWarnWhen404RouteHasFallbackAssetBundle()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-verify-404-ok-" + Guid.NewGuid().ToString("N"));
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
            File.WriteAllText(Path.Combine(pagesPath, "404.md"),
                """
                ---
                title: Not found
                slug: 404
                ---

                Not found
                """);

            var spec = new SiteSpec
            {
                Name = "Verifier Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "pages",
                        Input = "content/pages",
                        Output = "/"
                    }
                },
                AssetRegistry = new AssetRegistrySpec
                {
                    Bundles = new[]
                    {
                        new AssetBundleSpec
                        {
                            Name = "global",
                            Css = new[] { "/css/app.css" },
                            Js = new[] { "/js/site.js" }
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
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteVerifier.Verify(spec, plan);

            Assert.True(result.Success);
            Assert.DoesNotContain(result.Warnings, warning =>
                warning.Contains("does not match any AssetRegistry.RouteBundles entry", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
