using System.Text.Json;
using PowerForge.Web;

public class WebSiteScaffolderTests
{
    [Fact]
    public void Scaffold_CreatesBlogAndTaxonomyStarterContent()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-scaffold-" + Guid.NewGuid().ToString("N"));

        try
        {
            var result = WebSiteScaffolder.Scaffold(root, "Starter", "https://example.test", "simple");
            Assert.True(Directory.Exists(result.OutputPath));

            Assert.True(File.Exists(Path.Combine(root, "content", "blog", "_index.md")));
            Assert.True(File.Exists(Path.Combine(root, "content", "blog", "hello-world.md")));

            using var specDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "site.json")));
            var spec = specDoc.RootElement;

            var collections = spec.GetProperty("collections").EnumerateArray()
                .Select(element => element.GetProperty("name").GetString() ?? string.Empty)
                .ToArray();
            Assert.Contains("blog", collections, StringComparer.OrdinalIgnoreCase);

            var taxonomies = spec.GetProperty("taxonomies").EnumerateArray()
                .Select(element => element.GetProperty("name").GetString() ?? string.Empty)
                .ToArray();
            Assert.Contains("tags", taxonomies, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("categories", taxonomies, StringComparer.OrdinalIgnoreCase);

            var navigationItems = spec.GetProperty("navigation")
                .GetProperty("menus")[0]
                .GetProperty("items")
                .EnumerateArray()
                .Select(element => element.GetProperty("title").GetString() ?? string.Empty)
                .ToArray();
            Assert.Contains("Blog", navigationItems, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_ExportsNavigationTemplateMetadataToSiteNav()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-nav-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                """
                ---
                title: Home
                ---

                Home
                """);

            var themeLayouts = Path.Combine(root, "themes", "base", "layouts");
            Directory.CreateDirectory(themeLayouts);
            File.WriteAllText(Path.Combine(themeLayouts, "page.html"),
                """
                <!doctype html><html><head><title>{{TITLE}}</title></head><body>{{CONTENT}}</body></html>
                """);

            var spec = new SiteSpec
            {
                Name = "Nav metadata test",
                BaseUrl = "https://example.test",
                DefaultTheme = "base",
                ContentRoot = "content",
                ThemesRoot = "themes",
                DataRoot = "data",
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "pages",
                        Input = "content/pages",
                        Output = "/",
                        DefaultLayout = "page"
                    }
                },
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus = new[]
                    {
                        new MenuSpec
                        {
                            Name = "main",
                            Label = "Main",
                            Template = "menu-pill",
                            CssClass = "menu-main",
                            Meta = new Dictionary<string, object?> { ["variant"] = "primary" },
                            Items = new[]
                            {
                                new MenuItemSpec { Title = "Home", Url = "/" }
                            }
                        }
                    },
                    Regions = new[]
                    {
                        new NavigationRegionSpec
                        {
                            Name = "header.right",
                            Template = "cluster",
                            CssClass = "hdr-right",
                            Meta = new Dictionary<string, object?> { ["collapse"] = "mobile" },
                            Menus = new[] { "main" }
                        }
                    },
                    Footer = new NavigationFooterSpec
                    {
                        Label = "Footer",
                        Template = "footer-grid",
                        CssClass = "site-footer",
                        Meta = new Dictionary<string, object?> { ["columns"] = 3 },
                        Columns = new[]
                        {
                            new NavigationFooterColumnSpec
                            {
                                Name = "product",
                                Title = "Product",
                                Template = "footer-column",
                                CssClass = "footer-col",
                                Meta = new Dictionary<string, object?> { ["tone"] = "muted" },
                                Items = new[]
                                {
                                    new MenuItemSpec { Title = "Docs", Url = "/docs/" }
                                }
                            }
                        }
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var output = Path.Combine(root, "_site");
            WebSiteBuilder.Build(spec, plan, output);

            var navPath = Path.Combine(output, "data", "site-nav.json");
            Assert.True(File.Exists(navPath));

            using var navDoc = JsonDocument.Parse(File.ReadAllText(navPath));
            var rootElement = navDoc.RootElement;

            var menuModel = rootElement.GetProperty("menuModels")[0];
            Assert.Equal("menu-pill", menuModel.GetProperty("template").GetString());
            Assert.Equal("menu-main", menuModel.GetProperty("class").GetString());
            Assert.Equal("primary", menuModel.GetProperty("meta").GetProperty("variant").GetString());

            var region = rootElement.GetProperty("regions")[0];
            Assert.Equal("cluster", region.GetProperty("template").GetString());
            Assert.Equal("hdr-right", region.GetProperty("class").GetString());
            Assert.Equal("mobile", region.GetProperty("meta").GetProperty("collapse").GetString());

            var footer = rootElement.GetProperty("footerModel");
            Assert.Equal("footer-grid", footer.GetProperty("template").GetString());
            Assert.Equal("site-footer", footer.GetProperty("class").GetString());
            Assert.Equal(3, footer.GetProperty("meta").GetProperty("columns").GetInt32());

            var column = footer.GetProperty("columns")[0];
            Assert.Equal("footer-column", column.GetProperty("template").GetString());
            Assert.Equal("footer-col", column.GetProperty("class").GetString());
            Assert.Equal("muted", column.GetProperty("meta").GetProperty("tone").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
