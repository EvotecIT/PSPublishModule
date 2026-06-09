using System.Text.Json;
using System.Text.RegularExpressions;
using PowerForge.Web;

namespace PowerForge.Tests;

public class WebSiteCrawlPolicyTests
{
    [Fact]
    public void Build_AppliesRouteScopedCrawlPolicyAndWritesDiagnostics()
    {
        var root = CreateTempRoot("pf-web-crawl-policy-");
        try
        {
            WritePage(root, "content/pages/index.md",
                """
                ---
                title: Home
                slug: index
                ---

                Home.
                """);
            WritePage(root, "content/pages/search.md",
                """
                ---
                title: Search
                slug: search
                ---

                Search page.
                """);

            var spec = CreateSpec(new CrawlPolicySpec
            {
                Enabled = true,
                DefaultRobots = "index,follow",
                Bots = new[]
                {
                    new CrawlBotDirectiveSpec
                    {
                        Name = "googlebot",
                        Directives = "index,follow,max-image-preview:large"
                    }
                },
                Rules = new[]
                {
                    new CrawlRuleSpec
                    {
                        Name = "search-noindex",
                        Match = "/search/*",
                        MatchType = "wildcard",
                        Robots = "noindex,follow",
                        Bots = new[]
                        {
                            new CrawlBotDirectiveSpec
                            {
                                Name = "googlebot",
                                Directives = "noindex,follow"
                            }
                        }
                    }
                }
            });

            var outPath = Build(root, spec);
            var homeHtml = File.ReadAllText(Path.Combine(outPath, "index.html"));
            var searchHtml = File.ReadAllText(Path.Combine(outPath, "search", "index.html"));

            Assert.Equal("index,follow", ReadMetaContent(homeHtml, "robots"));
            Assert.Equal("index,follow,max-image-preview:large", ReadMetaContent(homeHtml, "googlebot"));
            Assert.Equal("noindex,follow", ReadMetaContent(searchHtml, "robots"));
            Assert.Equal("noindex,follow", ReadMetaContent(searchHtml, "googlebot"));

            var diagnosticsPath = Path.Combine(outPath, "_powerforge", "crawl-policy.json");
            Assert.True(File.Exists(diagnosticsPath), "Expected _powerforge/crawl-policy.json.");
            using var doc = JsonDocument.Parse(File.ReadAllText(diagnosticsPath));
            var pages = doc.RootElement.GetProperty("pages").EnumerateArray().ToArray();
            var searchEntry = pages.First(page =>
                page.TryGetProperty("outputPath", out var path) &&
                string.Equals(path.GetString(), "/search/", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("search-noindex", searchEntry.GetProperty("rule").GetString());
            Assert.Equal("noindex,follow", searchEntry.GetProperty("robots").GetString());
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Build_PageRobotsOverridesCrawlPolicyDefaults()
    {
        var root = CreateTempRoot("pf-web-crawl-policy-override-");
        try
        {
            WritePage(root, "content/pages/search.md",
                """
                ---
                title: Search
                slug: search
                meta.robots: "index,follow"
                meta.googlebot: "index,follow"
                ---

                Search page.
                """);

            var spec = CreateSpec(new CrawlPolicySpec
            {
                Enabled = true,
                DefaultRobots = "noindex,follow",
                Bots = new[]
                {
                    new CrawlBotDirectiveSpec
                    {
                        Name = "googlebot",
                        Directives = "noindex,follow"
                    }
                },
                Rules = new[]
                {
                    new CrawlRuleSpec
                    {
                        Name = "all-noindex",
                        Match = "/*",
                        MatchType = "wildcard",
                        Robots = "noindex,nofollow"
                    }
                }
            });

            var outPath = Build(root, spec);
            var searchHtml = File.ReadAllText(Path.Combine(outPath, "search", "index.html"));

            Assert.Equal("index,follow", ReadMetaContent(searchHtml, "robots"));
            Assert.Equal("index,follow", ReadMetaContent(searchHtml, "googlebot"));
        }
        finally
        {
            Cleanup(root);
        }
    }

    private static SiteSpec CreateSpec(CrawlPolicySpec crawlPolicy)
    {
        return new SiteSpec
        {
            Name = "Example Site",
            BaseUrl = "https://example.test",
            ContentRoot = "content",
            Seo = new SeoSpec
            {
                CrawlPolicy = crawlPolicy
            },
            Collections = new[]
            {
                new CollectionSpec
                {
                    Name = "pages",
                    Input = "content/pages",
                    Output = "/"
                }
            }
        };
    }

    private static string Build(string root, SiteSpec spec)
    {
        var configPath = Path.Combine(root, "site.json");
        File.WriteAllText(configPath, "{}");
        var plan = WebSitePlanner.Plan(spec, configPath);
        var outPath = Path.Combine(root, "_site");
        WebSiteBuilder.Build(spec, plan, outPath);
        return outPath;
    }

    private static string ReadMetaContent(string html, string name)
    {
        var pattern = "<meta\\s+name=\"" + Regex.Escape(name) + "\"\\s+content=\"([^\"]*)\"";
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"Expected meta '{name}' tag.");
        return System.Web.HttpUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static void WritePage(string root, string relativePath, string markdown)
    {
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(fullPath, markdown);
    }

    private static string CreateTempRoot(string prefix)
    {
        var root = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Cleanup(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}
