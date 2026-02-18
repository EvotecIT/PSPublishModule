using System;
using System.IO;
using System.Linq;
using Xunit;
using PowerForge.Web;

public class ScribanPfNavigationHelpersTests
{
    [Fact]
    public void Build_RendersPfNavHelpers_InScribanTheme()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-scriban-nav-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "content", "pages"));
            File.WriteAllText(Path.Combine(root, "content", "pages", "index.md"),
                """
                ---
                title: Home
                ---

                # Hello
                """);

            var themeRoot = Path.Combine(root, "themes", "t");
            Directory.CreateDirectory(themeRoot);
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            Directory.CreateDirectory(Path.Combine(themeRoot, "partials"));

            File.WriteAllText(Path.Combine(themeRoot, "theme.manifest.json"),
                """
                {
                  "schemaVersion": 2,
                  "contractVersion": 2,
                  "name": "t",
                  "engine": "scriban",
                  "layoutsPath": "layouts",
                  "partialsPath": "partials",
                  "defaultLayout": "base"
                }
                """);

            File.WriteAllText(Path.Combine(themeRoot, "layouts", "base.html"),
                """
                <!doctype html>
                <html>
                <head><title>{{ page.title }}</title></head>
                <body>
                  {{ include "header" }}
                  <main>{{ content }}</main>
                </body>
                </html>
                """);

            File.WriteAllText(Path.Combine(themeRoot, "partials", "header.html"),
                """
                <header>
                  <nav class="links">{{ pf.nav_links "main" }}</nav>
                  <div class="actions">{{ pf.nav_actions }}</div>
                </header>
                """);

            var spec = new SiteSpec
            {
                Name = "Test",
                BaseUrl = "https://example.com",
                ContentRoot = "content",
                ThemesRoot = "themes",
                DefaultTheme = "t",
                ThemeEngine = "scriban",
                TrailingSlash = TrailingSlashMode.Always,
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "pages",
                        Input = "content/pages",
                        Output = "/",
                        DefaultLayout = "base",
                        Include = new[] { "*.md" }
                    }
                },
                LinkRules = new LinkRulesSpec
                {
                    ExternalTarget = "_blank",
                    ExternalRel = "noopener"
                },
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus = new[]
                    {
                        new MenuSpec
                        {
                            Name = "main",
                            Items = new[]
                            {
                                new MenuItemSpec { Title = "Home", Url = "/" },
                                new MenuItemSpec { Title = "Docs", Url = "/docs/" }
                            }
                        }
                    },
                    Actions = new[]
                    {
                        new MenuItemSpec
                        {
                            Title = "GitHub",
                            Url = "https://github.com/example/repo",
                            External = true,
                            CssClass = "nav-icon"
                        }
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var outDir = Path.Combine(root, "_site");
            WebSiteBuilder.Build(spec, plan, outDir);

            var indexHtml = File.ReadAllText(Path.Combine(outDir, "index.html"));
            Assert.Contains("<nav class=\"links\">", indexHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"/\"", indexHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"/docs/\"", indexHtml, StringComparison.OrdinalIgnoreCase);

            // Current route is "/", so Home should be marked active.
            Assert.Contains("is-active", indexHtml, StringComparison.OrdinalIgnoreCase);

            // External action should pick up link rules.
            Assert.Contains("href=\"https://github.com/example/repo\"", indexHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("target=\"_blank\"", indexHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("rel=\"noopener\"", indexHtml, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch
            {
                // ignore cleanup failures in tests
            }
        }
    }

    [Fact]
    public void Build_RendersPfEditorialCards_InScribanTheme()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-scriban-editorial-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var blogRoot = Path.Combine(root, "content", "blog");
            Directory.CreateDirectory(blogRoot);
            File.WriteAllText(Path.Combine(blogRoot, "_index.md"),
                """
                ---
                title: Blog
                description: Latest updates
                ---

                # Blog
                """);

            File.WriteAllText(Path.Combine(blogRoot, "first-post.md"),
                """
                ---
                title: First post
                description: First description for helper output.
                date: 2026-01-03
                tags:
                  - release
                  - update
                meta.social_image: /images/first-post.png
                ---

                # First
                """);

            File.WriteAllText(Path.Combine(blogRoot, "second-post.md"),
                """
                ---
                title: Second post
                description: Another update
                date: 2026-01-04
                tags:
                  - notes
                ---

                # Second
                """);

            var themeRoot = Path.Combine(root, "themes", "t");
            Directory.CreateDirectory(themeRoot);
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            Directory.CreateDirectory(Path.Combine(themeRoot, "partials"));

            File.WriteAllText(Path.Combine(themeRoot, "theme.manifest.json"),
                """
                {
                  "schemaVersion": 2,
                  "contractVersion": 2,
                  "name": "t",
                  "engine": "scriban",
                  "layoutsPath": "layouts",
                  "partialsPath": "partials",
                  "defaultLayout": "base"
                }
                """);

            File.WriteAllText(Path.Combine(themeRoot, "layouts", "base.html"),
                """
                <!doctype html>
                <html>
                <head><title>{{ page.title }}</title></head>
                <body>
                  {{ include "header" }}
                  <main>
                    {{ pf.editorial_cards 0 120 true true true true "4:3" "/images/fallback.png" }}
                    {{ pf.editorial_pager "Newer" "Older" }}
                  </main>
                  {{ include "footer" }}
                </body>
                </html>
                """);

            File.WriteAllText(Path.Combine(themeRoot, "layouts", "post.html"),
                """
                <!doctype html>
                <html>
                <head><title>{{ page.title }}</title></head>
                <body>{{ content }}</body>
                </html>
                """);

            File.WriteAllText(Path.Combine(themeRoot, "partials", "header.html"), "<header>Header</header>");
            File.WriteAllText(Path.Combine(themeRoot, "partials", "footer.html"), "<footer>Footer</footer>");

            var spec = new SiteSpec
            {
                Name = "Editorial",
                BaseUrl = "https://example.com",
                ContentRoot = "content",
                ThemesRoot = "themes",
                DefaultTheme = "t",
                ThemeEngine = "scriban",
                TrailingSlash = TrailingSlashMode.Always,
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "blog",
                        Input = "content/blog",
                        Output = "/blog",
                        DefaultLayout = "post",
                        ListLayout = "base",
                        PageSize = 1,
                        Include = new[] { "*.md", "**/*.md" }
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var outDir = Path.Combine(root, "_site");
            WebSiteBuilder.Build(spec, plan, outDir);

            var blogHtml = File.ReadAllText(Path.Combine(outDir, "blog", "index.html"));
            Assert.Contains("pf-editorial-grid", blogHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"/blog/first-post/\"", blogHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("pf-editorial-card-image", blogHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("/images/first-post.png", blogHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("aspect-ratio: 4 / 3;", blogHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<time datetime=\"2026-01-03\">2026-01-03</time>", blogHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<span class=\"pf-chip\">release</span>", blogHtml, StringComparison.OrdinalIgnoreCase);

            var blogPage2Html = File.ReadAllText(Path.Combine(outDir, "blog", "page", "2", "index.html"));
            Assert.Contains("/images/fallback.png", blogPage2Html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(">Newer<", blogPage2Html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"/blog/\"", blogPage2Html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(">Older<", blogPage2Html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch
            {
                // ignore cleanup failures in tests
            }
        }
    }
}
