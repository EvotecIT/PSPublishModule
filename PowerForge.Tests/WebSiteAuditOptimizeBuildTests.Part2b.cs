using PowerForge.Web;
using ImageMagick;
using System.Xml.Linq;

namespace PowerForge.Tests;

public partial class WebSiteAuditOptimizeBuildTests
{
    [Fact]
    public void OptimizeDetailed_RespectsHtmlIncludeExcludeAndMaxHtmlFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-opt-html-scope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><html><head><title>t</title></head><body><h1> Hello </h1></body></html>");
            File.WriteAllText(Path.Combine(root, "docs", "a.html"), "<!doctype html><html><head><title>t</title></head><body><h1> A </h1></body></html>");
            File.WriteAllText(Path.Combine(root, "docs", "b.html"), "<!doctype html><html><head><title>t</title></head><body><h1> B </h1></body></html>");

            var result = WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions
            {
                SiteRoot = root,
                MinifyHtml = true,
                HtmlInclude = new[] { "docs/*.html" },
                HtmlExclude = new[] { "docs/b.html" },
                MaxHtmlFiles = 1
            });

            Assert.Equal(3, result.HtmlFileCount);
            Assert.Equal(1, result.HtmlSelectedFileCount);
            Assert.Equal(1, result.HtmlMinifiedCount);
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
