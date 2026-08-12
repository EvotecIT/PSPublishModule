using PowerForge.Web;

namespace PowerForge.Tests;

public partial class WebAgentReadinessTests
{
    [Fact]
    public void Prepare_EncodesDiscoveryRoutesInLinksAndHostHeaderMatchers()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-agent-ready-uri-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "sitemap.xml"),
                "<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"><url><loc>https://example.test/</loc></url></urlset>");
            File.WriteAllText(Path.Combine(root, "index.html"),
                "<!doctype html><html lang=\"en\"><head><title>Example</title></head><body><main><h1>Example</h1></main></body></html>");

            WebAgentReadiness.Prepare(new WebAgentReadinessPrepareOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                AgentReadiness = new AgentReadinessSpec
                {
                    Enabled = true,
                    Robots = false,
                    SecurityHeaders = new AgentSecurityHeadersSpec { Hsts = false },
                    ContentSignals = new AgentContentSignalsSpec { Enabled = false },
                    ApiCatalog = new AgentApiCatalogSpec
                    {
                        Enabled = true,
                        OutputPath = "discovery/api catalog.json",
                        Entries =
                        [
                            new AgentApiCatalogEntrySpec
                            {
                                Anchor = "/",
                                ServiceDoc = "/docs/",
                                Title = "Example docs"
                            }
                        ]
                    },
                    AgentSkills = new AgentSkillsDiscoverySpec { Enabled = false },
                    AgentsJson = new AgentDiscoveryDocumentSpec { Enabled = true },
                    MarkdownArtifacts = new AgentMarkdownArtifactsSpec { Enabled = false },
                    MarkdownNegotiation = false,
                    Apache = new AgentApacheSupportSpec { Enabled = true }
                }
            });

            Assert.True(File.Exists(Path.Combine(root, "discovery", "api catalog.json")));
            var headers = File.ReadAllText(Path.Combine(root, "_headers"));
            Assert.Contains("</discovery/api%20catalog.json>", headers, StringComparison.Ordinal);
            Assert.Contains("/discovery/api%20catalog.json" + Environment.NewLine + "  Content-Type:",
                headers, StringComparison.Ordinal);
            var apache = File.ReadAllText(Path.Combine(root, ".htaccess"));
            Assert.Contains("%{REQUEST_URI} == '/discovery/api%20catalog.json'", apache, StringComparison.Ordinal);
            using var agents = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "agents.json")));
            Assert.Equal("https://example.test/discovery/api%20catalog.json",
                agents.RootElement.GetProperty("resources").GetProperty("apiCatalog").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }
}
