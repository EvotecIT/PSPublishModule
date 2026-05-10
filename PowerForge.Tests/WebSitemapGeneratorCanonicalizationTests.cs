using System;
using System.Globalization;
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
    public void Generate_WritesSitemapCss_AndEmitsProcessingInstruction()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-sitemap-browser-style-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><title>home</title>");

            var result = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                IncludeTextFiles = false
            });

            Assert.True(File.Exists(Path.Combine(root, "sitemap.css")));
            var doc = XDocument.Load(result.OutputPath);
            var stylesheet = doc.Nodes().OfType<XProcessingInstruction>().FirstOrDefault(node => node.Target == "xml-stylesheet");
            Assert.NotNull(stylesheet);
            Assert.Contains("href=\"/sitemap.css\"", stylesheet!.Data, StringComparison.OrdinalIgnoreCase);

            var css = File.ReadAllText(Path.Combine(root, "sitemap.css"));
            Assert.Contains("urlset::before", css, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("counter(sitemap-entry)", css, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("link::before", css, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_UsesHtmlFreshnessSignals_AndOmitsUnknownLastmod()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-sitemap-html-freshness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "about"));
        Directory.CreateDirectory(Path.Combine(root, "timeline"));
        Directory.CreateDirectory(Path.Combine(root, "semantic-time"));

        try
        {
            File.WriteAllText(
                Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <title>home</title>
                <meta property="article:published_time" content="2020-01-01T00:00:00Z" />
                <meta property="article:modified_time" content="2024-03-04T05:06:07Z" />
                <script>window.example = {"dateModified":"2099-01-01T00:00:00Z"};</script>
                """);
            File.WriteAllText(Path.Combine(root, "about", "index.html"), "<!doctype html><title>about</title>");
            File.WriteAllText(Path.Combine(root, "timeline", "index.html"), "<!doctype html><title>timeline</title><time datetime=\"2030-01-01T00:00:00Z\">event</time>");
            File.WriteAllText(Path.Combine(root, "semantic-time", "index.html"), "<!doctype html><title>semantic</title><time itemprop=\"dateModified\" datetime=\"2024-04-05T06:07:08Z\">updated</time>");

            var result = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                IncludeTextFiles = false
            });

            var doc = XDocument.Load(result.OutputPath);
            var ns = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
            var urls = doc.Descendants(ns + "url")
                .ToDictionary(
                    url => url.Element(ns + "loc")?.Value ?? string.Empty,
                    url => url,
                    StringComparer.OrdinalIgnoreCase);

            Assert.Equal("2024-03-04T05:06:07.000Z", urls["https://example.test/"].Element(ns + "lastmod")?.Value);
            Assert.Null(urls["https://example.test/about/"].Element(ns + "lastmod"));
            Assert.Null(urls["https://example.test/timeline/"].Element(ns + "lastmod"));
            Assert.Equal("2024-04-05T06:07:08.000Z", urls["https://example.test/semantic-time/"].Element(ns + "lastmod")?.Value);
            Assert.Equal(2, result.LastModifiedCount);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_MergesPowerForgeSitemapMetadata_ByDefault()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-sitemap-generated-metadata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "_powerforge"));
        Directory.CreateDirectory(Path.Combine(root, "secret"));
        Directory.CreateDirectory(Path.Combine(root, "flagged"));
        Directory.CreateDirectory(Path.Combine(root, "custom"));

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><title>home</title>");
            File.WriteAllText(Path.Combine(root, "secret", "index.html"), "<!doctype html><meta name=\"robots\" content=\"noindex,follow\"><title>secret</title>");
            File.WriteAllText(Path.Combine(root, "flagged", "index.html"), "<!doctype html><title>flagged</title>");
            File.WriteAllText(Path.Combine(root, "custom", "index.htm"), "<!doctype html><title>custom suffix</title>");
            File.WriteAllText(
                Path.Combine(root, "_powerforge", "sitemap-entries.json"),
                """
                {
                  "schemaVersion": 1,
                  "noIndexTrusted": true,
                  "htmlRoutesTrusted": true,
                  "entries": [
                    { "path": "/", "lastModified": "2021-02-03T04:05:06.000Z" },
                    { "path": "/secret/", "lastModified": "2022-02-03T04:05:06.000Z" },
                    { "path": "/flagged/", "lastModified": "2022-03-03T04:05:06.000Z", "noIndex": true },
                    { "path": "/custom/", "lastModified": "2022-04-03T04:05:06.000Z" }
                  ]
                }
                """);

            var result = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                IncludeHtmlFiles = false,
                IncludeTextFiles = false
            });

            var doc = XDocument.Load(result.OutputPath);
            var ns = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
            var locs = doc.Descendants(ns + "url")
                .Select(url => url.Element(ns + "loc")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
            var home = doc.Descendants(ns + "url")
                .Single(url => string.Equals(url.Element(ns + "loc")?.Value, "https://example.test/", StringComparison.OrdinalIgnoreCase));

            Assert.Contains("https://example.test/", locs, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("https://example.test/secret/", locs, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://example.test/flagged/", locs, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("https://example.test/custom/", locs, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("2021-02-03T04:05:06.000Z", home.Element(ns + "lastmod")?.Value);
            Assert.Equal(3, result.LastModifiedCount);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_IgnoresMalformedGeneratedSitemapMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-sitemap-generated-metadata-bad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "_powerforge"));

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><title>home</title>");
            File.WriteAllText(Path.Combine(root, "_powerforge", "sitemap-entries.json"), """{ "entries": "not an array" }""");

            var result = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                IncludeTextFiles = false
            });

            var doc = XDocument.Load(result.OutputPath);
            var ns = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
            Assert.Contains(
                doc.Descendants(ns + "loc").Select(static loc => loc.Value),
                loc => string.Equals(loc, "https://example.test/", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_Warns_WhenMostLastmodValuesAreBuildDate()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-sitemap-lastmod-warning-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var result = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                IncludeHtmlFiles = false,
                IncludeTextFiles = false,
                Entries = Enumerable.Range(1, 10)
                    .Select(i => new WebSitemapEntry { Path = $"/page-{i}/", LastModified = today })
                    .ToArray()
            });

            Assert.Equal(10, result.LastModifiedCount);
            Assert.Contains(result.Warnings, warning => warning.Contains("suspicious", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_CanDisableBrowserStylesheet()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-sitemap-browser-style-off-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><title>home</title>");

            var result = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                IncludeTextFiles = false,
                GenerateBrowserStylesheet = false
            });

            Assert.False(File.Exists(Path.Combine(root, "sitemap.css")));
            var doc = XDocument.Load(result.OutputPath);
            Assert.DoesNotContain(doc.Nodes().OfType<XProcessingInstruction>(), node => node.Target == "xml-stylesheet");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_EmitsOfficialSchemaLocationHeaders()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-sitemap-schema-location-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><title>home</title>");

            var result = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                IncludeTextFiles = false,
                GenerateBrowserStylesheet = false,
                SitemapIndexPath = string.Empty
            });

            var xsiNs = XNamespace.Get("http://www.w3.org/2001/XMLSchema-instance");
            var sitemapDoc = XDocument.Load(result.OutputPath);
            Assert.Equal(
                "http://www.sitemaps.org/schemas/sitemap/0.9 http://www.sitemaps.org/schemas/sitemap/0.9/sitemap.xsd",
                sitemapDoc.Root?.Attribute(xsiNs + "schemaLocation")?.Value);

            var indexDoc = XDocument.Load(result.IndexOutputPath!);
            Assert.Equal(
                "http://www.sitemaps.org/schemas/sitemap/0.9 http://www.sitemaps.org/schemas/sitemap/0.9/siteindex.xsd",
                indexDoc.Root?.Attribute(xsiNs + "schemaLocation")?.Value);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_CanUseCustomBrowserStylesheetHref()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-sitemap-browser-style-custom-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><title>home</title>");

            var result = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                IncludeTextFiles = false,
                BrowserStylesheetHref = "assets/sitemap-view.css"
            });

            Assert.True(File.Exists(Path.Combine(root, "assets", "sitemap-view.css")));
            var doc = XDocument.Load(result.OutputPath);
            var stylesheet = doc.Nodes().OfType<XProcessingInstruction>().FirstOrDefault(node => node.Target == "xml-stylesheet");
            Assert.NotNull(stylesheet);
            Assert.Contains("href=\"/assets/sitemap-view.css\"", stylesheet!.Data, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_CanUseAbsoluteBrowserStylesheetHref()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-sitemap-browser-style-absolute-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><title>home</title>");

            var result = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                IncludeTextFiles = false,
                BrowserStylesheetHref = "https://cdn.example.test/sitemap.css"
            });

            Assert.False(File.Exists(Path.Combine(root, "sitemap.css")));
            var doc = XDocument.Load(result.OutputPath);
            var stylesheet = doc.Nodes().OfType<XProcessingInstruction>().FirstOrDefault(node => node.Target == "xml-stylesheet");
            Assert.NotNull(stylesheet);
            Assert.Contains("href=\"https://cdn.example.test/sitemap.css\"", stylesheet!.Data, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_RejectsUnsafeBrowserStylesheetHref()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-sitemap-browser-style-unsafe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><title>home</title>");

            Assert.Throws<ArgumentException>(() => WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                IncludeTextFiles = false,
                BrowserStylesheetHref = "/sitemap.css\" type=\"text/xsl"
            }));

            Assert.Throws<ArgumentException>(() => WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                IncludeTextFiles = false,
                BrowserStylesheetHref = "javascript:alert(1)"
            }));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_CollapsesLegacyApiHtmlAliases_FromMergedApiSitemap()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-sitemap-api-merge-alias-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var apiSitemapPath = Path.Combine(root, "api-sitemap.xml");
            File.WriteAllText(
                apiSitemapPath,
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc>https://example.test/api/sample-type.html</loc></url>
                  <url><loc>https://example.test/api/types/legacy-flat.html</loc></url>
                </urlset>
                """);

            var result = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                IncludeHtmlFiles = false,
                IncludeTextFiles = false,
                ApiSitemapPath = apiSitemapPath
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

            // Keep simple-template API routes as flat .html.
            Assert.Contains("https://example.test/api/types/legacy-flat.html", locs, StringComparer.OrdinalIgnoreCase);
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
            Assert.True(File.Exists(Path.Combine(root, "sitemap.css")));

            var newsNs = XNamespace.Get("http://www.google.com/schemas/sitemap-news/0.9");
            var sitemapNs = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
            var newsDoc = XDocument.Load(result.NewsOutputPath!);
            var newsStylesheet = newsDoc.Nodes().OfType<XProcessingInstruction>().FirstOrDefault(node => node.Target == "xml-stylesheet");
            Assert.NotNull(newsStylesheet);
            Assert.Contains("href=\"/sitemap.css\"", newsStylesheet!.Data, StringComparison.OrdinalIgnoreCase);

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
            var stylesheet = indexDoc.Nodes().OfType<XProcessingInstruction>().FirstOrDefault(node => node.Target == "xml-stylesheet");
            Assert.NotNull(stylesheet);
            Assert.Contains("href=\"/sitemap.css\"", stylesheet!.Data, StringComparison.OrdinalIgnoreCase);

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

    [Fact]
    public void Generate_CanEmitImageAndVideoSitemaps_FromHtmlDiscovery()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-sitemap-media-html-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "showcase", "clip"));
            File.WriteAllText(Path.Combine(root, "showcase", "clip", "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Clip</title></head>
                <body>
                  <img src="/assets/hero.png" />
                  <video src="/media/demo.mp4"></video>
                </body>
                </html>
                """);

            var result = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                IncludeTextFiles = false,
                ImageSitemap = new WebSitemapImageOptions
                {
                    PathPatterns = new[] { "/showcase/**" }
                },
                VideoSitemap = new WebSitemapVideoOptions
                {
                    PathPatterns = new[] { "/showcase/**" }
                }
            });

            Assert.True(File.Exists(result.ImageOutputPath));
            Assert.True(File.Exists(result.VideoOutputPath));

            var sitemapNs = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
            var imageNs = XNamespace.Get("http://www.google.com/schemas/sitemap-image/1.1");
            var videoNs = XNamespace.Get("http://www.google.com/schemas/sitemap-video/1.1");

            var imageDoc = XDocument.Load(result.ImageOutputPath!);
            var imageStylesheet = imageDoc.Nodes().OfType<XProcessingInstruction>().FirstOrDefault(node => node.Target == "xml-stylesheet");
            Assert.NotNull(imageStylesheet);
            Assert.Contains("href=\"/sitemap.css\"", imageStylesheet!.Data, StringComparison.OrdinalIgnoreCase);

            Assert.Contains(
                "https://example.test/assets/hero.png",
                imageDoc.Descendants(imageNs + "loc").Select(node => node.Value),
                StringComparer.OrdinalIgnoreCase);

            var videoDoc = XDocument.Load(result.VideoOutputPath!);
            var videoStylesheet = videoDoc.Nodes().OfType<XProcessingInstruction>().FirstOrDefault(node => node.Target == "xml-stylesheet");
            Assert.NotNull(videoStylesheet);
            Assert.Contains("href=\"/sitemap.css\"", videoStylesheet!.Data, StringComparison.OrdinalIgnoreCase);

            Assert.Contains(
                "https://example.test/media/demo.mp4",
                videoDoc.Descendants(videoNs + "content_loc").Select(node => node.Value),
                StringComparer.OrdinalIgnoreCase);
            Assert.Contains(
                "https://example.test/showcase/clip/",
                videoDoc.Descendants(sitemapNs + "loc").Select(node => node.Value),
                StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_SitemapIndex_CanReferenceNewsImageAndVideoMaps()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-sitemap-index-specialized-" + Guid.NewGuid().ToString("N"));
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
                        Path = "/news/release/",
                        Title = "Release",
                        ImageUrls = new[] { "/assets/release.png" },
                        VideoUrls = new[] { "/media/release.mp4" }
                    }
                },
                NewsSitemap = new WebSitemapNewsOptions
                {
                    PathPatterns = new[] { "/news/**" }
                },
                ImageSitemap = new WebSitemapImageOptions
                {
                    PathPatterns = new[] { "/news/**" }
                },
                VideoSitemap = new WebSitemapVideoOptions
                {
                    PathPatterns = new[] { "/news/**" }
                },
                SitemapIndexPath = string.Empty
            });

            Assert.True(File.Exists(result.IndexOutputPath));
            var sitemapNs = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
            var indexDoc = XDocument.Load(result.IndexOutputPath!);
            var indexedLocs = indexDoc
                .Descendants(sitemapNs + "sitemap")
                .Select(node => node.Element(sitemapNs + "loc")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            Assert.Contains("https://example.test/sitemap.xml", indexedLocs, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("https://example.test/sitemap-news.xml", indexedLocs, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("https://example.test/sitemap-images.xml", indexedLocs, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("https://example.test/sitemap-videos.xml", indexedLocs, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
