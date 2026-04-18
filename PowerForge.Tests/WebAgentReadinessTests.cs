using System.Text.Json;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public class WebAgentReadinessTests
{
    [Fact]
    public void Prepare_WritesDiscoveryFilesAndHeaders()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-agent-ready-prepare-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "sitemap.xml"),
                """
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc>https://example.test/</loc></url>
                </urlset>
                """);
            Directory.CreateDirectory(Path.Combine(root, "api"));
            File.WriteAllText(Path.Combine(root, "api", "index.json"), "{}");
            File.WriteAllText(Path.Combine(root, "llms.txt"), "# Example");
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html lang="en">
                <head>
                  <meta name="robots" content="index,follow">
                  <script type="application/ld+json">
                  {"@context":"https://schema.org","@type":["WebSite","Organization"],"name":"Example","sameAs":["https://example.test"],"publisher":{"@type":"Organization","name":"Example"},"dateModified":"2026-04-17"}
                  </script>
                </head>
                <body>
                  <header><nav><a href="/docs/">Documentation</a></nav></header>
                  <main><h1>Example?</h1><p>Hello agents.</p><img src="/logo.png" alt=""></main>
                  <footer>Example</footer>
                </body>
                </html>
                """);

            var result = WebAgentReadiness.Prepare(new WebAgentReadinessPrepareOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                SiteName = "Example",
                AgentReadiness = new AgentReadinessSpec
                {
                    Enabled = true,
                    ContentSignals = new AgentContentSignalsSpec { Search = true, AiInput = false, AiTrain = false },
                    ApiCatalog = new AgentApiCatalogSpec { Enabled = true, OutputPath = ".well-known/custom-api-catalog" },
                    AgentSkills = new AgentSkillsDiscoverySpec { Enabled = true },
                    A2AAgentCard = new AgentA2ACardSpec { Enabled = true }
                }
            });

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Checks.Select(check => $"{check.Status}: {check.Id} - {check.Message}")));
            Assert.True(File.Exists(Path.Combine(root, "robots.txt")));
            Assert.True(File.Exists(Path.Combine(root, ".well-known", "custom-api-catalog")));
            Assert.True(File.Exists(Path.Combine(root, ".well-known", "agent-skills", "index.json")));
            Assert.True(File.Exists(Path.Combine(root, "agents.json")));
            Assert.True(File.Exists(Path.Combine(root, ".well-known", "agent-card.json")));
            Assert.True(File.Exists(Path.Combine(root, "_headers")));

            var robots = File.ReadAllText(Path.Combine(root, "robots.txt"));
            Assert.Contains("Content-Signal: search=yes, ai-input=no, ai-train=no", robots, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Sitemap: https://example.test/sitemap.xml", robots, StringComparison.OrdinalIgnoreCase);

            var headers = File.ReadAllText(Path.Combine(root, "_headers"));
            Assert.Contains("</.well-known/custom-api-catalog>; rel=\"api-catalog\"", headers, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("/.well-known/custom-api-catalog" + Environment.NewLine + "  Content-Type: application/linkset+json", headers, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/.well-known/api-catalog" + Environment.NewLine + "  Content-Type:", headers, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("rel=\"api-catalog\"", headers, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("application/linkset+json", headers, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Strict-Transport-Security", headers, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Access-Control-Allow-Origin", headers, StringComparison.OrdinalIgnoreCase);

            using var agentsJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "agents.json")));
            Assert.Contains("/.well-known/custom-api-catalog", agentsJson.RootElement.GetProperty("resources").GetProperty("apiCatalog").GetString(), StringComparison.OrdinalIgnoreCase);

            using var apiCatalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, ".well-known", "custom-api-catalog")));
            Assert.True(apiCatalog.RootElement.TryGetProperty("linkset", out var linkset));
            Assert.True(linkset.GetArrayLength() > 0);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Prepare_WritesSecurityHeadersWhenLinkHeadersAreDisabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-agent-ready-security-headers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "sitemap.xml"),
                """
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc>https://example.test/</loc></url>
                </urlset>
                """);
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html lang="en">
                <head><title>Example</title><meta name="robots" content="index,follow"><script type="application/ld+json">{"@context":"https://schema.org","@type":["WebSite","Organization"],"name":"Example","sameAs":["https://example.test"],"publisher":{"@type":"Organization","name":"Example"},"dateModified":"2026-04-17"}</script></head>
                <body><header><nav><a href="/">Home</a></nav></header><main><h1>Example?</h1><p>Hello agents.</p></main><footer>Footer</footer></body>
                </html>
                """);

            var result = WebAgentReadiness.Prepare(new WebAgentReadinessPrepareOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                SiteName = "Example",
                AgentReadiness = new AgentReadinessSpec
                {
                    Enabled = true,
                    LinkHeaders = false,
                    MarkdownNegotiation = false,
                    ApiCatalog = new AgentApiCatalogSpec { Enabled = false },
                    AgentSkills = new AgentSkillsDiscoverySpec { Enabled = false },
                    AgentsJson = new AgentDiscoveryDocumentSpec { Enabled = true }
                }
            });

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Checks.Select(check => $"{check.Status}: {check.Id} - {check.Message}")));
            var headers = File.ReadAllText(Path.Combine(root, "_headers"));
            Assert.Contains("Strict-Transport-Security", headers, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Content-Security-Policy", headers, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Link:", headers, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/.well-known/api-catalog", headers, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Prepare_GeneratesMarkdownArtifactsWhenEnabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-agent-ready-markdown-artifacts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "docs"));

        try
        {
            File.WriteAllText(Path.Combine(root, "sitemap.xml"),
                """
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc>https://example.test/</loc></url>
                </urlset>
                """);
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html lang="en">
                <head><title>Example Home</title><meta name="robots" content="index,follow"><script type="application/ld+json">{"@context":"https://schema.org","@type":["WebSite","Organization"],"name":"Example","sameAs":["https://example.test"],"publisher":{"@type":"Organization","name":"Example"},"dateModified":"2026-04-17"}</script></head>
                <body><main><h1>Welcome</h1><p>Hello <strong>agents</strong>.</p><p><a href="/docs/">Read docs</a></p></main></body>
                </html>
                """);
            File.WriteAllText(Path.Combine(root, "docs", "index.html"),
                """
                <!doctype html>
                <html lang="en">
                <head><title>Docs</title></head>
                <body><main><h1>Docs</h1><ul><li>Install</li><li>Configure</li></ul></main></body>
                </html>
                """);

            var result = WebAgentReadiness.Prepare(new WebAgentReadinessPrepareOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                SiteName = "Example",
                AgentReadiness = new AgentReadinessSpec
                {
                    Enabled = true,
                    MarkdownArtifacts = new AgentMarkdownArtifactsSpec { Enabled = true },
                    MarkdownNegotiation = true,
                    ApiCatalog = new AgentApiCatalogSpec { Enabled = false },
                    AgentSkills = new AgentSkillsDiscoverySpec { Enabled = false },
                    AgentsJson = new AgentDiscoveryDocumentSpec { Enabled = true }
                }
            });

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Checks.Select(check => $"{check.Status}: {check.Id} - {check.Message}")));
            Assert.True(File.Exists(Path.Combine(root, "index.md")));
            Assert.True(File.Exists(Path.Combine(root, "docs", "index.md")));

            var markdown = File.ReadAllText(Path.Combine(root, "index.md"));
            Assert.Contains("# Example Home", markdown, StringComparison.Ordinal);
            Assert.Contains("Hello **agents**.", markdown, StringComparison.Ordinal);
            Assert.Contains("[Read docs](/docs/)", markdown, StringComparison.Ordinal);

            var headers = File.ReadAllText(Path.Combine(root, "_headers"));
            Assert.Contains("</index.md>; rel=\"alternate\"; type=\"text/markdown\"", headers, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("/index.md" + Environment.NewLine + "  Content-Type: text/markdown; charset=utf-8", headers, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("/docs/index.md" + Environment.NewLine + "  Content-Type: text/markdown; charset=utf-8", headers, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(result.Checks, check => check.Id == "markdown-artifacts" && check.Status == "pass");
            Assert.Contains(result.Checks, check => check.Id == "markdown-root-artifact" && check.Status == "pass");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Verify_DisabledAgentReadinessShortCircuits()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-agent-ready-disabled-all-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var result = WebAgentReadiness.Verify(new WebAgentReadinessVerifyOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                AgentReadiness = new AgentReadinessSpec { Enabled = false }
            });

            Assert.True(result.Success);
            Assert.Empty(result.Checks);
            Assert.Contains(result.Warnings, warning => warning.Contains("disabled", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveSpec_DoesNotMutateCallerSpec()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-agent-ready-no-mutate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "sitemap.xml"),
                """
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc>https://example.test/</loc></url>
                </urlset>
                """);
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html lang="en">
                <head><title>Example</title><meta name="robots" content="index,follow"><script type="application/ld+json">{"@context":"https://schema.org","@type":["WebSite","Organization"],"name":"Example","sameAs":["https://example.test"],"publisher":{"@type":"Organization","name":"Example"},"dateModified":"2026-04-17"}</script></head>
                <body><header><nav><a href="/">Home</a></nav></header><main><h1>Example?</h1><p>Hello agents.</p></main><footer>Footer</footer></body>
                </html>
                """);

            var spec = new AgentReadinessSpec
            {
                Enabled = true,
                MarkdownNegotiation = false
            };

            _ = WebAgentReadiness.Prepare(new WebAgentReadinessPrepareOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                SiteName = "Example",
                AgentReadiness = spec
            });

            Assert.Null(spec.SecurityHeaders);
            Assert.Null(spec.ContentSignals);
            Assert.Null(spec.ApiCatalog);
            Assert.Null(spec.AgentSkills);
            Assert.Null(spec.AgentsJson);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_AgentReady_PreparesAfterBuildAndSitemap()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-agent-ready-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var contentRoot = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(contentRoot);
            File.WriteAllText(Path.Combine(contentRoot, "index.md"),
                """
                ---
                title: Home
                ---

                Hello
                """);

            var themeRoot = Path.Combine(root, "themes", "base", "layouts");
            Directory.CreateDirectory(themeRoot);
            File.WriteAllText(Path.Combine(themeRoot, "page.html"),
                """
                <!doctype html><html lang="en"><head><title>{{TITLE}}</title><meta name="robots" content="index,follow"><script type="application/ld+json">{"@context":"https://schema.org","@type":["WebSite","Organization"],"name":"Agent Ready Pipeline","sameAs":["https://example.test"],"publisher":{"@type":"Organization","name":"Agent Ready Pipeline"},"dateModified":"2026-04-17"}</script></head><body><header><nav><a href="/">Home</a></nav></header><main><h1>{{TITLE}}?</h1>{{CONTENT}}</main><footer>Footer</footer></body></html>
                """);

            File.WriteAllText(Path.Combine(root, "site.json"),
                """
                {
                  "name": "Agent Ready Pipeline",
                  "baseUrl": "https://example.test",
                  "contentRoot": "content",
                  "themesRoot": "themes",
                  "defaultTheme": "base",
                  "agentReadiness": {
                    "enabled": true,
                    "apiCatalog": {
                      "enabled": true,
                      "entries": [
                        { "anchor": "/api/", "serviceDesc": "/api/index.json", "serviceDoc": "/api/" }
                      ]
                    },
                    "agentSkills": { "enabled": true },
                    "a2aAgentCard": { "enabled": true }
                  },
                  "collections": [
                    { "name": "pages", "input": "content/pages", "output": "/", "defaultLayout": "page" }
                  ]
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    { "task": "build", "config": "./site.json", "out": "./site", "clean": true },
                    { "task": "sitemap", "dependsOn": "build", "config": "./site.json" },
                    { "task": "agent-ready", "dependsOn": "sitemap", "operation": "prepare", "config": "./site.json" }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.Equal(3, result.Steps.Count);
            Assert.True(result.Steps[2].Success, result.Steps[2].Message);
            Assert.True(File.Exists(Path.Combine(root, "site", ".well-known", "agent-skills", "index.json")));
            Assert.True(File.Exists(Path.Combine(root, "site", "_headers")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void AgentReadyExpectedOutputsUseLastBuildOutputWhenSiteRootIsImplicit()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-agent-ready-cache-outputs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var siteRoot = Path.Combine(root, "site");
            var step = JsonDocument.Parse("""{ "task": "agent-ready", "config": "./site.json" }""").RootElement.Clone();
            var method = typeof(WebPipelineRunner).GetMethod("GetExpectedStepOutputs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            Assert.NotNull(method);

            var outputs = Assert.IsType<string[]>(method.Invoke(null, new object[] { "agent-ready", step, root, siteRoot }));

            Assert.Contains(Path.Combine(siteRoot, "robots.txt"), outputs);
            Assert.Contains(Path.Combine(siteRoot, "_headers"), outputs);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void AgentReadyScanStepsAreNotCacheable()
    {
        var step = JsonDocument.Parse("""{ "task": "agent-ready", "operation": "scan", "url": "https://example.test" }""").RootElement.Clone();
        var method = typeof(WebPipelineRunner).GetMethod("IsCacheableStep", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);

        var cacheable = Assert.IsType<bool>(method.Invoke(null, new object[] { "agent-ready", step }));

        Assert.False(cacheable);
    }

    [Fact]
    public void AgentReadyExpectedOutputsHonorConfiguredOptionalArtifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-agent-ready-optional-outputs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var siteRoot = Path.Combine(root, "site");
            Directory.CreateDirectory(siteRoot);
            File.WriteAllText(Path.Combine(siteRoot, "index.html"), "<!doctype html><html><body><main><h1>Home</h1></main></body></html>");
            File.WriteAllText(Path.Combine(root, "site.json"),
                """
                {
                  "name": "Agent Ready Optional Outputs",
                  "agentReadiness": {
                    "enabled": true,
                    "apiCatalog": { "enabled": false },
                    "agentSkills": { "enabled": true, "indexPath": ".well-known/skills.json" },
                    "agentsJson": { "enabled": false },
                    "a2aAgentCard": { "enabled": false },
                    "mcpServerCard": { "enabled": false },
                    "markdownArtifacts": { "enabled": true }
                  }
                }
                """);
            var step = JsonDocument.Parse("""{ "task": "agent-ready", "operation": "prepare", "config": "./site.json" }""").RootElement.Clone();
            var method = typeof(WebPipelineRunner).GetMethod("GetExpectedStepOutputs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            Assert.NotNull(method);

            var outputs = Assert.IsType<string[]>(method.Invoke(null, new object[] { "agent-ready", step, root, siteRoot }));

            Assert.Contains(Path.Combine(siteRoot, "robots.txt"), outputs);
            Assert.Contains(Path.Combine(siteRoot, ".well-known", "skills.json"), outputs);
            Assert.Contains(Path.Combine(siteRoot, "index.md"), outputs);
            Assert.Contains(Path.Combine(siteRoot, "_headers"), outputs);
            Assert.DoesNotContain(Path.Combine(siteRoot, ".well-known", "api-catalog"), outputs);
            Assert.DoesNotContain(Path.Combine(siteRoot, ".well-known", "mcp", "server-card.json"), outputs);
            Assert.DoesNotContain(Path.Combine(siteRoot, "agents.json"), outputs);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Verify_HonorsDisabledOptionalAgentReadinessChecks()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-agent-ready-disabled-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "sitemap.xml"),
                """
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc>https://example.test/</loc></url>
                </urlset>
                """);
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html lang="en">
                <head><title>Example</title><meta name="robots" content="index,follow"><script type="application/ld+json">{"@context":"https://schema.org","@type":["WebSite","Organization"],"name":"Example","sameAs":["https://example.test"],"publisher":{"@type":"Organization","name":"Example"},"dateModified":"2026-04-17"}</script></head>
                <body><header><nav><a href="/">Home</a></nav></header><main><h1>Example?</h1><p>Hello agents.</p></main><footer>Footer</footer></body>
                </html>
                """);

            var result = WebAgentReadiness.Verify(new WebAgentReadinessVerifyOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
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

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Checks.Select(check => $"{check.Status}: {check.Id} - {check.Message}")));
            Assert.DoesNotContain(result.Checks, check => string.Equals(check.Status, "fail", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Prepare_RejectsAgentOutputPathOutsideSiteRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-agent-ready-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "api"));
            File.WriteAllText(Path.Combine(root, "api", "index.json"), "{}");

            var ex = Assert.Throws<ArgumentException>(() => WebAgentReadiness.Prepare(new WebAgentReadinessPrepareOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                SiteName = "Example",
                AgentReadiness = new AgentReadinessSpec
                {
                    Enabled = true,
                    ApiCatalog = new AgentApiCatalogSpec { Enabled = true, OutputPath = "../outside-api-catalog" },
                    AgentSkills = new AgentSkillsDiscoverySpec { Enabled = false },
                    AgentsJson = new AgentDiscoveryDocumentSpec { Enabled = false }
                }
            }));

            Assert.Contains("outside the site root", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // Ignore cleanup failures in tests.
        }
    }
}
