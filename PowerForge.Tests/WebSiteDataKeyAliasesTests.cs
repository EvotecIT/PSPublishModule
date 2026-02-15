using System;
using System.IO;
using PowerForge.Web;
using Xunit;

public class WebSiteDataKeyAliasesTests
{
    [Fact]
    public void Build_LoadsHyphenatedDataFile_AsUnderscoreAlias()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-data-alias-hyphen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "content", "pages"));
            File.WriteAllText(Path.Combine(root, "content", "pages", "index.md"),
                """
                ---
                title: Home
                ---
                Hi
                """);

            Directory.CreateDirectory(Path.Combine(root, "data"));
            File.WriteAllText(Path.Combine(root, "data", "code-examples.json"),
                """
                { "tabs": [ { "label": "Hello" } ] }
                """);

            var themeRoot = Path.Combine(root, "themes", "t");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            File.WriteAllText(Path.Combine(themeRoot, "theme.manifest.json"),
                """
                {
                  "schemaVersion": 2,
                  "contractVersion": 2,
                  "name": "t",
                  "engine": "scriban",
                  "layoutsPath": "layouts",
                  "defaultLayout": "base"
                }
                """);
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "base.html"),
                """
                <!doctype html>
                <html><body>
                {{ for item in data.code_examples.tabs }}{{ item.label }}{{ end }}
                </body></html>
                """);

            var spec = new SiteSpec
            {
                Name = "Test",
                BaseUrl = "https://example.com",
                ContentRoot = "content",
                ThemesRoot = "themes",
                DataRoot = "data",
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
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var outDir = Path.Combine(root, "_site");
            WebSiteBuilder.Build(spec, plan, outDir);

            var html = File.ReadAllText(Path.Combine(outDir, "index.html"));
            Assert.Contains("Hello", html, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Build_LoadsDottedDataFile_AsUnderscoreAlias()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-data-alias-dot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "content", "pages"));
            File.WriteAllText(Path.Combine(root, "content", "pages", "index.md"),
                """
                ---
                title: Home
                ---
                Hi
                """);

            Directory.CreateDirectory(Path.Combine(root, "data"));
            File.WriteAllText(Path.Combine(root, "data", "sitemap.entries.json"),
                """
                { "count": 3 }
                """);

            var themeRoot = Path.Combine(root, "themes", "t");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            File.WriteAllText(Path.Combine(themeRoot, "theme.manifest.json"),
                """
                {
                  "schemaVersion": 2,
                  "contractVersion": 2,
                  "name": "t",
                  "engine": "scriban",
                  "layoutsPath": "layouts",
                  "defaultLayout": "base"
                }
                """);
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "base.html"),
                """
                <!doctype html>
                <html><body>
                {{ data.sitemap_entries.count }}
                </body></html>
                """);

            var spec = new SiteSpec
            {
                Name = "Test",
                BaseUrl = "https://example.com",
                ContentRoot = "content",
                ThemesRoot = "themes",
                DataRoot = "data",
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
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var outDir = Path.Combine(root, "_site");
            WebSiteBuilder.Build(spec, plan, outDir);

            var html = File.ReadAllText(Path.Combine(outDir, "index.html"));
            Assert.Contains("3", html, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // ignore cleanup failures in tests
        }
    }
}

