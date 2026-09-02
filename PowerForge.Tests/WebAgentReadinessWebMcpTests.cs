using System.Net;
using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Tests;

public partial class WebAgentReadinessTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Prepare_ReportsOnlyVerifiedWebMcpToolsInAgentsJson(bool validSurface)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webmcp-agents-json-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "search"));
        Directory.CreateDirectory(Path.Combine(root, "assets", "powerforge"));

        try
        {
            File.WriteAllText(Path.Combine(root, "sitemap.xml"),
                "<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"><url><loc>https://example.test/</loc></url></urlset>");
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html><html lang="en"><head><title>Example</title><meta name="robots" content="index,follow">
                <script type="application/ld+json">{"@context":"https://schema.org","@type":"Organization","name":"Example","sameAs":["https://example.test"],"dateModified":"2026-09-02"}</script></head>
                <body><header><nav><a href="/search/">Search</a></nav></header><main><h1>Example</h1></main><footer>Example</footer></body></html>
                """);
            File.WriteAllText(Path.Combine(root, "search", "index.html"),
                $"""
                <!doctype html><html><body><main data-webmcp-site-search data-webmcp-tool-name="{(validSurface ? "search_site" : "other_tool")}" data-webmcp-tool-description="Search public documentation." data-webmcp-search-index="/search/index.json"></main>
                <script src="/assets/powerforge/webmcp-site-search.v1.js" data-powerforge-webmcp></script></body></html>
                """);
            File.WriteAllText(
                Path.Combine(root, "assets", "powerforge", "webmcp-site-search.v1.js"),
                "// stale or tampered runtime that prepare must replace");

            var result = WebAgentReadiness.Prepare(new WebAgentReadinessPrepareOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                SiteName = "Example",
                AgentReadiness = WebMcpOnlySpec(agentsJson: true)
            });

            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "agents.json")));
            var capabilities = document.RootElement.GetProperty("capabilities");
            Assert.Equal(validSurface, capabilities.GetProperty("webMcp").GetBoolean());
            var tools = capabilities.GetProperty("webMcpTools");
            Assert.Equal(validSurface ? 1 : 0, tools.GetArrayLength());
            Assert.Equal(validSurface, result.Checks.Single(check => check.Id == "webmcp").Status == "pass");
            Assert.Equal(
                WebSiteBuilder.GetWebMcpSiteSearchAssetContent(),
                File.ReadAllText(Path.Combine(root, "assets", "powerforge", "webmcp-site-search.v1.js")));
            if (validSurface)
            {
                var tool = tools[0];
                Assert.Equal("search_site", tool.GetProperty("name").GetString());
                Assert.Equal("/search/", tool.GetProperty("route").GetString());
                Assert.Equal("site-search", tool.GetProperty("kind").GetString());
                Assert.True(tool.GetProperty("readOnly").GetBoolean());
            }
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Scan_InspectsConfiguredRouteAndSameOriginMarkedRuntime()
    {
        var handler = new WebMcpRouteScanHandler();
        var result = await WebAgentReadiness.ScanAsync(new WebAgentReadinessScanOptions
        {
            BaseUrl = "https://example.test",
            HttpMessageHandler = handler,
            AgentReadiness = WebMcpOnlySpec(agentsJson: false)
        });

        Assert.Contains(result.Checks, check => check.Id == "webmcp" && check.Status == "pass");
        Assert.Contains("/search/", handler.Requests);
        Assert.Contains("/assets/powerforge/webmcp-site-search.v1.js", handler.Requests);
    }

    [Fact]
    public void Verify_RejectsMarkedScriptThatOnlyMentionsRegisterTool()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webmcp-tampered-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "search"));
        Directory.CreateDirectory(Path.Combine(root, "assets", "powerforge"));

        try
        {
            File.WriteAllText(Path.Combine(root, "search", "index.html"),
                """
                <!doctype html><html><body><main data-webmcp-site-search data-webmcp-tool-name="search_site" data-webmcp-tool-description="Search public documentation." data-webmcp-search-index="/search/index.json"></main>
                <script src="/assets/powerforge/webmcp-site-search.v1.js" data-powerforge-webmcp></script></body></html>
                """);
            File.WriteAllText(Path.Combine(root, "assets", "powerforge", "webmcp-site-search.v1.js"),
                "// document.modelContext.registerTool({ name: 'delete_all' });");

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

    [Theory]
    [InlineData("http://powerforge.invalid/assets/powerforge/webmcp-site-search.v1.js")]
    [InlineData("https://powerforge.invalid:444/assets/powerforge/webmcp-site-search.v1.js")]
    public void Verify_RejectsRuntimeFromDifferentOrigin(string runtimeUrl)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webmcp-runtime-origin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "search"));
        Directory.CreateDirectory(Path.Combine(root, "assets", "powerforge"));

        try
        {
            File.WriteAllText(Path.Combine(root, "search", "index.html"),
                $"""
                <!doctype html><html><body><main data-webmcp-site-search data-webmcp-tool-name="search_site" data-webmcp-tool-description="Search public documentation." data-webmcp-search-index="/search/index.json"></main>
                <script src="{runtimeUrl}" data-powerforge-webmcp></script></body></html>
                """);
            File.WriteAllText(Path.Combine(root, "assets", "powerforge", "webmcp-site-search.v1.js"),
                WebSiteBuilder.GetWebMcpSiteSearchAssetContent());

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scan_RejectsPageOrRuntimeThatRedirectsOutsideConfiguredOrigin(bool redirectPage)
    {
        var result = await WebAgentReadiness.ScanAsync(new WebAgentReadinessScanOptions
        {
            BaseUrl = "https://example.test",
            HttpMessageHandler = new WebMcpRedirectingScanHandler(redirectPage),
            AgentReadiness = WebMcpOnlySpec(agentsJson: false)
        });

        Assert.Contains(result.Checks, check => check.Id == "webmcp" && check.Status == "fail");
    }

    [Theory]
    [InlineData("bad name", "/search/", "name")]
    [InlineData("search_site", "https://other.test/search/", "root-relative")]
    [InlineData("search_site", "/safe/%2e%2e/private/", "site root")]
    public void Prepare_RejectsUnsafeWebMcpToolConfiguration(string name, string route, string expectedMessage)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webmcp-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var spec = WebMcpOnlySpec(agentsJson: false);
            spec.WebMcpTools[0].Name = name;
            spec.WebMcpTools[0].Route = route;

            var exception = Assert.Throws<ArgumentException>(() => WebAgentReadiness.Prepare(new WebAgentReadinessPrepareOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                SiteName = "Example",
                AgentReadiness = spec
            }));

            Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Prepare_RejectsUnsupportedWebMcpImplementationKind()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webmcp-unsupported-kind-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var spec = WebMcpOnlySpec(agentsJson: false);
            spec.WebMcpTools[0].Kind = "custom";

            var exception = Assert.Throws<ArgumentException>(() => WebAgentReadiness.Prepare(new WebAgentReadinessPrepareOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                SiteName = "Example",
                AgentReadiness = spec
            }));

            Assert.Contains("supports 'site-search'", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Prepare_RejectsWritableSiteSearchConfiguration()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webmcp-writable-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var spec = WebMcpOnlySpec(agentsJson: false);
            spec.WebMcpTools[0].ReadOnly = false;

            var exception = Assert.Throws<ArgumentException>(() => WebAgentReadiness.Prepare(new WebAgentReadinessPrepareOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                SiteName = "Example",
                AgentReadiness = spec
            }));

            Assert.Contains("cannot be declared writable", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Build_EmitsReusableWebMcpSiteSearchRuntimeAndFallbackSurface()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webmcp-build-" + Guid.NewGuid().ToString("N"));
        var contentRoot = Path.Combine(root, "content", "pages");
        var themeRoot = Path.Combine(root, "themes", "minimal");
        Directory.CreateDirectory(contentRoot);
        Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));

        try
        {
            File.WriteAllText(Path.Combine(contentRoot, "index.md"), "---\ntitle: Home\nslug: index\n---\n\nWelcome.");
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "home.html"),
                "<!doctype html><html><head><title>{{TITLE}}</title></head><body>{{CONTENT}}</body></html>");
            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                "{\"name\":\"minimal\",\"engine\":\"simple\",\"defaultLayout\":\"home\"}");

            var spec = new SiteSpec
            {
                Name = "WebMCP Build",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "minimal",
                ThemesRoot = "themes",
                Features = ["search"],
                AgentReadiness = WebMcpOnlySpec(agentsJson: false),
                Collections =
                [
                    new CollectionSpec { Name = "pages", Input = "content/pages", Output = "/" }
                ]
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var outputRoot = Path.Combine(root, "_site");

            WebSiteBuilder.Build(spec, WebSitePlanner.Plan(spec, configPath), outputRoot);

            var runtimePath = Path.Combine(outputRoot, "assets", "powerforge", "webmcp-site-search.v1.js");
            var searchPath = Path.Combine(outputRoot, "search", "index.html");
            Assert.True(File.Exists(runtimePath));
            Assert.Contains("document.modelContext.registerTool", File.ReadAllText(runtimePath), StringComparison.Ordinal);
            var searchHtml = File.ReadAllText(searchPath);
            Assert.Contains("data-webmcp-tool-name=\"search_site\"", searchHtml, StringComparison.Ordinal);
            Assert.Contains("data-powerforge-webmcp", searchHtml, StringComparison.Ordinal);
            Assert.Contains(WebSiteBuilder.WebMcpSiteSearchAssetRoute, searchHtml, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static AgentReadinessSpec WebMcpOnlySpec(bool agentsJson) => new()
    {
        Enabled = true,
        WebMcp = true,
        WebMcpTools =
        [
            new AgentWebMcpToolSpec
            {
                Name = "search_site",
                Route = "/search/",
                Description = "Search public documentation.",
                Kind = "site-search",
                ReadOnly = true
            }
        ],
        Robots = false,
        LinkHeaders = false,
        SecurityHeaders = new AgentSecurityHeadersSpec { Enabled = false },
        ContentSignals = new AgentContentSignalsSpec { Enabled = false },
        ApiCatalog = new AgentApiCatalogSpec { Enabled = false },
        AgentSkills = new AgentSkillsDiscoverySpec { Enabled = false },
        AgentsJson = new AgentDiscoveryDocumentSpec { Enabled = agentsJson },
        MarkdownNegotiation = false,
        Apache = new AgentApacheSupportSpec { Enabled = false }
    };

    private sealed class WebMcpRouteScanHandler : HttpMessageHandler
    {
        internal List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(path);
            var response = path switch
            {
                "/" => Response(HttpStatusCode.OK,
                    "<!doctype html><html lang=\"en\"><head><title>Example</title><meta name=\"robots\" content=\"index,follow\"></head><body><main><h1>Example</h1></main></body></html>",
                    "text/html"),
                "/sitemap.xml" => Response(HttpStatusCode.OK,
                    "<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"><url><loc>https://example.test/</loc></url></urlset>",
                    "application/xml"),
                "/search/" => Response(HttpStatusCode.OK,
                    "<!doctype html><html><body><main data-webmcp-site-search data-webmcp-tool-name=\"search_site\" data-webmcp-tool-description=\"Search public documentation.\" data-webmcp-search-index=\"/search/index.json\"></main><script src=\"/assets/powerforge/webmcp-site-search.v1.js\" data-powerforge-webmcp></script></body></html>",
                    "text/html"),
                "/assets/powerforge/webmcp-site-search.v1.js" => Response(HttpStatusCode.OK,
                    WebSiteBuilder.GetWebMcpSiteSearchAssetContent(),
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

    private sealed class WebMcpRedirectingScanHandler(bool redirectPage) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var response = path switch
            {
                "/" => Response(HttpStatusCode.OK,
                    "<!doctype html><html lang=\"en\"><head><title>Example</title></head><body><main><h1>Example</h1></main></body></html>",
                    "text/html"),
                "/sitemap.xml" => Response(HttpStatusCode.OK,
                    "<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"><url><loc>https://example.test/</loc></url></urlset>",
                    "application/xml"),
                "/search/" => Response(HttpStatusCode.OK,
                    "<!doctype html><html><body><main data-webmcp-site-search data-webmcp-tool-name=\"search_site\" data-webmcp-tool-description=\"Search public documentation.\" data-webmcp-search-index=\"/search/index.json\"></main><script src=\"/assets/powerforge/webmcp-site-search.v1.js\" data-powerforge-webmcp></script></body></html>",
                    "text/html"),
                "/assets/powerforge/webmcp-site-search.v1.js" => Response(HttpStatusCode.OK,
                    WebSiteBuilder.GetWebMcpSiteSearchAssetContent(),
                    "text/javascript"),
                _ => Response(HttpStatusCode.NotFound, "not found", "text/plain")
            };

            if ((redirectPage && path == "/search/") ||
                (!redirectPage && path == "/assets/powerforge/webmcp-site-search.v1.js"))
                response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://cdn.example.test/webmcp-site-search.v1.js");
            else
                response.RequestMessage = request;
            return Task.FromResult(response);
        }

        private static HttpResponseMessage Response(HttpStatusCode statusCode, string content, string mediaType) => new(statusCode)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, mediaType)
        };
    }
}
