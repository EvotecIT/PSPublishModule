using System.Text.Json;
using System.Text.RegularExpressions;
using PowerForge.Web;

namespace PowerForge.Tests;

public class WebSiteSeoTemplatesTests
{
    [Fact]
    public void Build_AppliesSiteSeoTemplates_AndEmitsSeoPreview()
    {
        var root = CreateTempRoot("pf-web-seo-templates-site-");
        try
        {
            WritePage(root, "content/pages/index.md",
                """
                ---
                title: Home
                description: Home page.
                slug: index
                ---

                Welcome to docs.
                """);

            var spec = new SiteSpec
            {
                Name = "Example Site",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                Seo = new SeoSpec
                {
                    Templates = new SeoTemplatesSpec
                    {
                        Title = "{title} | {site}",
                        Description = "{title} :: {site} :: {lang}"
                    }
                },
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

            var outPath = Build(root, spec);
            var html = File.ReadAllText(Path.Combine(outPath, "index.html"));

            Assert.Contains("<title>Home | Example Site</title>", html, StringComparison.Ordinal);
            Assert.Equal("Home :: Example Site :: en", ReadMetaDescriptionContent(html));

            var previewPath = Path.Combine(outPath, "_powerforge", "seo-preview.json");
            Assert.True(File.Exists(previewPath), "Expected _powerforge/seo-preview.json to be emitted.");
            using var previewDoc = JsonDocument.Parse(File.ReadAllText(previewPath));
            var pages = previewDoc.RootElement.GetProperty("pages");
            Assert.True(pages.GetArrayLength() > 0, "Expected at least one SEO preview page entry.");

            var homePage = pages.EnumerateArray()
                .First(page =>
                    page.TryGetProperty("outputPath", out var path) &&
                    string.Equals(path.GetString(), "/", StringComparison.OrdinalIgnoreCase));

            Assert.Equal("Home | Example Site", homePage.GetProperty("seoTitle").GetString());
            Assert.Equal("Home :: Example Site :: en", homePage.GetProperty("seoDescription").GetString());
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Build_CollectionSeoTemplateOverridesSite_AndPageOverrideWinsForDescription()
    {
        var root = CreateTempRoot("pf-web-seo-templates-collection-");
        try
        {
            WritePage(root, "content/blog/release.md",
                """
                ---
                title: Release 1
                description: Default release description.
                slug: release-1
                date: 2026-02-18
                meta.seo_description: "Custom release snippet."
                ---

                Release body.
                """);

            var spec = new SiteSpec
            {
                Name = "Example Site",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                Seo = new SeoSpec
                {
                    Templates = new SeoTemplatesSpec
                    {
                        Title = "{title} | {site}",
                        Description = "{title} | {site}"
                    }
                },
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "blog",
                        Input = "content/blog",
                        Output = "/blog",
                        Seo = new SeoSpec
                        {
                            Templates = new SeoTemplatesSpec
                            {
                                Title = "{title} ({date}) | {site}",
                                Description = "{collection} | {title} | {lang}"
                            }
                        }
                    }
                }
            };

            var outPath = Build(root, spec);
            var html = File.ReadAllText(Path.Combine(outPath, "blog", "release-1", "index.html"));

            Assert.Contains("<title>Release 1 (2026-02-18) | Example Site</title>", html, StringComparison.Ordinal);
            Assert.Contains("meta name=\"description\" content=\"Custom release snippet.\"", html, StringComparison.Ordinal);

            var previewPath = Path.Combine(outPath, "_powerforge", "seo-preview.json");
            using var previewDoc = JsonDocument.Parse(File.ReadAllText(previewPath));
            var page = previewDoc.RootElement
                .GetProperty("pages")
                .EnumerateArray()
                .First(entry =>
                    entry.TryGetProperty("outputPath", out var path) &&
                    (path.GetString() ?? string.Empty).Contains("release-1", StringComparison.OrdinalIgnoreCase));

            Assert.Equal("{title} ({date}) | {site}", page.GetProperty("titleTemplate").GetString());
            Assert.Equal("Release 1 (2026-02-18) | Example Site", page.GetProperty("seoTitle").GetString());
            Assert.Equal("Custom release snippet.", page.GetProperty("seoDescription").GetString());
        }
        finally
        {
            Cleanup(root);
        }
    }

    private static string Build(string root, SiteSpec spec)
    {
        var configPath = Path.Combine(root, "site.json");
        File.WriteAllText(configPath, "{}");
        var plan = WebSitePlanner.Plan(spec, configPath);
        var outPath = Path.Combine(root, "_site");
        WebSiteBuilder.Build(spec, plan, outPath);
        return outPath;
    }

    private static void WritePage(string root, string relativePath, string markdown)
    {
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(fullPath, markdown);
    }

    private static string CreateTempRoot(string prefix)
    {
        var root = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string ReadMetaDescriptionContent(string html)
    {
        var match = Regex.Match(
            html,
            "<meta\\s+name=\"description\"\\s+content=\"([^\"]*)\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Assert.True(match.Success, "Expected description meta tag.");
        return System.Web.HttpUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static void Cleanup(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}
