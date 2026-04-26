using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebSitemapMigrationAnalyzerTests
{
    [Fact]
    public void Analyze_MapsRootContentToBlogAndGeneratesSyntheticAmpRedirect()
    {
        var result = WebSitemapMigrationAnalyzer.Analyze(new WebSitemapMigrationOptions
        {
            LegacyUrls = new[] { "https://example.test/my-post/" },
            NewUrls = new[] { "https://example.test/blog/my-post/" }
        });

        Assert.Equal(1, result.MissingLegacyCount);
        Assert.Contains(result.Redirects, row =>
            row.LegacyUrl == "https://example.test/my-post/" &&
            row.TargetUrl == "https://example.test/blog/my-post/" &&
            row.MatchKind == "root-to-blog");
        Assert.Contains(result.Redirects, row =>
            row.LegacyUrl == "https://example.test/my-post/amp/" &&
            row.TargetUrl == "https://example.test/blog/my-post/" &&
            row.MatchKind == "synthetic-amp-to-root-to-blog");
    }

    [Fact]
    public void Analyze_MapsCategoryUsingDiacriticInsensitiveSlugVariant()
    {
        var result = WebSitemapMigrationAnalyzer.Analyze(new WebSitemapMigrationOptions
        {
            LegacyUrls = new[] { "https://example.test/category/zażółć-gęślą/" },
            NewUrls = new[] { "https://example.test/categories/zazolc-gesla/" }
        });

        var redirect = Assert.Single(result.Redirects, static row => row.MatchKind == "category-normalized");
        Assert.Equal("https://example.test/category/zażółć-gęślą/", redirect.LegacyUrl);
        Assert.Equal("https://example.test/categories/zazolc-gesla/", redirect.TargetUrl);
        Assert.Equal("category-normalized", redirect.MatchKind);
    }

    [Fact]
    public void Analyze_UsesGeneratedRouteWhenRouteExistsOutsideSitemap()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-sitemap-migration-route-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "projects", "pswritehtml"));
        File.WriteAllText(Path.Combine(root, "projects", "pswritehtml", "index.html"), "<!doctype html>");

        try
        {
            var result = WebSitemapMigrationAnalyzer.Analyze(new WebSitemapMigrationOptions
            {
                LegacyUrls = new[] { "https://example.test/powershell-modules/pswritehtml/" },
                NewUrls = Array.Empty<string>(),
                NewSiteRoot = root,
                IncludeSyntheticAmpRedirects = false
            });

            var redirect = Assert.Single(result.Redirects);
            Assert.Equal("https://example.test/projects/pswritehtml/", redirect.TargetUrl);
            Assert.Equal("powershell-modules-detail", redirect.MatchKind);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Analyze_IncludesAmpListingRoots_WhenBlogRouteExists()
    {
        var result = WebSitemapMigrationAnalyzer.Analyze(new WebSitemapMigrationOptions
        {
            LegacyUrls = new[] { "https://example.test/old/" },
            NewUrls = new[] { "https://example.test/blog/" },
            IncludeSyntheticAmpRedirects = false,
            IncludeAmpListingRoots = true
        });

        Assert.Contains(result.Redirects, row =>
            row.LegacyUrl == "https://example.test/amp/" &&
            row.TargetUrl == "https://example.test/blog/" &&
            row.MatchKind == "amp-root-to-blog");
    }

    [Fact]
    public void GetSlugVariants_RemovesNumericSuffixesAndDiacritics()
    {
        var variants = WebSitemapMigrationAnalyzer.GetSlugVariants("Zażółć-Gęślą-2");

        Assert.Contains("Zażółć-Gęślą", variants, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Zazolc-Gesla-2", variants, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Zazolc-Gesla", variants, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_IgnoresMalformedSitemapLocValues()
    {
        var result = WebSitemapMigrationAnalyzer.Analyze(new WebSitemapMigrationOptions
        {
            LegacyUrls = new[] { "not-a-url", "https://example.test/old/" },
            NewUrls = new[] { "https://example.test/blog/old/", "http://[invalid" }
        });

        Assert.Equal(1, result.LegacyUrlCount);
        Assert.Equal(1, result.NewUrlCount);
        Assert.Contains(result.Redirects, row =>
            row.LegacyUrl == "https://example.test/old/" &&
            row.TargetUrl == "https://example.test/blog/old/");
    }

    [Fact]
    public void GeneratedRouteExists_DoesNotProbeOutsideSiteRoot()
    {
        var parent = Path.Combine(Path.GetTempPath(), "pf-web-sitemap-migration-root-guard-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(parent, "site");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(parent, "secret.html"), "<!doctype html>");

        try
        {
            Assert.False(WebSitemapMigrationAnalyzer.GeneratedRouteExists(root, "https://example.test/%2e%2e/secret/"));
        }
        finally
        {
            TryDeleteDirectory(parent);
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
            // best-effort cleanup
        }
    }
}
