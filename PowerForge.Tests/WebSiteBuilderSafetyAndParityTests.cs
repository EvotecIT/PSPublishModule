using PowerForge.Web;

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
}
