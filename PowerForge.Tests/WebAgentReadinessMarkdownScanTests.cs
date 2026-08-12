using PowerForge.Web;

namespace PowerForge.Tests;

public partial class WebAgentReadinessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scan_FailsWhenEnabledMarkdownArtifactIsMissingOrHasWrongMediaType(bool publishWithWrongMediaType)
    {
        var responses = new Dictionary<string, (string Content, string MediaType)>(StringComparer.Ordinal);
        if (publishWithWrongMediaType)
            responses["/index.md"] = ("# Example", "text/plain");

        var result = await WebAgentReadiness.ScanAsync(new WebAgentReadinessScanOptions
        {
            BaseUrl = "https://example.test",
            HttpMessageHandler = new ConfiguredDiscoveryScanHandler(
                responses,
                "</index.md>; rel=\"alternate\"; type=\"text/markdown\""),
            AgentReadiness = MarkdownOnlySpec()
        });

        var artifact = Assert.Single(result.Checks, check => check.Id == "markdown-artifact-public");
        Assert.Equal("fail", artifact.Status);
        Assert.Contains(
            publishWithWrongMediaType ? "text/plain" : "not found",
            artifact.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal("https://example.test/index.md", artifact.Target);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task Scan_FetchesEnabledMarkdownArtifactForCorsWhenNegotiationSucceeds()
    {
        var responses = new Dictionary<string, (string Content, string MediaType)>(StringComparer.Ordinal)
        {
            ["/index.md"] = ("# Example", "text/markdown")
        };
        var handler = new ConfiguredDiscoveryScanHandler(
            responses,
            "</index.md>; rel=\"alternate\"; type=\"text/markdown\"",
            negotiateMarkdown: true);
        var spec = MarkdownOnlySpec();
        spec.SecurityHeaders = new AgentSecurityHeadersSpec
        {
            Enabled = true,
            Hsts = false,
            ContentSecurityPolicy = false,
            XContentTypeOptions = false,
            XFrameOptions = false,
            ReferrerPolicy = false,
            PermissionsPolicy = false,
            CorsForWellKnown = true,
            CorsAllowOrigin = "*"
        };

        var result = await WebAgentReadiness.ScanAsync(new WebAgentReadinessScanOptions
        {
            BaseUrl = "https://example.test",
            HttpMessageHandler = handler,
            AgentReadiness = spec
        });

        Assert.Equal("pass", Assert.Single(result.Checks, check => check.Id == "markdown-negotiation").Status);
        Assert.Equal("pass", Assert.Single(result.Checks, check => check.Id == "markdown-artifact-public").Status);
        var cors = Assert.Single(result.Checks, check => check.Id == "security-cors");
        Assert.Equal("fail", cors.Status);
        Assert.Contains("/index.md", cors.Message, StringComparison.Ordinal);
        Assert.Contains("/index.md", handler.Requests);
    }

    private static AgentReadinessSpec MarkdownOnlySpec() => new()
    {
        Enabled = true,
        Robots = false,
        LinkHeaders = true,
        SecurityHeaders = new AgentSecurityHeadersSpec { Enabled = false },
        ContentSignals = new AgentContentSignalsSpec { Enabled = false },
        ApiCatalog = new AgentApiCatalogSpec { Enabled = false },
        AgentSkills = new AgentSkillsDiscoverySpec { Enabled = false },
        AgentsJson = new AgentDiscoveryDocumentSpec { Enabled = false },
        A2AAgentCard = new AgentA2ACardSpec { Enabled = false },
        McpServerCard = new AgentMcpServerCardSpec { Enabled = false },
        OpenApi = new AgentOpenApiSpec { Enabled = false },
        MarkdownArtifacts = new AgentMarkdownArtifactsSpec { Enabled = true, Extension = ".md" },
        MarkdownNegotiation = true
    };
}
