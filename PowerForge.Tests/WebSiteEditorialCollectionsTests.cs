using PowerForge.Web;

namespace PowerForge.Tests;

public class WebSiteEditorialCollectionsTests
{
    [Fact]
    public void Build_AutoGeneratesSectionIndex_WhenEnabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-editorial-autoindex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var blogPath = Path.Combine(root, "content", "blog");
            Directory.CreateDirectory(blogPath);
            File.WriteAllText(Path.Combine(blogPath, "first-post.md"),
                """
                ---
                title: First Post
                date: 2026-02-10
                ---

                Hello
                """);

            var themeRoot = Path.Combine(root, "themes", "editorial-test");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "post.html"),
                """
                <!doctype html>
                <html>
                <body>
                  <article>{{ page.title }}</article>
                </body>
                </html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "list.html"),
                """
                <!doctype html>
                <html>
                <body>
                  <h1>{{ page.title }}</h1>
                  <div id="items">{{ for i in items }}{{ i.title }};{{ end }}</div>
                </body>
                </html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                """
                {
                  "name": "editorial-test",
                  "engine": "scriban",
                  "defaultLayout": "post"
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Editorial Auto Index Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "editorial-test",
                ThemesRoot = "themes",
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "blog",
                        Input = "content/blog",
                        Output = "/blog",
                        DefaultLayout = "post",
                        ListLayout = "list",
                        AutoGenerateSectionIndex = true,
                        AutoSectionTitle = "Blog Updates"
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteBuilder.Build(spec, plan, Path.Combine(root, "_site"));

            var blogIndex = Path.Combine(result.OutputPath, "blog", "index.html");
            Assert.True(File.Exists(blogIndex));

            var html = File.ReadAllText(blogIndex);
            Assert.Contains("Blog Updates", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("First Post", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_EditorialSectionDefaultsToNewestFirst()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-editorial-sort-default-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var blogPath = Path.Combine(root, "content", "blog");
            Directory.CreateDirectory(blogPath);
            File.WriteAllText(Path.Combine(blogPath, "older.md"),
                """
                ---
                title: Older
                date: 2026-01-01
                ---

                Older
                """);
            File.WriteAllText(Path.Combine(blogPath, "newer.md"),
                """
                ---
                title: Newer
                date: 2026-02-01
                ---

                Newer
                """);

            var themeRoot = Path.Combine(root, "themes", "editorial-sort-default");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "post.html"), "<html><body>{{ page.title }}</body></html>");
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "list.html"), "<html><body><div id=\"items\">{{ for i in items }}{{ i.title }};{{ end }}</div></body></html>");
            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                """
                {
                  "name": "editorial-sort-default",
                  "engine": "scriban",
                  "defaultLayout": "post"
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Editorial Sort Default Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "editorial-sort-default",
                ThemesRoot = "themes",
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "blog",
                        Input = "content/blog",
                        Output = "/blog",
                        DefaultLayout = "post",
                        ListLayout = "list",
                        AutoGenerateSectionIndex = true
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteBuilder.Build(spec, plan, Path.Combine(root, "_site"));

            var html = File.ReadAllText(Path.Combine(result.OutputPath, "blog", "index.html"));
            Assert.Contains("Newer;Older;", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_SectionListRespectsSortByAndSortOrder()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-editorial-sort-explicit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var newsPath = Path.Combine(root, "content", "news");
            Directory.CreateDirectory(newsPath);
            File.WriteAllText(Path.Combine(newsPath, "zeta.md"),
                """
                ---
                title: Zeta
                date: 2026-02-01
                ---

                Zeta
                """);
            File.WriteAllText(Path.Combine(newsPath, "alpha.md"),
                """
                ---
                title: Alpha
                date: 2026-01-01
                ---

                Alpha
                """);

            var themeRoot = Path.Combine(root, "themes", "editorial-sort-explicit");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "post.html"), "<html><body>{{ page.title }}</body></html>");
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "list.html"), "<html><body><div id=\"items\">{{ for i in items }}{{ i.title }};{{ end }}</div></body></html>");
            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                """
                {
                  "name": "editorial-sort-explicit",
                  "engine": "scriban",
                  "defaultLayout": "post"
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Editorial Sort Explicit Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "editorial-sort-explicit",
                ThemesRoot = "themes",
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "news",
                        Input = "content/news",
                        Output = "/news",
                        DefaultLayout = "post",
                        ListLayout = "list",
                        AutoGenerateSectionIndex = true,
                        SortBy = "title",
                        SortOrder = SortOrder.Asc
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteBuilder.Build(spec, plan, Path.Combine(root, "_site"));

            var html = File.ReadAllText(Path.Combine(result.OutputPath, "news", "index.html"));
            Assert.Contains("Alpha;Zeta;", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_BlogPresetOnCustomCollectionName_AppliesAutoLandingAndListLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-editorial-preset-custom-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var updatesPath = Path.Combine(root, "content", "updates");
            Directory.CreateDirectory(updatesPath);
            File.WriteAllText(Path.Combine(updatesPath, "first.md"),
                """
                ---
                title: First Update
                date: 2026-02-15
                ---

                Update body
                """);

            var themeRoot = Path.Combine(root, "themes", "editorial-preset-custom");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "post.html"), "<html><body><div id=\"post-layout\">{{ page.title }}</div></body></html>");
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "list.html"), "<html><body><div id=\"list-layout\">{{ page.title }}|{{ for i in items }}{{ i.title }};{{ end }}</div></body></html>");
            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                """
                {
                  "name": "editorial-preset-custom",
                  "engine": "scriban",
                  "defaultLayout": "post"
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Editorial Preset Custom Collection Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "editorial-preset-custom",
                ThemesRoot = "themes",
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "updates",
                        Preset = "blog",
                        Input = "content/updates",
                        Output = "/updates"
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteBuilder.Build(spec, plan, Path.Combine(root, "_site"));

            var html = File.ReadAllText(Path.Combine(result.OutputPath, "updates", "index.html"));
            Assert.Contains("id=\"list-layout\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("First Update", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
