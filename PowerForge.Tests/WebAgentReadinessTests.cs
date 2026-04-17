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
                    ApiCatalog = new AgentApiCatalogSpec { Enabled = true },
                    AgentSkills = new AgentSkillsDiscoverySpec { Enabled = true },
                    A2AAgentCard = new AgentA2ACardSpec { Enabled = true }
                }
            });

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Checks.Select(check => $"{check.Status}: {check.Id} - {check.Message}")));
            Assert.True(File.Exists(Path.Combine(root, "robots.txt")));
            Assert.True(File.Exists(Path.Combine(root, ".well-known", "api-catalog")));
            Assert.True(File.Exists(Path.Combine(root, ".well-known", "agent-skills", "index.json")));
            Assert.True(File.Exists(Path.Combine(root, "agents.json")));
            Assert.True(File.Exists(Path.Combine(root, ".well-known", "agent-card.json")));
            Assert.True(File.Exists(Path.Combine(root, "_headers")));

            var robots = File.ReadAllText(Path.Combine(root, "robots.txt"));
            Assert.Contains("Content-Signal: search=yes, ai-input=no, ai-train=no", robots, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Sitemap: https://example.test/sitemap.xml", robots, StringComparison.OrdinalIgnoreCase);

            var headers = File.ReadAllText(Path.Combine(root, "_headers"));
            Assert.Contains("rel=\"api-catalog\"", headers, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("application/linkset+json", headers, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Strict-Transport-Security", headers, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Access-Control-Allow-Origin", headers, StringComparison.OrdinalIgnoreCase);

            using var apiCatalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, ".well-known", "api-catalog")));
            Assert.True(apiCatalog.RootElement.TryGetProperty("linkset", out var linkset));
            Assert.True(linkset.GetArrayLength() > 0);
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
