using System.Text.Json;
using System.Text.RegularExpressions;
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
    public void Prepare_IncludesHostedProjectApiReferencesWhenEnabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-agent-ready-project-api-" + Guid.NewGuid().ToString("N"));
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

            var gpoApiRoot = Path.Combine(root, "projects", "gpozaurr", "api");
            Directory.CreateDirectory(gpoApiRoot);
            File.WriteAllText(Path.Combine(gpoApiRoot, "index.html"), "<!doctype html><title>GPOZaurr API</title>");
            File.WriteAllText(Path.Combine(gpoApiRoot, "index.json"), "{}");

            var plainApiRoot = Path.Combine(root, "projects", "plain-project", "api");
            Directory.CreateDirectory(plainApiRoot);
            File.WriteAllText(Path.Combine(plainApiRoot, "index.html"), "<!doctype html><title>Plain API</title>");

            var unknownApiRoot = Path.Combine(root, "projects", "unknown-module", "api");
            Directory.CreateDirectory(unknownApiRoot);
            File.WriteAllText(Path.Combine(unknownApiRoot, "index.html"), "<!doctype html><title>Unknown API</title>");

            var numericApiRoot = Path.Combine(root, "projects", "123-module", "api");
            Directory.CreateDirectory(numericApiRoot);
            File.WriteAllText(Path.Combine(numericApiRoot, "index.html"), "<!doctype html><title>Numeric API</title>");

            var duplicateApiRoot = Path.Combine(root, "projects", "duplicate-project", "api");
            Directory.CreateDirectory(duplicateApiRoot);
            File.WriteAllText(Path.Combine(duplicateApiRoot, "index.html"), "<!doctype html><title>Duplicate API</title>");

            var catalogRoot = Path.Combine(root, "data", "projects");
            Directory.CreateDirectory(catalogRoot);
            File.WriteAllText(Path.Combine(catalogRoot, "catalog.json"),
                """
                {
                  "projects": [
                    { "slug": "gpozaurr", "name": "GPOZaurr", "links": { "apiPowerShell": "/projects/gpozaurr/api/" } },
                    { "slug": "plain-project", "name": "Plain Project", "links": { "apiPowerShell": null } },
                    { "slug": "officeimo", "name": "OfficeIMO", "links": { "apiPowerShell": "https://officeimo.com/api/powershell/" } }
                  ]
                }
                """);

            var result = WebAgentReadiness.Prepare(new WebAgentReadinessPrepareOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                SiteName = "Example",
                AgentReadiness = new AgentReadinessSpec
                {
                    Enabled = true,
                    Robots = false,
                    LinkHeaders = false,
                    SecurityHeaders = new AgentSecurityHeadersSpec { Enabled = false },
                    ApiCatalog = new AgentApiCatalogSpec
                    {
                        Enabled = true,
                        IncludeProjectApiReferences = true,
                        Entries = new[]
                        {
                            new AgentApiCatalogEntrySpec
                            {
                                Anchor = "/projects/api-suite/",
                                ServiceDoc = "/projects/api-suite/",
                                Title = "API suite"
                            },
                            new AgentApiCatalogEntrySpec
                            {
                                Anchor = "/projects/duplicate-project/api/",
                                ServiceDoc = "/projects/duplicate-project/api/custom/",
                                Title = "Explicit duplicate API"
                            }
                        }
                    },
                    AgentSkills = new AgentSkillsDiscoverySpec { Enabled = false },
                    AgentsJson = new AgentDiscoveryDocumentSpec { Enabled = false }
                }
            });

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Checks.Select(check => $"{check.Status}: {check.Id} - {check.Message}")));
            using var apiCatalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, ".well-known", "api-catalog")));
            var linkset = apiCatalog.RootElement.GetProperty("linkset").EnumerateArray().ToArray();
            var anchors = linkset.Select(static item => item.GetProperty("anchor").GetString()).ToArray();

            Assert.Contains("https://example.test/projects/api-suite/", anchors);
            Assert.Contains("https://example.test/projects/gpozaurr/api/", anchors);
            Assert.Contains("https://example.test/projects/plain-project/api/", anchors);
            Assert.Contains("https://example.test/projects/unknown-module/api/", anchors);
            Assert.Contains("https://example.test/projects/123-module/api/", anchors);
            Assert.DoesNotContain("https://officeimo.com/api/powershell/", anchors);
            Assert.Single(anchors, static anchor => anchor == "https://example.test/projects/duplicate-project/api/");

            var gpo = linkset.Single(static item => item.GetProperty("anchor").GetString() == "https://example.test/projects/gpozaurr/api/");
            Assert.Equal("https://example.test/projects/gpozaurr/api/index.json", gpo.GetProperty("service-desc")[0].GetProperty("href").GetString());
            Assert.Equal("GPOZaurr PowerShell API reference", gpo.GetProperty("service-doc")[0].GetProperty("title").GetString());

            var plain = linkset.Single(static item => item.GetProperty("anchor").GetString() == "https://example.test/projects/plain-project/api/");
            Assert.False(plain.TryGetProperty("service-desc", out _));

            var unknown = linkset.Single(static item => item.GetProperty("anchor").GetString() == "https://example.test/projects/unknown-module/api/");
            Assert.Equal("Unknown Module API reference", unknown.GetProperty("service-doc")[0].GetProperty("title").GetString());

            var numeric = linkset.Single(static item => item.GetProperty("anchor").GetString() == "https://example.test/projects/123-module/api/");
            Assert.Equal("123 Module API reference", numeric.GetProperty("service-doc")[0].GetProperty("title").GetString());

            var duplicate = linkset.Single(static item => item.GetProperty("anchor").GetString() == "https://example.test/projects/duplicate-project/api/");
            Assert.Equal("Explicit duplicate API", duplicate.GetProperty("service-doc")[0].GetProperty("title").GetString());
            Assert.Contains(result.Warnings, warning => warning.Contains("duplicate-project", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Prepare_WithAbsoluteProjectCatalogPath_WarnsAndStillInfersProjectApi()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-agent-ready-project-api-catalog-path-" + Guid.NewGuid().ToString("N"));
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

            var apiRoot = Path.Combine(root, "projects", "safe-module", "api");
            Directory.CreateDirectory(apiRoot);
            File.WriteAllText(Path.Combine(apiRoot, "index.html"), "<!doctype html><title>Safe API</title>");

            var result = WebAgentReadiness.Prepare(new WebAgentReadinessPrepareOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                SiteName = "Example",
                AgentReadiness = new AgentReadinessSpec
                {
                    Enabled = true,
                    Robots = false,
                    LinkHeaders = false,
                    SecurityHeaders = new AgentSecurityHeadersSpec { Enabled = false },
                    ApiCatalog = new AgentApiCatalogSpec
                    {
                        Enabled = true,
                        IncludeProjectApiReferences = true,
                        ProjectCatalogPath = Path.Combine(Path.GetTempPath(), "outside-project-catalog.json")
                    },
                    AgentSkills = new AgentSkillsDiscoverySpec { Enabled = false },
                    AgentsJson = new AgentDiscoveryDocumentSpec { Enabled = false }
                }
            });

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Checks.Select(check => $"{check.Status}: {check.Id} - {check.Message}")));
            Assert.Contains(result.Warnings, warning => warning.Contains("relative to the site root", StringComparison.OrdinalIgnoreCase));
            using var apiCatalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, ".well-known", "api-catalog")));
            var linkset = apiCatalog.RootElement.GetProperty("linkset").EnumerateArray().ToArray();
            var entry = linkset.Single(static item => item.GetProperty("anchor").GetString() == "https://example.test/projects/safe-module/api/");
            Assert.Equal("Safe Module API reference", entry.GetProperty("service-doc")[0].GetProperty("title").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Prepare_WithMalformedProjectCatalog_WarnsAndStillInfersProjectApi()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-agent-ready-project-api-bad-catalog-" + Guid.NewGuid().ToString("N"));
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

            var apiRoot = Path.Combine(root, "projects", "bad-catalog-module", "api");
            Directory.CreateDirectory(apiRoot);
            File.WriteAllText(Path.Combine(apiRoot, "index.html"), "<!doctype html><title>Bad Catalog API</title>");

            var catalogRoot = Path.Combine(root, "data", "projects");
            Directory.CreateDirectory(catalogRoot);
            File.WriteAllText(Path.Combine(catalogRoot, "catalog.json"), "{ this is not json");

            var result = WebAgentReadiness.Prepare(new WebAgentReadinessPrepareOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                SiteName = "Example",
                AgentReadiness = new AgentReadinessSpec
                {
                    Enabled = true,
                    Robots = false,
                    LinkHeaders = false,
                    SecurityHeaders = new AgentSecurityHeadersSpec { Enabled = false },
                    ApiCatalog = new AgentApiCatalogSpec
                    {
                        Enabled = true,
                        IncludeProjectApiReferences = true
                    },
                    AgentSkills = new AgentSkillsDiscoverySpec { Enabled = false },
                    AgentsJson = new AgentDiscoveryDocumentSpec { Enabled = false }
                }
            });

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Checks.Select(check => $"{check.Status}: {check.Id} - {check.Message}")));
            Assert.Contains(result.Warnings, warning => warning.Contains("Could not read project catalog", StringComparison.OrdinalIgnoreCase));
            using var apiCatalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, ".well-known", "api-catalog")));
            var linkset = apiCatalog.RootElement.GetProperty("linkset").EnumerateArray().ToArray();
            var entry = linkset.Single(static item => item.GetProperty("anchor").GetString() == "https://example.test/projects/bad-catalog-module/api/");
            Assert.Equal("Bad Catalog Module API reference", entry.GetProperty("service-doc")[0].GetProperty("title").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Prepare_WithProjectCatalogTraversalPath_WarnsAndStillInfersProjectApi()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-agent-ready-project-api-traversal-" + Guid.NewGuid().ToString("N"));
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

            var apiRoot = Path.Combine(root, "projects", "traversal-module", "api");
            Directory.CreateDirectory(apiRoot);
            File.WriteAllText(Path.Combine(apiRoot, "index.html"), "<!doctype html><title>Traversal API</title>");

            var result = WebAgentReadiness.Prepare(new WebAgentReadinessPrepareOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                SiteName = "Example",
                AgentReadiness = new AgentReadinessSpec
                {
                    Enabled = true,
                    Robots = false,
                    LinkHeaders = false,
                    SecurityHeaders = new AgentSecurityHeadersSpec { Enabled = false },
                    ApiCatalog = new AgentApiCatalogSpec
                    {
                        Enabled = true,
                        IncludeProjectApiReferences = true,
                        ProjectCatalogPath = "../outside-project-catalog.json"
                    },
                    AgentSkills = new AgentSkillsDiscoverySpec { Enabled = false },
                    AgentsJson = new AgentDiscoveryDocumentSpec { Enabled = false }
                }
            });

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Checks.Select(check => $"{check.Status}: {check.Id} - {check.Message}")));
            Assert.Contains(result.Warnings, warning => warning.Contains("resolves outside the site root", StringComparison.OrdinalIgnoreCase));
            using var apiCatalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, ".well-known", "api-catalog")));
            var linkset = apiCatalog.RootElement.GetProperty("linkset").EnumerateArray().ToArray();
            var entry = linkset.Single(static item => item.GetProperty("anchor").GetString() == "https://example.test/projects/traversal-module/api/");
            Assert.Equal("Traversal Module API reference", entry.GetProperty("service-doc")[0].GetProperty("title").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Prepare_DoesNotInferHostedProjectApiReferencesWhenDisabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-agent-ready-project-api-disabled-" + Guid.NewGuid().ToString("N"));
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

            var apiRoot = Path.Combine(root, "projects", "disabled-module", "api");
            Directory.CreateDirectory(apiRoot);
            File.WriteAllText(Path.Combine(apiRoot, "index.html"), "<!doctype html><title>Disabled API</title>");

            var result = WebAgentReadiness.Prepare(new WebAgentReadinessPrepareOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                SiteName = "Example",
                AgentReadiness = new AgentReadinessSpec
                {
                    Enabled = true,
                    Robots = false,
                    LinkHeaders = false,
                    SecurityHeaders = new AgentSecurityHeadersSpec { Enabled = false },
                    ApiCatalog = new AgentApiCatalogSpec
                    {
                        Enabled = true,
                        IncludeProjectApiReferences = false,
                        Entries = new[]
                        {
                            new AgentApiCatalogEntrySpec
                            {
                                Anchor = "/api/",
                                ServiceDoc = "/api/",
                                Title = "Explicit API"
                            }
                        }
                    },
                    AgentSkills = new AgentSkillsDiscoverySpec { Enabled = false },
                    AgentsJson = new AgentDiscoveryDocumentSpec { Enabled = false }
                }
            });

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Checks.Select(check => $"{check.Status}: {check.Id} - {check.Message}")));
            using var apiCatalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, ".well-known", "api-catalog")));
            var anchors = apiCatalog.RootElement.GetProperty("linkset").EnumerateArray()
                .Select(static item => item.GetProperty("anchor").GetString())
                .ToArray();
            Assert.Contains("https://example.test/api/", anchors);
            Assert.DoesNotContain("https://example.test/projects/disabled-module/api/", anchors);
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
                <head><title>Example Home</title><meta name="robots" content="index,follow"><script type="application/ld+json">{"@context":"https://schema.org","@type":["WebSite","Organization"],"name":"Example","sameAs":["https://example.test"],"publisher":{"@type":"Organization","name":"Example"},"dateModified":"2020-01-01"}</script></head>
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
    public void Prepare_HonorsMarkdownArtifactExtensionAndSkipsApiFragments()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-agent-ready-markdown-extension-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "api-fragments"));

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
                <head><title>Example Home</title><meta name="robots" content="index,follow"><script type="application/ld+json">{"@context":"https://schema.org","@type":["WebSite","Organization"],"name":"Example","sameAs":["https://example.test"],"publisher":{"@type":"Organization","name":"Example"},"dateModified":"2020-01-01"}</script></head>
                <body><main><h1>Welcome</h1><p>Hello agents.</p></main></body>
                </html>
                """);
            File.WriteAllText(Path.Combine(root, "api-fragments", "index.html"),
                """
                <!doctype html>
                <html><body><main><h1>Fragment</h1></main></body></html>
                """);

            var result = WebAgentReadiness.Prepare(new WebAgentReadinessPrepareOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                SiteName = "Example",
                AgentReadiness = new AgentReadinessSpec
                {
                    Enabled = true,
                    MarkdownArtifacts = new AgentMarkdownArtifactsSpec { Enabled = true, Extension = ".markdown" },
                    MarkdownNegotiation = false,
                    ApiCatalog = new AgentApiCatalogSpec { Enabled = false },
                    AgentSkills = new AgentSkillsDiscoverySpec { Enabled = false },
                    AgentsJson = new AgentDiscoveryDocumentSpec { Enabled = false }
                }
            });

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Checks.Select(check => $"{check.Status}: {check.Id} - {check.Message}")));
            Assert.True(File.Exists(Path.Combine(root, "index.markdown")));
            Assert.False(File.Exists(Path.Combine(root, "api-fragments", "index.markdown")));

            var headers = File.ReadAllText(Path.Combine(root, "_headers"));
            Assert.Contains("</index.markdown>; rel=\"alternate\"; type=\"text/markdown\"", headers, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("/index.markdown" + Environment.NewLine + "  Content-Type: text/markdown; charset=utf-8", headers, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/api-fragments/index.markdown", headers, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Prepare_WritesApacheHeadersAndMarkdownNegotiationRulesWhenEnabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-agent-ready-apache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "sitemap.xml"),
                """
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc>https://example.test/</loc></url>
                </urlset>
                """);
            File.WriteAllText(Path.Combine(root, ".htaccess"),
                """
                # Generated by PowerForge.Web
                ErrorDocument 404 /404.html
                """);
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html lang="en">
                <head><title>Example Home</title><meta name="robots" content="index,follow"><script type="application/ld+json">{"@context":"https://schema.org","@type":["WebSite","Organization"],"name":"Example","sameAs":["https://example.test"],"publisher":{"@type":"Organization","name":"Example"},"dateModified":"2020-01-01"}</script></head>
                <body><main><h1>Welcome</h1><p>Hello agents.</p></main></body>
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
                    ContentSignals = new AgentContentSignalsSpec { Search = true, AiInput = true, AiTrain = false },
                    ApiCatalog = new AgentApiCatalogSpec
                    {
                        Enabled = true,
                        Entries = new[]
                        {
                            new AgentApiCatalogEntrySpec { Anchor = "/api/", ServiceDesc = "/api/index.json", ServiceDoc = "/api/" }
                        }
                    },
                    AgentSkills = new AgentSkillsDiscoverySpec { Enabled = false },
                    AgentsJson = new AgentDiscoveryDocumentSpec { Enabled = false },
                    MarkdownArtifacts = new AgentMarkdownArtifactsSpec { Enabled = true },
                    MarkdownNegotiation = true,
                    SecurityHeaders = new AgentSecurityHeadersSpec
                    {
                        ContentSecurityPolicyValue = "default-src 'self'\r\nHeader set X-Injected \"bad\""
                    },
                    Apache = new AgentApacheSupportSpec { Enabled = true }
                }
            });

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Checks.Select(check => $"{check.Status}: {check.Id} - {check.Message}")));

            var apache = File.ReadAllText(Path.Combine(root, ".htaccess"));
            Assert.Contains("ErrorDocument 404 /404.html", apache, StringComparison.Ordinal);
            Assert.Contains("Header always add Link \"</.well-known/api-catalog>; rel=\\\"api-catalog\\\"; type=\\\"application/linkset+json\\\"\"", apache, StringComparison.Ordinal);
            Assert.Contains("Header set Content-Signal \"search=yes, ai-input=yes, ai-train=no\"", apache, StringComparison.Ordinal);
            Assert.DoesNotContain(Environment.NewLine + "  Header set X-Injected", apache, StringComparison.Ordinal);
            Assert.Contains("Header set Content-Security-Policy \"default-src 'self'Header set X-Injected \\\"bad\\\"\"", apache, StringComparison.Ordinal);
            Assert.Contains("RewriteCond %{HTTP_ACCEPT} \"(^|,|;)[[:space:]]*text/markdown\" [NC]", apache, StringComparison.Ordinal);
            Assert.Contains("RewriteRule ^$ /index.md [L,T=text/markdown]", apache, StringComparison.Ordinal);
            Assert.Contains("<If \"%{REQUEST_URI} == '/.well-known/api-catalog'\">", apache, StringComparison.Ordinal);
            Assert.Contains("Header always set Content-Type \"application/linkset+json; profile=\\\"https://www.rfc-editor.org/info/rfc9727\\\"\"", apache, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Prepare_ReplacesExistingApacheManagedBlock()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-agent-ready-apache-idempotent-" + Guid.NewGuid().ToString("N"));
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
                <head><title>Example</title><meta name="robots" content="index,follow"><script type="application/ld+json">{"@context":"https://schema.org","@type":"Organization","name":"Example","sameAs":["https://example.test"],"dateModified":"2026-04-17"}</script></head>
                <body><main><h1>Example</h1><p>Hello agents.</p></main></body>
                </html>
                """);
            File.WriteAllText(Path.Combine(root, ".htaccess"),
                """
                # Keep this custom line
                # BEGIN PowerForge Agent Readiness
                Header set X-Stale "yes"
                # END PowerForge Agent Readiness
                """);

            var options = new WebAgentReadinessPrepareOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                SiteName = "Example",
                AgentReadiness = new AgentReadinessSpec
                {
                    Enabled = true,
                    LinkHeaders = false,
                    SecurityHeaders = new AgentSecurityHeadersSpec { Enabled = false },
                    ContentSignals = new AgentContentSignalsSpec { Enabled = false },
                    ApiCatalog = new AgentApiCatalogSpec { Enabled = false },
                    AgentSkills = new AgentSkillsDiscoverySpec { Enabled = false },
                    AgentsJson = new AgentDiscoveryDocumentSpec { Enabled = false },
                    MarkdownArtifacts = new AgentMarkdownArtifactsSpec { Enabled = false },
                    MarkdownNegotiation = false,
                    Apache = new AgentApacheSupportSpec { Enabled = true }
                }
            };

            var first = WebAgentReadiness.Prepare(options);
            var second = WebAgentReadiness.Prepare(options);

            Assert.True(first.Success, string.Join(Environment.NewLine, first.Checks.Select(check => $"{check.Status}: {check.Id} - {check.Message}")));
            Assert.True(second.Success, string.Join(Environment.NewLine, second.Checks.Select(check => $"{check.Status}: {check.Id} - {check.Message}")));

            var apache = File.ReadAllText(Path.Combine(root, ".htaccess"));
            Assert.Contains("# Keep this custom line", apache, StringComparison.Ordinal);
            Assert.DoesNotContain("X-Stale", apache, StringComparison.Ordinal);
            Assert.Single(Regex.Matches(apache, "# BEGIN PowerForge Agent Readiness").Cast<Match>());
            Assert.Single(Regex.Matches(apache, "# END PowerForge Agent Readiness").Cast<Match>());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Prepare_WritesApacheVaryHeaderWhenOnlyMarkdownNegotiationIsEnabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-agent-ready-apache-vary-" + Guid.NewGuid().ToString("N"));
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
                <head><title>Example</title><meta name="robots" content="index,follow"><script type="application/ld+json">{"@context":"https://schema.org","@type":"Organization","name":"Example","sameAs":["https://example.test"],"dateModified":"2026-04-17"}</script></head>
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
                    Robots = false,
                    LinkHeaders = false,
                    SecurityHeaders = new AgentSecurityHeadersSpec { Enabled = false },
                    ContentSignals = new AgentContentSignalsSpec { Enabled = false },
                    ApiCatalog = new AgentApiCatalogSpec { Enabled = false },
                    AgentSkills = new AgentSkillsDiscoverySpec { Enabled = false },
                    AgentsJson = new AgentDiscoveryDocumentSpec { Enabled = false },
                    MarkdownArtifacts = new AgentMarkdownArtifactsSpec { Enabled = true },
                    MarkdownNegotiation = true,
                    Apache = new AgentApacheSupportSpec
                    {
                        Enabled = true,
                        LinkHeaders = false,
                        ContentSignalsHeader = false,
                        DiscoveryResourceHeaders = false,
                        MarkdownNegotiation = true
                    }
                }
            });

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Checks.Select(check => $"{check.Status}: {check.Id} - {check.Message}")));

            var apache = File.ReadAllText(Path.Combine(root, ".htaccess"));
            Assert.Contains("<IfModule mod_headers.c>", apache, StringComparison.Ordinal);
            Assert.Contains("Header merge Vary \"Accept\"", apache, StringComparison.Ordinal);
            Assert.Contains("RewriteRule ^$ /index.md [L,T=text/markdown]", apache, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Verify_PassesSecurityHeadersFromApacheConfig()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-agent-ready-apache-security-" + Guid.NewGuid().ToString("N"));
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
                <head><title>Example</title><meta name="robots" content="index,follow"><script type="application/ld+json">{"@context":"https://schema.org","@type":"Organization","name":"Example","sameAs":["https://example.test"],"dateModified":"2026-04-17"}</script></head>
                <body><header><nav><a href="/">Home</a></nav></header><main><h1>Example?</h1><p>Hello agents.</p></main><footer>Footer</footer></body>
                </html>
                """);
            File.WriteAllText(Path.Combine(root, ".htaccess"),
                """
                <IfModule mod_headers.c>
                  Header set Strict-Transport-Security "max-age=31536000"
                  Header set Content-Security-Policy "default-src 'self'; frame-ancestors 'none'"
                  Header set X-Content-Type-Options "nosniff"
                  Header set X-Frame-Options "DENY"
                  Header set Referrer-Policy "strict-origin-when-cross-origin"
                  Header set Access-Control-Allow-Origin "*"
                </IfModule>
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
                    SecurityHeaders = new AgentSecurityHeadersSpec { Enabled = true },
                    ContentSignals = new AgentContentSignalsSpec { Enabled = false },
                    ApiCatalog = new AgentApiCatalogSpec { Enabled = false },
                    AgentSkills = new AgentSkillsDiscoverySpec { Enabled = false },
                    AgentsJson = new AgentDiscoveryDocumentSpec { Enabled = false },
                    MarkdownNegotiation = false,
                    Apache = new AgentApacheSupportSpec { Enabled = true }
                }
            });

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Checks.Select(check => $"{check.Status}: {check.Id} - {check.Message}")));
            Assert.Contains(result.Checks, check => check.Id == "security-hsts" && check.Status == "pass");
            Assert.Contains(result.Checks, check => check.Id == "security-csp" && check.Status == "pass");
            Assert.Contains(result.Checks, check => check.Id == "security-cors" && check.Status == "pass");
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
            Directory.CreateDirectory(Path.Combine(siteRoot, "api-fragments"));
            File.WriteAllText(Path.Combine(siteRoot, "index.html"), "<!doctype html><html><body><main><h1>Home</h1></main></body></html>");
            File.WriteAllText(Path.Combine(siteRoot, "api-fragments", "index.html"), "<!doctype html><html><body><main><h1>Fragment</h1></main></body></html>");
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
            Assert.DoesNotContain(Path.Combine(siteRoot, "api-fragments", "index.md"), outputs);
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
    public void Verify_IgnoresApacheConfigWhenApacheSupportIsDisabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-agent-ready-apache-disabled-" + Guid.NewGuid().ToString("N"));
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
                <head><title>Example</title><meta name="robots" content="index,follow"><script type="application/ld+json">{"@context":"https://schema.org","@type":"Organization","name":"Example","sameAs":["https://example.test"],"dateModified":"2026-04-17"}</script></head>
                <body><header><nav><a href="/">Home</a></nav></header><main><h1>Example</h1><p>Hello agents.</p></main><footer>Footer</footer></body>
                </html>
                """);
            File.WriteAllText(Path.Combine(root, ".htaccess"),
                """
                <IfModule mod_headers.c>
                  Header set Strict-Transport-Security "max-age=31536000"
                </IfModule>
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
                    SecurityHeaders = new AgentSecurityHeadersSpec
                    {
                        Enabled = true,
                        Hsts = true,
                        ContentSecurityPolicy = false,
                        XContentTypeOptions = false,
                        XFrameOptions = false,
                        ReferrerPolicy = false,
                        CorsForWellKnown = false
                    },
                    ContentSignals = new AgentContentSignalsSpec { Enabled = false },
                    ApiCatalog = new AgentApiCatalogSpec { Enabled = false },
                    AgentSkills = new AgentSkillsDiscoverySpec { Enabled = false },
                    AgentsJson = new AgentDiscoveryDocumentSpec { Enabled = false },
                    MarkdownNegotiation = false,
                    Apache = new AgentApacheSupportSpec { Enabled = false }
                }
            });

            Assert.Contains(result.Checks, check => check.Id == "security-hsts" && check.Status == "fail");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Verify_PassesWebMcpWhenDeclarativeToolAnnotationsExist()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-agent-ready-webmcp-" + Guid.NewGuid().ToString("N"));
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
                <head><title>Example</title><meta name="robots" content="index,follow"><script type="application/ld+json">{"@context":"https://schema.org","@type":"Organization","name":"Example","sameAs":["https://example.test"],"dateModified":"2026-04-17"}</script></head>
                <body><header><nav><a href="/">Home</a></nav></header><main><h1>Example?</h1><form tool-name="site-search" tool-description="Search public documentation"><input name="q" tool-param-description="Search query"></form></main><footer>Footer</footer></body>
                </html>
                """);

            var result = WebAgentReadiness.Verify(new WebAgentReadinessVerifyOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                AgentReadiness = new AgentReadinessSpec
                {
                    Enabled = true,
                    WebMcp = true,
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
            Assert.Contains(result.Checks, check => check.Id == "webmcp" && check.Status == "pass");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Verify_DoesNotPassWebMcpForCommentsOrDataAttributes()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-agent-ready-webmcp-false-" + Guid.NewGuid().ToString("N"));
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
                <head><title>Example</title><meta name="robots" content="index,follow"><script type="application/ld+json">{"@context":"https://schema.org","@type":"Organization","name":"Example","sameAs":["https://example.test"],"dateModified":"2026-04-17"}</script></head>
                <body><header><nav><a href="/">Home</a></nav></header><main><h1>Example?</h1><!-- tool-name="site-search" --><button data-tool-name="copy">Copy</button></main><footer>Footer</footer></body>
                </html>
                """);

            var result = WebAgentReadiness.Verify(new WebAgentReadinessVerifyOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                AgentReadiness = new AgentReadinessSpec
                {
                    Enabled = true,
                    WebMcp = true,
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

            Assert.Contains(result.Checks, check => check.Id == "webmcp" && check.Status == "fail");
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

    [Fact]
    public void Prepare_RejectsApacheOutputPathOutsideSiteRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-agent-ready-apache-path-" + Guid.NewGuid().ToString("N"));
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
                <head><title>Example</title><meta name="robots" content="index,follow"><script type="application/ld+json">{"@context":"https://schema.org","@type":"Organization","name":"Example","sameAs":["https://example.test"],"dateModified":"2026-04-17"}</script></head>
                <body><header><nav><a href="/">Home</a></nav></header><main><h1>Example?</h1><p>Hello agents.</p></main><footer>Footer</footer></body>
                </html>
                """);

            var ex = Assert.Throws<ArgumentException>(() => WebAgentReadiness.Prepare(new WebAgentReadinessPrepareOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                SiteName = "Example",
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
                    MarkdownNegotiation = false,
                    Apache = new AgentApacheSupportSpec { Enabled = true, OutputPath = "../outside.htaccess" }
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
