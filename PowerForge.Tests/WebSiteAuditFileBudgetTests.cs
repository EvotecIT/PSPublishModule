using PowerForge.Web;

namespace PowerForge.Tests;

public class WebSiteAuditFileBudgetTests
{
    [Fact]
    public void Audit_MaxTotalFiles_EmitsBudgetWarning()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-file-budget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><html><head><title>Home</title></head><body></body></html>");
            File.WriteAllText(Path.Combine(root, "extra.txt"), "x");

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                MaxTotalFiles = 1,
                CheckLinks = false,
                CheckAssets = false,
                CheckNavConsistency = false,
                NavRequired = false,
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
            Assert.True(result.TotalFileCount > 1);
            Assert.Contains(result.Issues, i => i.Category.Equals("budget", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_MaxTotalFiles_CanExcludePathsFromBudget()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-file-budget-exclude-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><html><head><title>Home</title></head><body></body></html>");
            File.WriteAllText(Path.Combine(root, "extra.txt"), "x");

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                MaxTotalFiles = 1,
                BudgetExclude = new[] { "extra.txt" },
                CheckLinks = false,
                CheckAssets = false,
                CheckNavConsistency = false,
                NavRequired = false,
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
            Assert.Equal(1, result.TotalFileCount);
            Assert.DoesNotContain(result.Issues, i => i.Category.Equals("budget", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_MaxTotalFiles_CanBeGatedByFailCategories()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-file-budget-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><html><head><title>Home</title></head><body></body></html>");
            File.WriteAllText(Path.Combine(root, "extra.txt"), "x");

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                MaxTotalFiles = 1,
                FailOnCategories = new[] { "budget" },
                CheckLinks = false,
                CheckAssets = false,
                CheckNavConsistency = false,
                NavRequired = false,
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

            Assert.False(result.Success);
            Assert.Contains(result.Errors, e => e.Contains("fail categories", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_MaxTotalFiles_CanBeSuppressedBySuppressIssues()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-file-budget-suppress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><html><head><title>Home</title></head><body></body></html>");
            File.WriteAllText(Path.Combine(root, "extra.txt"), "x");

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                MaxTotalFiles = 1,
                FailOnCategories = new[] { "budget" },
                SuppressIssues = new[] { "PFAUDIT.BUDGET" },
                CheckLinks = false,
                CheckAssets = false,
                CheckNavConsistency = false,
                NavRequired = false,
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

            Assert.True(result.TotalFileCount > 1);
            Assert.True(result.Success);
            Assert.DoesNotContain(result.Issues, i => i.Category.Equals("budget", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
