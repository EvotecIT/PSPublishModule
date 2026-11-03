using PowerForge.Web;

namespace PowerForge.Tests;

public partial class WebAgentReadinessTests
{
    [Theory]
    [InlineData("", false)]
    [InlineData(" \r\n\t", false)]
    [InlineData("# Content", true)]
    public void Verify_RejectsUnreadableOrEmptyMarkdownArtifacts(string content, bool locked)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-empty-markdown-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var path = Path.Combine(root, "index.md");
            File.WriteAllText(path, content);
            using var lockedFile = locked ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None) : null;
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

    [Theory]
    [InlineData("<dialog><h2>Search</h2></dialog><main><h1>Example</h1></main>", "pass")]
    [InlineData("<header><h2>Search</h2></header><div role='main'><h1>Example</h1></div>", "pass")]
    [InlineData("<main hidden><h1>Old</h1></main><main><h1>Example</h1></main>", "pass")]
    [InlineData("<main><dialog><h2>Search</h2></dialog><div hidden><h2>Hidden</h2></div><h1>Example</h1></main>", "pass")]
    [InlineData("<main><h2>Section</h2><h1>Example</h1></main>", "warn")]
    [InlineData("<main><dialog open><h2>Search</h2></dialog><h1>Example</h1></main>", "warn")]
    public void Verify_UsesFirstMainContentHeading(string html, string expectedStatus)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-main-heading-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), html);
            var result = WebAgentReadiness.Verify(new WebAgentReadinessVerifyOptions { SiteRoot = root });
            Assert.Equal(expectedStatus, Assert.Single(result.Checks, check => check.Id == "heading-hierarchy").Status);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
