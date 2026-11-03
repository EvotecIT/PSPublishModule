using PowerForge.Web;

namespace PowerForge.Tests;

public partial class WebAgentReadinessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Verify_RejectsEmptyMarkdownArtifacts(bool whitespace)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-empty-markdown-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "index.md"), whitespace ? " \r\n\t" : "");
            var result = WebAgentReadiness.Verify(new WebAgentReadinessVerifyOptions
            {
                SiteRoot = root,
                AgentReadiness = MarkdownOnlySpec()
            });
            Assert.Equal("fail", Assert.Single(result.Checks, check => check.Id == "markdown-artifacts").Status);
            Assert.Equal("fail", Assert.Single(result.Checks, check => check.Id == "markdown-root-artifact").Status);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scan_RejectsEmptyMarkdownArtifacts(bool whitespace)
    {
        var result = await WebAgentReadiness.ScanAsync(new WebAgentReadinessScanOptions
        {
            BaseUrl = "https://example.test",
            HttpMessageHandler = new ConfiguredDiscoveryScanHandler(new Dictionary<string, (string Content, string MediaType)>
            {
                ["/index.md"] = (whitespace ? " \r\n\t" : "", "text/markdown")
            }),
            AgentReadiness = MarkdownOnlySpec()
        });
        Assert.Equal("fail", Assert.Single(result.Checks, check => check.Id == "markdown-artifact-public").Status);
        Assert.Equal("fail", Assert.Single(result.Checks, check => check.Id == "markdown-negotiation").Status);
    }

    [Fact]
    public async Task Scan_RejectsEmptyNegotiatedMarkdown()
    {
        var result = await WebAgentReadiness.ScanAsync(new WebAgentReadinessScanOptions
        {
            BaseUrl = "https://example.test",
            HttpMessageHandler = new ConfiguredDiscoveryScanHandler(
                new Dictionary<string, (string Content, string MediaType)>(),
                negotiateMarkdown: true, negotiatedMarkdownContent: " \r\n\t"),
            AgentReadiness = MarkdownOnlySpec()
        });
        Assert.Equal("fail", Assert.Single(result.Checks, check => check.Id == "markdown-negotiation").Status);
    }

    [Fact]
    public void Verify_UsesMainContentHeadingBeforeSearchDialogHeadings()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-main-heading-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<dialog><h2>Search</h2></dialog><main><h1>Example</h1></main>");
            var result = WebAgentReadiness.Verify(new WebAgentReadinessVerifyOptions { SiteRoot = root });
            Assert.Equal("pass", Assert.Single(result.Checks, check => check.Id == "heading-hierarchy").Status);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
