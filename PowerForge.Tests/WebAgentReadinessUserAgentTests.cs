using System.Net;
using PowerForge.Web;

namespace PowerForge.Tests;

public partial class WebAgentReadinessTests
{
    [Fact]
    public async Task Scan_UsesSharedVerificationUserAgent()
    {
        var handler = new UserAgentScanHandler();

        _ = await WebAgentReadiness.ScanAsync(new WebAgentReadinessScanOptions
        {
            BaseUrl = "https://example.test",
            HttpMessageHandler = handler,
            AgentReadiness = new AgentReadinessSpec
            {
                Enabled = true,
                Robots = false,
                LinkHeaders = false,
                SecurityHeaders = new AgentSecurityHeadersSpec { Enabled = false },
                ContentSignals = new AgentContentSignalsSpec { Enabled = false },
                ApiCatalog = new AgentApiCatalogSpec { Enabled = false },
                AgentSkills = new AgentSkillsDiscoverySpec { Enabled = false },
                AgentsJson = new AgentDiscoveryDocumentSpec { Enabled = false },
                MarkdownNegotiation = false
            }
        });

        Assert.NotEmpty(handler.UserAgents);
        Assert.Equal(2, handler.RequestUris.Count(uri => uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.Query)));
        Assert.All(handler.UserAgents, userAgent =>
            Assert.Equal(WebVerificationIdentity.UserAgent, userAgent));
    }

    private sealed class UserAgentScanHandler : HttpMessageHandler
    {
        internal List<string> UserAgents { get; } = [];
        internal List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            UserAgents.Add(request.Headers.UserAgent.ToString());
            RequestUris.Add(request.RequestUri!);
            var path = request.RequestUri!.AbsolutePath;
            var content = path switch
            {
                "/" => "<!doctype html><html><body><main><h1>Example</h1></main></body></html>",
                "/sitemap.xml" => "<urlset><url><loc>https://example.test/</loc></url></urlset>",
                _ => "not found"
            };
            var response = new HttpResponseMessage(
                path is "/" or "/sitemap.xml" ? HttpStatusCode.OK : HttpStatusCode.NotFound)
            {
                Content = new StringContent(content)
            };
            return Task.FromResult(response);
        }
    }
}
