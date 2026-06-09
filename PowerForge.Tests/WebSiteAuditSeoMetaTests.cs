using PowerForge.Web;

namespace PowerForge.Tests;

public class WebSiteAuditSeoMetaTests
{
    [Fact]
    public void Audit_FlagsSeoMetaProblems_WhenEnabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-seo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Home</title>
                  <link rel="canonical" href="/index/" />
                  <link rel="canonical" href="https://example.test/index/" />
                  <meta property="og:title" content="Home" />
                  <meta property="og:url" content="/index/" />
                  <meta property="og:url" content="https://example.test/index/" />
                  <meta name="twitter:card" content="summary_large_image" />
                  <meta name="twitter:image" content="/assets/social/card.png" />
                </head>
                <body>Home</body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(CreateSeoOnlyOptions(root));

            Assert.True(result.Success);
            Assert.Contains(result.Issues, issue => issue.Hint == "seo-duplicate-canonical");
            Assert.Contains(result.Issues, issue => issue.Hint == "seo-duplicate-og-url");
            Assert.Contains(result.Issues, issue => issue.Hint == "seo-og-url-absolute");
            Assert.Contains(result.Issues, issue => issue.Hint == "seo-missing-og-description");
            Assert.Contains(result.Issues, issue => issue.Hint == "seo-missing-og-image");
            Assert.Contains(result.Issues, issue => issue.Hint == "seo-missing-twitter-title");
            Assert.Contains(result.Issues, issue => issue.Hint == "seo-missing-twitter-description");
            Assert.Contains(result.Issues, issue => issue.Hint == "seo-missing-twitter-url");
            Assert.Contains(result.Issues, issue => issue.Hint == "seo-twitter-image-absolute");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_FlagsSitemapNoIndexMismatch_WhenNoIndexRouteIsInSitemap()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-seo-sitemap-noindex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "search"));
            File.WriteAllText(Path.Combine(root, "search", "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Search</title>
                  <meta name="robots" content="noindex,nofollow" />
                </head>
                <body>Search</body>
                </html>
                """);

            File.WriteAllText(Path.Combine(root, "sitemap.xml"),
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc>https://example.test/</loc></url>
                  <url><loc>https://example.test/search/</loc></url>
                </urlset>
                """);

            var options = CreateSeoOnlyOptions(root);
            // The sitemap SEO pass stays global even when the normal audit is scoped to a page sample.
            options.Include = new[] { "index.html" };

            var result = WebSiteAuditor.Audit(options);

            Assert.Contains(result.Issues, issue => issue.Category == "seo" &&
                                                   issue.Path == "sitemap.xml" &&
                                                   issue.Hint.StartsWith("seo-sitemap-noindex", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_FlagsSitemapCanonicalMismatch_WhenSitemapLocDiffersFromPageCanonical()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-seo-sitemap-canonical-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "blog", "network-tools"));

        try
        {
            File.WriteAllText(Path.Combine(root, "blog", "network-tools", "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Network Tools</title>
                  <link rel="canonical" href="https://example.test/blog/network-tools" />
                  <meta property="og:title" content="Network Tools" />
                  <meta property="og:description" content="Network diagnostics." />
                  <meta property="og:url" content="https://example.test/blog/network-tools" />
                  <meta property="og:image" content="https://example.test/assets/card.png" />
                  <meta name="twitter:card" content="summary_large_image" />
                  <meta name="twitter:title" content="Network Tools" />
                  <meta name="twitter:description" content="Network diagnostics." />
                  <meta name="twitter:url" content="https://example.test/blog/network-tools" />
                  <meta name="twitter:image" content="https://example.test/assets/card.png" />
                </head>
                <body>Network Tools</body>
                </html>
                """);

            File.WriteAllText(Path.Combine(root, "sitemap.xml"),
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc>https://example.test/blog/network-tools/</loc></url>
                </urlset>
                """);

            var options = CreateSeoOnlyOptions(root);
            // The sitemap SEO pass stays global even when the normal audit is scoped to a page sample.
            options.Include = new[] { "index.html" };

            var result = WebSiteAuditor.Audit(options);

            Assert.Contains(result.Issues, issue => issue.Category == "seo" &&
                                                   issue.Path == "sitemap.xml" &&
                                                   issue.Hint.StartsWith("seo-sitemap-canonical-mismatch", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_FlagsDuplicateSitemapLocs_WhenSameUrlAppearsMoreThanOnce()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-seo-sitemap-duplicate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "blog", "network-tools"));

        try
        {
            File.WriteAllText(Path.Combine(root, "blog", "network-tools", "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Network Tools</title>
                  <link rel="canonical" href="https://example.test/blog/network-tools/" />
                  <meta property="og:title" content="Network Tools" />
                  <meta property="og:description" content="Network diagnostics." />
                  <meta property="og:url" content="https://example.test/blog/network-tools/" />
                  <meta property="og:image" content="https://example.test/assets/card.png" />
                  <meta name="twitter:card" content="summary_large_image" />
                  <meta name="twitter:title" content="Network Tools" />
                  <meta name="twitter:description" content="Network diagnostics." />
                  <meta name="twitter:url" content="https://example.test/blog/network-tools/" />
                  <meta name="twitter:image" content="https://example.test/assets/card.png" />
                </head>
                <body>Network Tools</body>
                </html>
                """);

            File.WriteAllText(Path.Combine(root, "sitemap.xml"),
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc>https://example.test/blog/network-tools/</loc></url>
                  <url><loc>https://example.test/blog/network-tools/</loc></url>
                </urlset>
                """);

            var result = WebSiteAuditor.Audit(CreateSeoOnlyOptions(root));

            Assert.Contains(result.Issues, issue => issue.Category == "seo" &&
                                                   issue.Path == "sitemap.xml" &&
                                                   issue.Hint.StartsWith("seo-sitemap-duplicate-loc", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_SkipsSeoMetaChecks_WhenPageIsNoIndex()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-seo-noindex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Hidden</title>
                  <meta name="robots" content="noindex,nofollow" />
                  <link rel="canonical" href="/hidden/" />
                  <meta property="og:url" content="/hidden/" />
                </head>
                <body>Hidden page</body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(CreateSeoOnlyOptions(root));
            Assert.DoesNotContain(result.Issues, issue => issue.Category == "seo");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_DetectsApiDocsSeoMeta_WhenTagsArePresent()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-seo-apidocs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "api"));

        try
        {
            File.WriteAllText(Path.Combine(root, "api", "index.html"),
                """
                <!doctype html>
                <html lang="en">
                <head>
                  <meta charset="utf-8" />
                  <meta name="viewport" content="width=device-width, initial-scale=1" />
                  <title>Add-GPOPermission - GPOZaurr API Reference</title>
                  <meta name="description" content="API reference for Add-GPOPermission in GPOZaurr API Reference." />
                  <link rel="canonical" href="https://evotec.xyz/projects/gpozaurr/api/add-gpopermission/" />
                  <link rel="alternate" hreflang="en" href="https://evotec.xyz/projects/gpozaurr/api/add-gpopermission/" />
                  <link rel="alternate" hreflang="x-default" href="https://evotec.xyz/projects/gpozaurr/api/add-gpopermission/" />
                  <!-- Open Graph -->
                  <meta property="og:title" content="Add-GPOPermission - GPOZaurr API Reference" />
                  <meta property="og:description" content="API reference for Add-GPOPermission in GPOZaurr API Reference." />
                  <meta property="og:type" content="website" />
                  <meta property="og:url" content="https://evotec.xyz/projects/gpozaurr/api/add-gpopermission/" />
                  <meta property="og:image" content="https://evotec.xyz/wp-content/uploads/2015/05/Logo-evotec-012.png" />
                  <meta property="og:image:alt" content="Add-GPOPermission - GPOZaurr API Reference" />
                  <meta property="og:site_name" content="Evotec" />

                  <!-- Twitter Card -->
                  <meta name="twitter:card" content="summary_large_image" />
                  <meta name="twitter:title" content="Add-GPOPermission - GPOZaurr API Reference" />
                  <meta name="twitter:site" content="@PrzemyslawKlys" />
                  <meta name="twitter:creator" content="@PrzemyslawKlys" />
                  <meta name="twitter:description" content="API reference for Add-GPOPermission in GPOZaurr API Reference." />
                  <meta name="twitter:url" content="https://evotec.xyz/projects/gpozaurr/api/add-gpopermission/" />
                  <meta name="twitter:image" content="https://evotec.xyz/wp-content/uploads/2015/05/Logo-evotec-012.png" />
                  <meta name="twitter:image:alt" content="Add-GPOPermission - GPOZaurr API Reference" />
                </head>
                <body>API docs</body>
                </html>
                """);

            File.WriteAllText(Path.Combine(root, "api", "alias.html"),
                """
                <!doctype html>
                <html lang="en">
                <head>
                  <meta name="robots" content="noindex,follow" data-pf="api-docs-legacy-alias" />
                  <meta charset="utf-8" />
                  <title>Alias</title>
                  <link rel="canonical" href="https://evotec.xyz/projects/gpozaurr/api/add-gpopermission/" />
                </head>
                <body>Alias</body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(CreateSeoOnlyOptions(root));

            Assert.DoesNotContain(result.Issues, issue => issue.Category == "seo");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static WebAuditOptions CreateSeoOnlyOptions(string siteRoot)
    {
        return new WebAuditOptions
        {
            SiteRoot = siteRoot,
            CheckSeoMeta = true,
            CheckHtmlStructure = false,
            CheckTitles = false,
            CheckDuplicateIds = false,
            CheckMediaEmbeds = false,
            CheckHeadingOrder = false,
            CheckLinkPurposeConsistency = false,
            CheckLinks = false,
            CheckAssets = false,
            CheckNetworkHints = false,
            CheckRenderBlockingResources = false,
            CheckNavConsistency = false,
            CheckUtf8 = false,
            CheckMetaCharset = false,
            CheckUnicodeReplacementChars = false
        };
    }
}
