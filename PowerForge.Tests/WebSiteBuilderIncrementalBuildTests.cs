using PowerForge.Web;

namespace PowerForge.Tests;

public class WebSiteBuilderIncrementalBuildTests
{
    [Fact]
    public void Build_DoesNotRewriteUnchangedOutputs()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-incremental-" + Guid.NewGuid().ToString("N"));
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

            var themeRoot = Path.Combine(root, "themes", "t");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "home.html"),
                """
                <!doctype html>
                <html>
                <head><title>{{TITLE}}</title></head>
                <body>{{CONTENT}}</body>
                </html>
                """);
            Directory.CreateDirectory(Path.Combine(themeRoot, "assets"));
            File.WriteAllText(Path.Combine(themeRoot, "assets", "test.txt"), "hello");
            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                """
                {
                  "name": "t",
                  "engine": "simple",
                  "defaultLayout": "home"
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Incremental Build Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "t",
                ThemesRoot = "themes",
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

            var outPath = Path.Combine(root, "_site");
            WebSiteBuilder.Build(spec, plan, outPath);

            var indexHtml = Path.Combine(outPath, "index.html");
            var planJson = Path.Combine(outPath, "_powerforge", "site-plan.json");
            var themeAsset = Path.Combine(outPath, "themes", "t", "assets", "test.txt");

            Assert.True(File.Exists(indexHtml));
            Assert.True(File.Exists(planJson));
            Assert.True(File.Exists(themeAsset));

            var indexTime = File.GetLastWriteTimeUtc(indexHtml);
            var planTime = File.GetLastWriteTimeUtc(planJson);
            var assetTime = File.GetLastWriteTimeUtc(themeAsset);

            Thread.Sleep(1200);

            plan = WebSitePlanner.Plan(spec, configPath);
            WebSiteBuilder.Build(spec, plan, outPath);

            Assert.Equal(indexTime, File.GetLastWriteTimeUtc(indexHtml));
            Assert.Equal(planTime, File.GetLastWriteTimeUtc(planJson));
            Assert.Equal(assetTime, File.GetLastWriteTimeUtc(themeAsset));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}

