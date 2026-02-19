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

            var result = WebSiteAuditor.Audit(CreateSeoOnlyOptions(root));

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
