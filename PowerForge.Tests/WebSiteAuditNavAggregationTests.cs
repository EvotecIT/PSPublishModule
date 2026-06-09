using PowerForge.Web;

namespace PowerForge.Tests;

public class WebSiteAuditNavAggregationTests
{
    [Fact]
    public void Audit_AggregatesMissingNavWarnings()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-nav-agg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><html><head><title>Home</title></head><body></body></html>");
            File.WriteAllText(Path.Combine(root, "docs", "page.html"), "<!doctype html><html><head><title>Docs</title></head><body></body></html>");

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                IgnoreNavFor = Array.Empty<string>(),
                NavSelector = "nav",
                NavRequired = true,
                CheckLinks = false,
                CheckAssets = false,
                CheckTitles = false,
                CheckDuplicateIds = false,
                CheckHtmlStructure = false,
                CheckHeadingOrder = false,
                CheckLinkPurposeConsistency = false,
                CheckNetworkHints = false,
                CheckRenderBlockingResources = false,
                CheckUtf8 = false,
                CheckMetaCharset = false,
                CheckUnicodeReplacementChars = false
            });

            Assert.True(result.Success);
            Assert.Equal(1, result.Issues.Count(i => i.Category.Equals("nav", StringComparison.OrdinalIgnoreCase)));
            Assert.Contains(result.Warnings, w => w.Contains("on 2 page(s)", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, w => w.Contains("index.html", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, w => w.Contains("docs/page.html", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}

