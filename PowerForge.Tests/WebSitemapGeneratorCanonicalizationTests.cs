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
    public void Generate_ExcludesUtilityAndNoIndexHtml_ByDefault()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-sitemap-exclude-defaults-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "api-fragments"));
            Directory.CreateDirectory(Path.Combine(root, "search"));

            File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><title>home</title>");
            File.WriteAllText(Path.Combine(root, "faq.scripts.html"), "<!doctype html><title>scripts</title>");
            File.WriteAllText(Path.Combine(root, "api-fragments", "header.html"), "<header>fragment</header>");
            File.WriteAllText(
                Path.Combine(root, "search", "index.html"),
                "<!doctype html><head><meta name=robots content=noindex /><title>Search</title></head><body>search</body>");

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
            Assert.DoesNotContain("https://example.test/faq.scripts.html", locs, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://example.test/api-fragments/header.html", locs, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://example.test/search/", locs, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_DoesNotIncludeGeneratedHtmlRoute_WhenXmlInclusionDisabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-sitemap-generated-html-skip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "sitemap"));
            File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><title>home</title>");
            File.WriteAllText(Path.Combine(root, "sitemap", "index.html"), "<!doctype html><title>existing sitemap</title>");

            var result = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                IncludeTextFiles = false,
                GenerateHtml = true,
                IncludeGeneratedHtmlRouteInXml = false
            });

            var doc = XDocument.Load(result.OutputPath);
            var ns = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
            var locs = doc
                .Descendants(ns + "url")
                .Select(url => url.Element(ns + "loc")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            Assert.Contains("https://example.test/", locs, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://example.test/sitemap/", locs, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_CanIncludeNoIndexAndDisableDefaultExcludes_WhenRequested()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-sitemap-exclude-override-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "api-fragments"));
            Directory.CreateDirectory(Path.Combine(root, "search"));

            File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><title>home</title>");
            File.WriteAllText(Path.Combine(root, "faq.scripts.html"), "<!doctype html><title>scripts</title>");
            File.WriteAllText(Path.Combine(root, "api-fragments", "header.html"), "<header>fragment</header>");
            File.WriteAllText(
                Path.Combine(root, "search", "index.html"),
                "<!doctype html><head><meta name=\"robots\" content=\"noindex\" /><title>Search</title></head><body>search</body>");

            var result = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                IncludeTextFiles = false,
                IncludeNoIndexHtml = true,
                UseDefaultExcludePatterns = false
            });

            var doc = XDocument.Load(result.OutputPath);
            var ns = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
            var locs = doc
                .Descendants(ns + "url")
                .Select(url => url.Element(ns + "loc")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            Assert.Contains("https://example.test/faq.scripts.html", locs, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("https://example.test/api-fragments/header.html", locs, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("https://example.test/search/", locs, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_CanEmitNewsSitemapAndSitemapIndex()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-sitemap-news-index-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var result = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                IncludeHtmlFiles = false,
                IncludeTextFiles = false,
                Entries = new[]
                {
                    new WebSitemapEntry
                    {
                        Path = "/news/launch/",
                        Title = "Launch Announcement",
                        LastModified = "2026-02-18"
                    },
                    new WebSitemapEntry
                    {
                        Path = "/docs/",
                        Title = "Docs"
                    }
                },
                NewsSitemap = new WebSitemapNewsOptions
                {
                    PathPatterns = new[] { "/news/**" },
                    PublicationName = "Example Product",
                    PublicationLanguage = "en"
                },
                SitemapIndexPath = string.Empty
            });

            Assert.True(File.Exists(result.OutputPath));
            Assert.True(File.Exists(result.NewsOutputPath));
            Assert.True(File.Exists(result.IndexOutputPath));

            var newsNs = XNamespace.Get("http://www.google.com/schemas/sitemap-news/0.9");
            var sitemapNs = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
            var newsDoc = XDocument.Load(result.NewsOutputPath!);
            var newsUrls = newsDoc.Descendants(sitemapNs + "url").ToArray();
            Assert.Single(newsUrls);
            Assert.Contains("https://example.test/news/launch/",
                newsUrls[0].Element(sitemapNs + "loc")?.Value,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(
                "Example Product",
                newsUrls[0]
                    .Element(newsNs + "news")?
                    .Element(newsNs + "publication")?
                    .Element(newsNs + "name")?
                    .Value);

            var indexDoc = XDocument.Load(result.IndexOutputPath!);
            var indexedLocs = indexDoc
                .Descendants(sitemapNs + "sitemap")
                .Select(node => node.Element(sitemapNs + "loc")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            Assert.Contains("https://example.test/sitemap.xml", indexedLocs, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("https://example.test/sitemap-news.xml", indexedLocs, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_NewsSitemap_DefaultPatterns_IncludeLocalizedNewsRoute()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-sitemap-news-default-patterns-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var result = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                IncludeHtmlFiles = false,
                IncludeTextFiles = false,
                Entries = new[]
                {
                    new WebSitemapEntry { Path = "/pl/news/release/", Title = "Release" },
                    new WebSitemapEntry { Path = "/blog/update/", Title = "Blog Update" }
                },
                NewsSitemap = new WebSitemapNewsOptions()
            });

            Assert.True(File.Exists(result.NewsOutputPath));

            var sitemapNs = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
            var doc = XDocument.Load(result.NewsOutputPath!);
            var locs = doc.Descendants(sitemapNs + "loc")
                .Select(node => node.Value)
                .ToArray();

            Assert.Contains("https://example.test/pl/news/release/", locs, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://example.test/blog/update/", locs, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
