using System.Net;
using PowerForge.Web;

namespace PowerForge.Tests;

public partial class WebAgentReadinessTests
{
    [Fact]
    public async Task Scan_AcceptsCanonicalRuntimeWithPlatformLineEndings()
    {
        var handler = new RuntimeIdentityWebMcpRouteScanHandler(
            WebSiteBuilder.GetWebMcpSiteSearchAssetContent().Replace("\n", "\r\n", StringComparison.Ordinal));
        var result = await WebAgentReadiness.ScanAsync(new WebAgentReadinessScanOptions
        {
            BaseUrl = "https://example.test",
            HttpMessageHandler = handler,
            AgentReadiness = WebMcpOnlySpec(agentsJson: false)
        });

        Assert.Contains(result.Checks, check => check.Id == "webmcp" && check.Status == "pass");
    }

    [Theory]
    [InlineData("\r")]
    [InlineData("\f")]
    [InlineData("\u0085")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public async Task Scan_RejectsOtherLineEndingSubstitutions(string replacement)
    {
        var handler = new RuntimeIdentityWebMcpRouteScanHandler(
            WebSiteBuilder.GetWebMcpSiteSearchAssetContent().Replace("\n", replacement, StringComparison.Ordinal));
        var result = await WebAgentReadiness.ScanAsync(new WebAgentReadinessScanOptions
        {
            BaseUrl = "https://example.test",
            HttpMessageHandler = handler,
            AgentReadiness = WebMcpOnlySpec(agentsJson: false)
        });

        Assert.Contains(result.Checks, check => check.Id == "webmcp" && check.Status == "fail");
    }

    [Fact]
    public void Verify_AcceptsCanonicalRuntimeWithPlatformLineEndings()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webmcp-runtime-line-endings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "search"));
        Directory.CreateDirectory(Path.Combine(root, "assets", "powerforge"));

        try
        {
            File.WriteAllText(Path.Combine(root, "search", "index.html"),
                """
                <!doctype html><html><body><main data-webmcp-site-search data-webmcp-tool-name="search_site" data-webmcp-tool-description="Search public documentation." data-webmcp-search-index="/search/index.json"></main>
                <script src="/assets/powerforge/webmcp-site-search.v1.js" data-powerforge-webmcp></script></body></html>
                """);
            File.WriteAllText(Path.Combine(root, "search", "index.json"), "[]");
            File.WriteAllText(
                Path.Combine(root, "assets", "powerforge", "webmcp-site-search.v1.js"),
                WebSiteBuilder.GetWebMcpSiteSearchAssetContent().ReplaceLineEndings("\r\n"));

            var result = WebAgentReadiness.Verify(new WebAgentReadinessVerifyOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                AgentReadiness = WebMcpOnlySpec(agentsJson: false)
            });

            Assert.Contains(result.Checks, check => check.Id == "webmcp" && check.Status == "pass");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("\r")]
    [InlineData("\f")]
    [InlineData("\u0085")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public void Verify_RejectsOtherLineEndingSubstitutions(string replacement)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webmcp-runtime-line-endings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "search"));
        Directory.CreateDirectory(Path.Combine(root, "assets", "powerforge"));

        try
        {
            File.WriteAllText(Path.Combine(root, "search", "index.html"),
                """
                <!doctype html><html><body><main data-webmcp-site-search data-webmcp-tool-name="search_site" data-webmcp-tool-description="Search public documentation." data-webmcp-search-index="/search/index.json"></main>
                <script src="/assets/powerforge/webmcp-site-search.v1.js" data-powerforge-webmcp></script></body></html>
                """);
            File.WriteAllText(Path.Combine(root, "search", "index.json"), "[]");
            File.WriteAllText(
                Path.Combine(root, "assets", "powerforge", "webmcp-site-search.v1.js"),
                WebSiteBuilder.GetWebMcpSiteSearchAssetContent().Replace("\n", replacement, StringComparison.Ordinal));

            var result = WebAgentReadiness.Verify(new WebAgentReadinessVerifyOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                AgentReadiness = WebMcpOnlySpec(agentsJson: false)
            });

            Assert.Contains(result.Checks, check => check.Id == "webmcp" && check.Status == "fail");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private sealed class RuntimeIdentityWebMcpRouteScanHandler(string runtimeContent) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = request.RequestUri!.AbsolutePath switch
            {
                "/" => Response(HttpStatusCode.OK,
                    "<!doctype html><html lang=\"en\"><head><title>Example</title></head><body><main><h1>Example</h1></main></body></html>",
                    "text/html"),
                "/sitemap.xml" => Response(HttpStatusCode.OK, "<urlset></urlset>", "application/xml"),
                "/search/" => Response(HttpStatusCode.OK,
                    "<!doctype html><html><body><main data-webmcp-site-search data-webmcp-tool-name=\"search_site\" data-webmcp-tool-description=\"Search public documentation.\" data-webmcp-search-index=\"/search/index.json\"></main><script src=\"/assets/powerforge/webmcp-site-search.v1.js\" data-powerforge-webmcp></script></body></html>",
                    "text/html"),
                "/search/index.json" => Response(HttpStatusCode.OK, "[]", "application/json"),
                "/assets/powerforge/webmcp-site-search.v1.js" => Response(HttpStatusCode.OK,
                    runtimeContent,
                    "text/javascript"),
                _ => Response(HttpStatusCode.NotFound, "not found", "text/plain")
            };
            response.RequestMessage = request;
            return Task.FromResult(response);
        }

        private static HttpResponseMessage Response(HttpStatusCode statusCode, string content, string mediaType) => new(statusCode)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, mediaType)
        };
    }
}
