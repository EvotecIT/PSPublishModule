using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebSiteAuditLinkTargetsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pf-web-link-targets-" + Guid.NewGuid().ToString("N"));

    public WebSiteAuditLinkTargetsTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("vscode:mcp/install?%7B%22name%22%3A%22officeimo%22%7D")]
    [InlineData("cursor://anysphere.cursor-deeplink/mcp/install?name=officeimo&config=e30%3D")]
    [InlineData("VSCODE:mcp/install?name=officeimo")]
    public void Audit_DoesNotResolveApplicationUrisAsLocalFiles(string href)
    {
        WritePage(href);

        var result = Audit();

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal(0, result.BrokenLinkCount);
        Assert.Equal(0, result.LinkCount);
    }

    [Theory]
    [InlineData("/.well-known/api-catalog")]
    [InlineData("/.well-known/api-catalog?format=json")]
    [InlineData("/.well-known/api-catalog#apis")]
    public void Audit_ResolvesExistingExtensionlessFiles(string href)
    {
        Directory.CreateDirectory(Path.Combine(_root, ".well-known"));
        File.WriteAllText(Path.Combine(_root, ".well-known", "api-catalog"), "{\"apis\":[]}");
        WritePage(href);

        var result = Audit();

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal(1, result.LinkCount);
        Assert.Equal(0, result.BrokenLinkCount);
    }

    [Fact]
    public void Audit_StillRejectsMissingExtensionlessTargets()
    {
        WritePage("/.well-known/missing");

        var result = Audit();

        Assert.False(result.Success);
        Assert.Equal(1, result.BrokenLinkCount);
        Assert.Contains(result.Errors, error => error.Contains("broken link", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("catalog")]
    [InlineData("catalog.html")]
    public void Audit_RejectsExistingFilesInASiblingWithTheSameRootPrefix(string fileName)
    {
        string sibling = _root + "-backup";
        Directory.CreateDirectory(sibling);
        try
        {
            File.WriteAllText(Path.Combine(sibling, fileName), "outside the generated site");
            WritePage("../" + Path.GetFileName(sibling) + "/" + fileName);
            var result = Audit();
            // Unresolvable external paths are excluded, as in SEO; they must never count as resolved site links.
            Assert.Equal(0, result.LinkCount);
        }
        finally { Directory.Delete(sibling, true); }
    }

    [Fact]
    public void Audit_ResolvesTheSiteRootToItsIndex()
    {
        WritePage("/");
        var result = Audit();
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal(1, result.LinkCount);
        Assert.Equal(0, result.BrokenLinkCount);
    }

    [Theory]
    [InlineData("data:text/html,Hello", false)]
    [InlineData("https://example.test/frame", true)]
    [InlineData("//example.test/frame", true)]
    public void Audit_ReferrerPolicyAppliesOnlyToNetworkFrames(string source, bool expectedWarning)
    {
        File.WriteAllText(Path.Combine(_root, "index.html"),
            "<!doctype html><html><head><title>Embed</title></head><body><iframe title=\"Example\" loading=\"lazy\" src=\"" +
            source + "\"></iframe></body></html>");

        var result = Audit();

        Assert.Equal(expectedWarning, result.Warnings.Any(warning => warning.Contains("missing referrerpolicy", StringComparison.Ordinal)));
    }

    private void WritePage(string href) => File.WriteAllText(Path.Combine(_root, "index.html"),
        "<!doctype html><html><head><title>Install</title></head><body><a href=\"" +
        System.Net.WebUtility.HtmlEncode(href) + "\">Install</a></body></html>");

    private WebAuditResult Audit() => WebSiteAuditor.Audit(new WebAuditOptions
    {
        SiteRoot = _root,
        CheckAssets = false,
        NavRequired = false,
        MediaProfiles = new[] { new WebAuditMediaProfile { Match = "**", RequireIframeReferrerPolicy = true } }
    });

    public void Dispose() => Directory.Delete(_root, true);
}
