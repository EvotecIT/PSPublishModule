using PowerForge.Web;

namespace PowerForge.Tests;

public class WebSiteBuilderPrismAssetInjectionTests
{
    [Fact]
    public void Build_InjectsManualPrismBootstrapBeforeLocalHashedAssets()
    {
        var root = CreateTempRoot("pf-web-prism-local-hash-");
        try
        {
            WritePage(root, "index.md",
                """
                ---
                title: Home
                slug: index
                ---

                ```powershell
                Get-Date
                ```
                """);

            var prismRoot = Path.Combine(root, "assets", "prism");
            var componentsRoot = Path.Combine(prismRoot, "components");
            Directory.CreateDirectory(componentsRoot);
            File.WriteAllText(Path.Combine(prismRoot, "prism.css"), "/* prism */");
            File.WriteAllText(Path.Combine(prismRoot, "prism-okaidia.css"), "/* prism dark */");
            File.WriteAllText(Path.Combine(prismRoot, "prism-core.a1b2.js"), "/* prism core */");
            File.WriteAllText(Path.Combine(prismRoot, "prism-autoloader.c3d4.js"), "/* prism autoloader */");

            var spec = BuildPagesSpec();
            spec.Prism = new PrismSpec
            {
                Source = "local",
                Local = new PrismLocalSpec
                {
                    Core = "/assets/prism/prism-core.a1b2.js",
                    Autoloader = "/assets/prism/prism-autoloader.c3d4.js",
                    LanguagesPath = "/assets/prism/components/"
                }
            };

            var html = BuildAndRead(root, spec, "index.html");
            Assert.Contains("window.Prism=window.Prism||{};window.Prism.manual=true;", html, StringComparison.Ordinal);
            Assert.Contains("setTimeout(run,delayMs)", html, StringComparison.Ordinal);
            Assert.Contains("targetPath='/assets/prism/components/'", html, StringComparison.Ordinal);

            var manualIndex = html.IndexOf("window.Prism=window.Prism||{};window.Prism.manual=true;", StringComparison.Ordinal);
            var coreIndex = html.IndexOf("src=\"/assets/prism/prism-core.a1b2.js\"", StringComparison.Ordinal);
            var autoloaderIndex = html.IndexOf("src=\"/assets/prism/prism-autoloader.c3d4.js\"", StringComparison.Ordinal);
            Assert.True(manualIndex >= 0, "Expected Prism bootstrap script.");
            Assert.True(coreIndex > manualIndex, "Prism core should load after Prism manual bootstrap.");
            Assert.True(autoloaderIndex > coreIndex, "Prism autoloader should load after Prism core.");
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Build_InjectsManualPrismBootstrapBeforeCdnAssets()
    {
        var root = CreateTempRoot("pf-web-prism-cdn-bootstrap-");
        try
        {
            WritePage(root, "index.md",
                """
                ---
                title: Home
                slug: index
                ---

                ```csharp
                Console.WriteLine("hello");
                ```
                """);

            var spec = BuildPagesSpec();
            spec.Prism = new PrismSpec
            {
                Source = "cdn",
                CdnBase = "https://cdn.example/prismjs@1.29.0"
            };

            var html = BuildAndRead(root, spec, "index.html");
            Assert.Contains("window.Prism=window.Prism||{};window.Prism.manual=true;", html, StringComparison.Ordinal);
            Assert.Contains("src=\"https://cdn.example/prismjs@1.29.0/components/prism-core.min.js\"", html, StringComparison.Ordinal);
            Assert.Contains("src=\"https://cdn.example/prismjs@1.29.0/plugins/autoloader/prism-autoloader.min.js\"", html, StringComparison.Ordinal);
            Assert.Contains("targetPath='https://cdn.example/prismjs@1.29.0/components/'", html, StringComparison.Ordinal);
            Assert.Contains("setTimeout(run,delayMs)", html, StringComparison.Ordinal);

            var manualIndex = html.IndexOf("window.Prism=window.Prism||{};window.Prism.manual=true;", StringComparison.Ordinal);
            var coreIndex = html.IndexOf("src=\"https://cdn.example/prismjs@1.29.0/components/prism-core.min.js\"", StringComparison.Ordinal);
            var autoloaderIndex = html.IndexOf("src=\"https://cdn.example/prismjs@1.29.0/plugins/autoloader/prism-autoloader.min.js\"", StringComparison.Ordinal);
            Assert.True(manualIndex >= 0, "Expected Prism bootstrap script.");
            Assert.True(coreIndex > manualIndex, "Prism core should load after Prism manual bootstrap.");
            Assert.True(autoloaderIndex > coreIndex, "Prism autoloader should load after Prism core.");
        }
        finally
        {
            Cleanup(root);
        }
    }

    private static SiteSpec BuildPagesSpec()
    {
        return new SiteSpec
        {
            Name = "Prism Injection Test",
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
            }
        };
    }

    private static string BuildAndRead(string root, SiteSpec spec, string relativeOutputFile)
    {
        var configPath = Path.Combine(root, "site.json");
        File.WriteAllText(configPath, "{}");
        var plan = WebSitePlanner.Plan(spec, configPath);
        var outPath = Path.Combine(root, "_site");
        WebSiteBuilder.Build(spec, plan, outPath);
        return File.ReadAllText(Path.Combine(outPath, relativeOutputFile));
    }

    private static void WritePage(string root, string fileName, string markdown)
    {
        var pagesPath = Path.Combine(root, "content", "pages");
        Directory.CreateDirectory(pagesPath);
        File.WriteAllText(Path.Combine(pagesPath, fileName), markdown);
    }

    private static string CreateTempRoot(string prefix)
    {
        var root = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Cleanup(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}
