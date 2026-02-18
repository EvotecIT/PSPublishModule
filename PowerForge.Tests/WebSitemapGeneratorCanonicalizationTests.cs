using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using PowerForge.Web;
using Xunit;

public class WebSitemapGeneratorCanonicalizationTests
{
    [Fact]
    public void Generate_CollapsesHtmlAlias_WhenSlashRouteAlsoExists()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-sitemap-canonicalize-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "api", "sample-type"));
            File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><title>home</title>");
            File.WriteAllText(Path.Combine(root, "api", "sample-type.html"), "<!doctype html><title>type-html</title>");
            File.WriteAllText(Path.Combine(root, "api", "sample-type", "index.html"), "<!doctype html><title>type-index</title>");
            File.WriteAllText(Path.Combine(root, "privacy.html"), "<!doctype html><title>privacy</title>");

            var result = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                IncludeTextFiles = false
            });

            var doc = XDocument.Load(result.OutputPath);
            var ns = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
            var locs = doc
                .Descendants(ns + "url")
                .Select(url => url.Element(ns + "loc")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            Assert.Contains("https://example.test/api/sample-type/", locs, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://example.test/api/sample-type.html", locs, StringComparer.OrdinalIgnoreCase);

            // Standalone .html routes with no slash alias should remain as-is.
            Assert.Contains("https://example.test/privacy.html", locs, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GenerateHtmlSitemap_DisambiguatesDuplicateTitles_WithPathSuffix()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-sitemap-title-dedupe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><title>Home</title>");
            File.WriteAllText(Path.Combine(root, "a.html"), "<!doctype html><title>Otp - CodeGlyphX API Reference</title>");
            File.WriteAllText(Path.Combine(root, "b.html"), "<!doctype html><title>Otp - CodeGlyphX API Reference</title>");

            var result = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                IncludeTextFiles = false,
                GenerateHtml = true
            });

            Assert.True(File.Exists(result.HtmlOutputPath));
            var html = File.ReadAllText(result.HtmlOutputPath!);

            Assert.Contains("Otp - CodeGlyphX API Reference (/a.html)", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Otp - CodeGlyphX API Reference (/b.html)", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_SkipsNoIndexPages_ByDefault()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-sitemap-noindex-skip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><title>Home</title>");
            File.WriteAllText(
                Path.Combine(root, "hidden.html"),
                "<!doctype html><head><meta name=\"robots\" content=\"noindex,nofollow\" /><title>Hidden</title></head>");

            var result = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                IncludeTextFiles = false
            });

            var doc = XDocument.Load(result.OutputPath);
            var ns = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
            var locs = doc
                .Descendants(ns + "url")
                .Select(url => url.Element(ns + "loc")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            Assert.Contains("https://example.test/", locs, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://example.test/hidden.html", locs, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_IncludeNoIndexPages_WhenEnabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-sitemap-noindex-include-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><title>Home</title>");
            File.WriteAllText(
                Path.Combine(root, "hidden.html"),
                "<!doctype html><head><meta name=\"googlebot\" content=\"noindex\" /><title>Hidden</title></head>");

            var result = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                IncludeTextFiles = false,
                IncludeNoIndexPages = true
            });

            var doc = XDocument.Load(result.OutputPath);
            var ns = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
            var locs = doc
                .Descendants(ns + "url")
                .Select(url => url.Element(ns + "loc")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            Assert.Contains("https://example.test/hidden.html", locs, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
