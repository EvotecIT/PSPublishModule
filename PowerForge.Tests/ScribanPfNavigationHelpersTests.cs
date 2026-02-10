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
}
