using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebRedirectPageAuditTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "pf-redirect-audit-" + Guid.NewGuid().ToString("N"));

    public WebRedirectPageAuditTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "target"));
        File.WriteAllText(Path.Combine(root, "target", "index.html"),
            "<html><head><title>Target</title></head><body><header><nav>Navigation</nav></header></body></html>");
    }

    [Theory]
    [InlineData("static-redirect")]
    [InlineData("api-docs-legacy-alias")]
    public void GeneratedRedirectSkipsOnlyItsNavigationCheck(string marker)
    {
        File.WriteAllText(Path.Combine(root, "old.html"), WebRedirectPage.Render("/target/", marker));
        var result = WebSiteAuditor.Audit(Options());
        Assert.True(result.Success, string.Join(";", result.Issues.Select(issue => issue.Message)));
        Assert.Equal(1, result.NavIgnoredCount);
        Assert.Equal(1, result.NavCheckedCount);
    }

    [Fact]
    public void CanonicalPagesStillRequireNavigation()
    {
        File.WriteAllText(Path.Combine(root, "old.html"), WebRedirectPage.Render("/target/", "static-redirect"));
        File.WriteAllText(Path.Combine(root, "target", "index.html"), "<html><head><title>Target</title></head><body>Content</body></html>");
        var result = WebSiteAuditor.Audit(Options());
        Assert.False(result.Success);
        Assert.Contains(result.Issues, issue => issue.Category == "nav");
        Assert.Equal(1, result.NavIgnoredCount);
        Assert.Equal(1, result.NavCheckedCount);
    }

    [Theory]
    [InlineData("<meta name='robots' content='noindex,follow'>")]
    [InlineData("<meta data-pf='static-redirect'><link rel='canonical' href='/target/'>")]
    [InlineData("<meta data-pf='static-redirect'><meta http-equiv='refresh' content='0;url=/target/'>")]
    public void OrdinaryOrIncompleteMarkupDoesNotSuppressNavigation(string head)
    {
        File.WriteAllText(Path.Combine(root, "authored.html"), $"<html><head><title>Authored</title>{head}</head><body><!-- static-redirect -->Content</body></html>");
        var result = WebSiteAuditor.Audit(Options());
        Assert.False(result.Success);
        Assert.Contains(result.Issues, issue => issue.Category == "nav");
        Assert.Equal(0, result.NavIgnoredCount);
        Assert.Equal(2, result.NavCheckedCount);
    }

    [Fact]
    public void GeneratedRedirectRetainsLinkValidation()
    {
        File.WriteAllText(Path.Combine(root, "old.html"), WebRedirectPage.Render("/missing/", "static-redirect"));
        var options = Options();
        options.CheckLinks = true;
        var result = WebSiteAuditor.Audit(options);
        Assert.True(result.BrokenLinkCount > 0);
        Assert.Equal(1, result.NavIgnoredCount);
        Assert.Equal(1, result.NavCheckedCount);
    }

    private WebAuditOptions Options() => new()
    {
        SiteRoot = root,
        CheckLinks = false,
        CheckAssets = false,
        CheckLinkPurposeConsistency = false,
        NavSelector = "header nav",
        NavRequired = true,
        FailOnCategories = new[] { "nav" }
    };

    public void Dispose() => Directory.Delete(root, recursive: true);
}
