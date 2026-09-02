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
            var readiness = WebMcpOnlySpec(agentsJson: true);
            readiness.WebMcpTools[0].Kind = " site-search ";
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
            File.WriteAllText(Path.Combine(root, "search", "index.json"), "[]");
            File.WriteAllText(
                Path.Combine(root, "assets", "powerforge", "webmcp-site-search.v1.js"),
                "// stale or tampered runtime that prepare must replace");

            var result = WebAgentReadiness.Prepare(new WebAgentReadinessPrepareOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                SiteName = "Example",
                AgentReadiness = readiness
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
        Assert.Contains("/search/index.json", handler.Requests);
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
            File.WriteAllText(Path.Combine(root, "search", "index.json"), "[]");

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

    [Fact]
    public async Task Scan_RejectsSearchIndexThatRedirectsOutsideConfiguredOrigin()
    {
        var result = await WebAgentReadiness.ScanAsync(new WebAgentReadinessScanOptions
        {
            BaseUrl = "https://example.test",
            HttpMessageHandler = new WebMcpRedirectingScanHandler(redirectPage: false, redirectIndex: true),
            AgentReadiness = WebMcpOnlySpec(agentsJson: false)
        });

        Assert.Contains(result.Checks, check =>
            check.Id == "webmcp" &&
            check.Status == "fail" &&
            check.Message.Contains("search index", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Scan_ResolvesRelativeResourcesAgainstFinalRedirectedPageUrl()
    {
        var handler = new WebMcpRelativeRedirectScanHandler();
        var spec = WebMcpOnlySpec(agentsJson: false);
        spec.WebMcpTools[0].Route = "/search";

        var result = await WebAgentReadiness.ScanAsync(new WebAgentReadinessScanOptions
        {
            BaseUrl = "https://example.test",
            HttpMessageHandler = handler,
            AgentReadiness = spec
        });

        Assert.Contains(result.Checks, check => check.Id == "webmcp" && check.Status == "pass");
        Assert.Contains("/search/index.json", handler.Requests);
        Assert.Contains("/search/webmcp-site-search.v1.js", handler.Requests);
        Assert.DoesNotContain("/webmcp-site-search.v1.js", handler.Requests);
    }

    [Fact]
    public async Task Scan_ResolvesRelativeResourcesAgainstDocumentBaseUrl()
    {
        var handler = new WebMcpDocumentBaseScanHandler();

        var result = await WebAgentReadiness.ScanAsync(new WebAgentReadinessScanOptions
        {
            BaseUrl = "https://example.test",
            HttpMessageHandler = handler,
            AgentReadiness = WebMcpOnlySpec(agentsJson: false)
        });

        Assert.Contains(result.Checks, check => check.Id == "webmcp" && check.Status == "pass");
        Assert.Contains("/alternate/index.json", handler.Requests);
        Assert.Contains("/alternate/webmcp-site-search.v1.js", handler.Requests);
        Assert.DoesNotContain("/search/index.json", handler.Requests);
    }

    [Fact]
    public async Task Scan_RejectsRelativeResourcesWithCrossOriginDocumentBaseUrl()
    {
        var handler = new WebMcpDocumentBaseScanHandler("https://other.test/alternate/");

        var result = await WebAgentReadiness.ScanAsync(new WebAgentReadinessScanOptions
        {
            BaseUrl = "https://example.test",
            HttpMessageHandler = handler,
            AgentReadiness = WebMcpOnlySpec(agentsJson: false)
        });

        Assert.Contains(result.Checks, check => check.Id == "webmcp" && check.Status == "fail");
        Assert.DoesNotContain("/alternate/index.json", handler.Requests);
    }

    [Theory]
    [InlineData("bad name", "/search/", "name")]
    [InlineData("search_site", "https://other.test/search/", "root-relative")]
    [InlineData("search_site", "//other.test/search/", "root-relative")]
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

    [Theory]
    [InlineData("/search/")]
    [InlineData("/search")]
    [InlineData("/search/index.html")]
    public void Prepare_RejectsMultipleSiteSearchToolsOnOneDocumentRoute(string secondRoute)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webmcp-duplicate-route-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var spec = WebMcpOnlySpec(agentsJson: false);
            spec.WebMcpTools =
            [
                spec.WebMcpTools[0],
                new AgentWebMcpToolSpec
                {
                    Name = "search_other",
                    Route = secondRoute,
                    Description = "Search another collection.",
                    Kind = "site-search",
                    ReadOnly = true
                }
            ];

            var exception = Assert.Throws<ArgumentException>(() => WebAgentReadiness.Prepare(new WebAgentReadinessPrepareOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                SiteName = "Example",
                AgentReadiness = spec
            }));

            Assert.Contains("Only one", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("/search/", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("/search-results.html")]
    [InlineData("/searchindex.html")]
    public void Configuration_AllowsDistinctExplicitHtmlDocumentRoutes(string secondRoute)
    {
        var spec = WebMcpOnlySpec(agentsJson: false);
        spec.WebMcpTools =
        [
            spec.WebMcpTools[0],
            new AgentWebMcpToolSpec
            {
                Name = "search_other",
                Route = secondRoute,
                Description = "Search another collection.",
                Kind = "site-search",
                ReadOnly = true
            }
        ];

        WebAgentReadiness.ValidateWebMcpConfiguration(spec);
    }

    [Theory]
    [InlineData("<script src=\"/assets/powerforge/webmcp-site-search.v1.js\" data-powerforge-webmcp></script><main data-webmcp-site-search data-webmcp-tool-name=\"search_site\" data-webmcp-tool-description=\"Search public documentation.\" data-webmcp-search-index=\"/search/index.json\"></main>", false)]
    [InlineData("<head><script defer src=\"/assets/powerforge/webmcp-site-search.v1.js\" data-powerforge-webmcp></script></head><body><main data-webmcp-site-search data-webmcp-tool-name=\"search_site\" data-webmcp-tool-description=\"Search public documentation.\" data-webmcp-search-index=\"/search/index.json\"></main></body>", true)]
    [InlineData("<head><script async defer src=\"/assets/powerforge/webmcp-site-search.v1.js\" data-powerforge-webmcp></script></head><body><main data-webmcp-site-search data-webmcp-tool-name=\"search_site\" data-webmcp-tool-description=\"Search public documentation.\" data-webmcp-search-index=\"/search/index.json\"></main></body>", false)]
    [InlineData("<head><script type=\"module\" src=\"/assets/powerforge/webmcp-site-search.v1.js\" data-powerforge-webmcp></script></head><body><main data-webmcp-site-search data-webmcp-tool-name=\"search_site\" data-webmcp-tool-description=\"Search public documentation.\" data-webmcp-search-index=\"/search/index.json\"></main></body>", true)]
    [InlineData("<body><main data-webmcp-site-search data-webmcp-tool-name=\"search_site\" data-webmcp-tool-description=\"Search public documentation.\" data-webmcp-search-index=\"/search/index.json\"></main><script type=\"application/json\" src=\"/assets/powerforge/webmcp-site-search.v1.js\" data-powerforge-webmcp></script></body>", false)]
    [InlineData("<body><main data-webmcp-site-search data-webmcp-tool-name=\"search_site\" data-webmcp-tool-description=\"Search public documentation.\" data-webmcp-search-index=\"/search/index.json\"></main><script nomodule src=\"/assets/powerforge/webmcp-site-search.v1.js\" data-powerforge-webmcp></script></body>", false)]
    [InlineData("<body><main data-webmcp-site-search data-webmcp-tool-name=\"search_site\" data-webmcp-tool-description=\"Search public documentation.\" data-webmcp-search-index=\"/search/index.json\"></main><script integrity=\"sha256-invalid\" src=\"/assets/powerforge/webmcp-site-search.v1.js\" data-powerforge-webmcp></script></body>", false)]
    [InlineData("<body><main data-webmcp-site-search data-webmcp-tool-name=\"search_site\" data-webmcp-tool-description=\"Search public documentation.\" data-webmcp-search-index=\"/search/index.json\"></main><script type=\"text/javascript; charset=utf-8\" src=\"/assets/powerforge/webmcp-site-search.v1.js\" data-powerforge-webmcp></script></body>", true)]
    public void Verify_RequiresRuntimeToExecuteAfterItsSurface(string markup, bool expectedPass)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webmcp-script-order-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "search"));
        Directory.CreateDirectory(Path.Combine(root, "assets", "powerforge"));

        try
        {
            File.WriteAllText(Path.Combine(root, "search", "index.html"), "<!doctype html><html>" + markup + "</html>");
            File.WriteAllText(Path.Combine(root, "search", "index.json"), "[]");
            File.WriteAllText(Path.Combine(root, "assets", "powerforge", "webmcp-site-search.v1.js"),
                WebSiteBuilder.GetWebMcpSiteSearchAssetContent());

            var result = WebAgentReadiness.Verify(new WebAgentReadinessVerifyOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                AgentReadiness = WebMcpOnlySpec(agentsJson: false)
            });

            var webMcp = Assert.Single(result.Checks, check => check.Id == "webmcp");
            Assert.Equal(expectedPass ? "pass" : "fail", webMcp.Status);
            if (!expectedPass)
                Assert.Contains("before its site-search surface", webMcp.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-json")]
    [InlineData("{}")]
    public void Verify_RejectsMissingMalformedOrNonArraySearchIndex(string? indexContent)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webmcp-index-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "search"));
        Directory.CreateDirectory(Path.Combine(root, "assets", "powerforge"));

        try
        {
            File.WriteAllText(Path.Combine(root, "search", "index.html"),
                """
                <!doctype html><html><body><main data-webmcp-site-search data-webmcp-tool-name="search_site" data-webmcp-tool-description="Search public documentation." data-webmcp-search-index="./index.json"></main>
                <script src="../assets/powerforge/webmcp-site-search.v1.js" data-powerforge-webmcp></script></body></html>
                """);
            File.WriteAllText(Path.Combine(root, "assets", "powerforge", "webmcp-site-search.v1.js"),
                WebSiteBuilder.GetWebMcpSiteSearchAssetContent());
            if (indexContent is not null)
                File.WriteAllText(Path.Combine(root, "search", "index.json"), indexContent);

            var result = WebAgentReadiness.Verify(new WebAgentReadinessVerifyOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                AgentReadiness = WebMcpOnlySpec(agentsJson: false)
            });

            Assert.Contains(result.Checks, check =>
                check.Id == "webmcp" &&
                check.Status == "fail" &&
                check.Message.Contains("JSON-array search index", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Verify_RejectsMismatchedDescriptionOrMultipleActiveSurfaces(bool multipleSurfaces)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webmcp-surface-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "search"));
        Directory.CreateDirectory(Path.Combine(root, "assets", "powerforge"));

        try
        {
            var firstDescription = multipleSurfaces ? "Search public documentation." : "Stale description.";
            var secondSurface = multipleSurfaces
                ? "<main data-webmcp-site-search data-webmcp-tool-name=\"search_other\" data-webmcp-tool-description=\"Other search.\" data-webmcp-search-index=\"./index.json\"></main>"
                : string.Empty;
            File.WriteAllText(Path.Combine(root, "search", "index.html"),
                $"<!doctype html><html><body><main data-webmcp-site-search data-webmcp-tool-name=\"search_site\" data-webmcp-tool-description=\"{firstDescription}\" data-webmcp-search-index=\"./index.json\"></main>{secondSurface}<script src=\"../assets/powerforge/webmcp-site-search.v1.js\" data-powerforge-webmcp></script></body></html>");
            File.WriteAllText(Path.Combine(root, "search", "index.json"), "[]");
            File.WriteAllText(Path.Combine(root, "assets", "powerforge", "webmcp-site-search.v1.js"),
                WebSiteBuilder.GetWebMcpSiteSearchAssetContent());

            var result = WebAgentReadiness.Verify(new WebAgentReadinessVerifyOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                AgentReadiness = WebMcpOnlySpec(agentsJson: false)
            });

            var webMcp = Assert.Single(result.Checks, check => check.Id == "webmcp");
            Assert.Equal("fail", webMcp.Status);
            Assert.Contains(
                multipleSurfaces ? "exactly one" : "configured description",
                webMcp.Message,
                StringComparison.OrdinalIgnoreCase);
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
            spec.AgentReadiness.WebMcpTools[0].Kind = " site-search ";
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
            Assert.Contains("../assets/powerforge/webmcp-site-search.v1.js", searchHtml, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Build_RefreshesLegacyGeneratedFallbackWithCurrentWebMcpConfiguration()
    {
        var (root, spec, configPath, outputRoot) = CreateMinimalWebMcpBuild("https://example.test", "/search/");
        Directory.CreateDirectory(Path.Combine(outputRoot, "search"));
        File.WriteAllText(Path.Combine(outputRoot, "search", "index.html"),
            """
            <!doctype html><html><body><main class="pf-search-wrap"><input id="pf-search-query"><div>Loading search index...</div><div id="pf-search-results"></div></main></body></html>
            """);

        try
        {
            WebSiteBuilder.Build(spec, WebSitePlanner.Plan(spec, configPath), outputRoot);

            var searchHtml = File.ReadAllText(Path.Combine(outputRoot, "search", "index.html"));
            Assert.Contains("data-powerforge-generated-search-fallback", searchHtml, StringComparison.Ordinal);
            Assert.Contains("data-webmcp-tool-name=\"search_site\"", searchHtml, StringComparison.Ordinal);
            Assert.Contains("data-webmcp-tool-description=\"Search public documentation.\"", searchHtml, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Build_GeneratesConfiguredRouteWithBasePathRelativeResources()
    {
        var (root, spec, configPath, outputRoot) = CreateMinimalWebMcpBuild("https://example.test/project/", "/find");

        try
        {
            WebSiteBuilder.Build(spec, WebSitePlanner.Plan(spec, configPath), outputRoot);

            var searchPath = Path.Combine(outputRoot, "find", "index.html");
            var searchHtml = File.ReadAllText(searchPath);
            Assert.Contains("data-webmcp-search-index=\"../search/index.json\"", searchHtml, StringComparison.Ordinal);
            Assert.Contains("src=\"../assets/powerforge/webmcp-site-search.v1.js\"", searchHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("src=\"/assets/powerforge", searchHtml, StringComparison.Ordinal);

            var verify = WebAgentReadiness.Verify(new WebAgentReadinessVerifyOptions
            {
                SiteRoot = outputRoot,
                BaseUrl = spec.BaseUrl,
                AgentReadiness = spec.AgentReadiness
            });
            Assert.Contains(verify.Checks, check => check.Id == "webmcp" && check.Status == "pass");

            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputRoot, "search", "manifest.json")));
            Assert.Equal("/find", manifest.RootElement.GetProperty("searchPagePath").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Build_PreservesThemeOwnedSearchPage()
    {
        var (root, spec, configPath, outputRoot) = CreateMinimalWebMcpBuild("https://example.test", "/search/");
        var searchPath = Path.Combine(outputRoot, "search", "index.html");
        const string customHtml = "<!doctype html><html><body data-theme-owned-search><h1>Custom search</h1></body></html>";
        Directory.CreateDirectory(Path.GetDirectoryName(searchPath)!);
        File.WriteAllText(searchPath, customHtml);

        try
        {
            WebSiteBuilder.Build(spec, WebSitePlanner.Plan(spec, configPath), outputRoot);
            Assert.Equal(customHtml, File.ReadAllText(searchPath));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static (string Root, SiteSpec Spec, string ConfigPath, string OutputRoot) CreateMinimalWebMcpBuild(
        string baseUrl,
        string route)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webmcp-build-contract-" + Guid.NewGuid().ToString("N"));
        var contentRoot = Path.Combine(root, "content", "pages");
        var themeRoot = Path.Combine(root, "themes", "minimal");
        Directory.CreateDirectory(contentRoot);
        Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
        File.WriteAllText(Path.Combine(contentRoot, "index.md"), "---\ntitle: Home\nslug: index\n---\n\nWelcome.");
        File.WriteAllText(Path.Combine(themeRoot, "layouts", "home.html"),
            "<!doctype html><html><head><title>{{TITLE}}</title></head><body>{{CONTENT}}</body></html>");
        File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
            "{\"name\":\"minimal\",\"engine\":\"simple\",\"defaultLayout\":\"home\"}");

        var spec = new SiteSpec
        {
            Name = "WebMCP Build Contract",
            BaseUrl = baseUrl,
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
        spec.AgentReadiness.WebMcpTools[0].Route = route;
        var configPath = Path.Combine(root, "site.json");
        File.WriteAllText(configPath, "{}");
        return (root, spec, configPath, Path.Combine(root, "_site"));
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
                "/search/index.json" => Response(HttpStatusCode.OK, "[]", "application/json"),
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

    private sealed class WebMcpRedirectingScanHandler(bool redirectPage, bool redirectIndex = false) : HttpMessageHandler
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
                "/search/index.json" => Response(HttpStatusCode.OK, "[]", "application/json"),
                "/assets/powerforge/webmcp-site-search.v1.js" => Response(HttpStatusCode.OK,
                    WebSiteBuilder.GetWebMcpSiteSearchAssetContent(),
                    "text/javascript"),
                _ => Response(HttpStatusCode.NotFound, "not found", "text/plain")
            };

            if ((redirectPage && path == "/search/") ||
                (redirectIndex && path == "/search/index.json") ||
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

    private sealed class WebMcpRelativeRedirectScanHandler : HttpMessageHandler
    {
        internal List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(path);
            var response = path switch
            {
                "/" => Response(HttpStatusCode.OK,
                    "<!doctype html><html><body><main><h1>Example</h1></main></body></html>",
                    "text/html"),
                "/sitemap.xml" => Response(HttpStatusCode.OK, "<urlset></urlset>", "application/xml"),
                "/search" => Response(HttpStatusCode.OK,
                    "<!doctype html><html><body><main data-webmcp-site-search data-webmcp-tool-name=\"search_site\" data-webmcp-tool-description=\"Search public documentation.\" data-webmcp-search-index=\"index.json\"></main><script src=\"webmcp-site-search.v1.js\" data-powerforge-webmcp></script></body></html>",
                    "text/html"),
                "/search/index.json" => Response(HttpStatusCode.OK, "[]", "application/json"),
                "/search/webmcp-site-search.v1.js" => Response(HttpStatusCode.OK,
                    WebSiteBuilder.GetWebMcpSiteSearchAssetContent(),
                    "text/javascript"),
                _ => Response(HttpStatusCode.NotFound, "not found", "text/plain")
            };
            response.RequestMessage = path == "/search"
                ? new HttpRequestMessage(HttpMethod.Get, "https://example.test/search/")
                : request;
            return Task.FromResult(response);
        }

        private static HttpResponseMessage Response(HttpStatusCode statusCode, string content, string mediaType) => new(statusCode)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, mediaType)
        };
    }

    private sealed class WebMcpDocumentBaseScanHandler(string documentBase = "/alternate/") : HttpMessageHandler
    {
        internal List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(path);
            var response = path switch
            {
                "/" => Response(HttpStatusCode.OK,
                    "<!doctype html><html><body><main><h1>Example</h1></main></body></html>",
                    "text/html"),
                "/sitemap.xml" => Response(HttpStatusCode.OK, "<urlset></urlset>", "application/xml"),
                "/search/" => Response(HttpStatusCode.OK,
                    $"<!doctype html><html><head><base href=\"{documentBase}\"></head><body><main data-webmcp-site-search data-webmcp-tool-name=\"search_site\" data-webmcp-tool-description=\"Search public documentation.\" data-webmcp-search-index=\"index.json\"></main><script src=\"webmcp-site-search.v1.js\" data-powerforge-webmcp></script></body></html>",
                    "text/html"),
                "/alternate/index.json" => Response(HttpStatusCode.OK, "[]", "application/json"),
                "/alternate/webmcp-site-search.v1.js" => Response(HttpStatusCode.OK,
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
}
