using PowerForge.Web;

namespace PowerForge.Tests;

public class WebSiteStructuredDataProfilesTests
{
    [Fact]
    public void Build_EmitsFaqAndHowToStructuredData_WhenEnabled()
    {
        var root = CreateTempRoot("pf-web-structured-faq-howto-");
        try
        {
            WritePage(root, "index.md",
                """
                ---
                title: Release Workflow
                slug: index
                meta.faq.questions:
                  - "What does this workflow do?|Builds, tests, and publishes the module."
                  - "Can I run this locally?|Yes, use the same pipeline command."
                meta.howto.name: Publish Module
                meta.howto.description: End-to-end release flow.
                meta.howto.total_time: PT20M
                meta.howto.tools:
                  - ".NET SDK"
                  - "PowerShell 7"
                meta.howto.supplies:
                  - "Repository access"
                meta.howto.steps:
                  - "Build|Run dotnet build to compile binaries."
                  - "Test|Run dotnet test to validate regressions."
                ---

                Workflow details.
                """);

            var spec = BuildPagesSpec();
            spec.StructuredData = new StructuredDataSpec
            {
                Enabled = true,
                Breadcrumbs = false,
                FaqPage = true,
                HowTo = true
            };

            var html = BuildAndRead(root, spec, "index.html");
            Assert.Contains("\"@type\":\"FAQPage\"", html, StringComparison.Ordinal);
            Assert.Contains("\"What does this workflow do?\"", html, StringComparison.Ordinal);
            Assert.Contains("\"@type\":\"HowTo\"", html, StringComparison.Ordinal);
            Assert.Contains("\"@type\":\"HowToStep\"", html, StringComparison.Ordinal);
            Assert.Contains("\"totalTime\":\"PT20M\"", html, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Build_EmitsProductAndSoftwareApplicationStructuredData_WhenEnabled()
    {
        var root = CreateTempRoot("pf-web-structured-product-software-");
        try
        {
            WritePage(root, "index.md",
                """
                ---
                title: IntelligenceX Pro
                slug: index
                description: High-trust automation workflow package.
                meta.product.name: IntelligenceX Pro
                meta.product.brand: Evotec
                meta.product.sku: IX-PRO-01
                meta.product.price: 49.99
                meta.product.price_currency: USD
                meta.product.rating_value: 4.8
                meta.product.rating_count: 137
                meta.software.name: IntelligenceX CLI
                meta.software.application_category: DeveloperApplication
                meta.software.operating_system: Windows, Linux, macOS
                meta.software.version: 1.4.0
                meta.software.download_url: /downloads/ix-cli
                meta.software.price: 0
                meta.software.price_currency: USD
                ---

                Product details.
                """);

            var spec = BuildPagesSpec();
            spec.StructuredData = new StructuredDataSpec
            {
                Enabled = true,
                Breadcrumbs = false,
                Product = true,
                SoftwareApplication = true
            };

            var html = BuildAndRead(root, spec, "index.html");
            Assert.Contains("\"@type\":\"Product\"", html, StringComparison.Ordinal);
            Assert.Contains("\"sku\":\"IX-PRO-01\"", html, StringComparison.Ordinal);
            Assert.Contains("\"price\":\"49.99\"", html, StringComparison.Ordinal);
            Assert.Contains("\"@type\":\"SoftwareApplication\"", html, StringComparison.Ordinal);
            Assert.Contains("\"applicationCategory\":\"DeveloperApplication\"", html, StringComparison.Ordinal);
            Assert.Contains("\"downloadUrl\":\"https://example.test/downloads/ix-cli\"", html, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Build_PrefersNewsArticleStructuredData_ForNewsCollection_WhenEnabled()
    {
        var root = CreateTempRoot("pf-web-structured-news-");
        try
        {
            WritePage(root, "index.md",
                """
                ---
                title: Home
                slug: index
                ---

                Home.
                """);

            var blogPath = Path.Combine(root, "content", "blog");
            Directory.CreateDirectory(blogPath);
            File.WriteAllText(Path.Combine(blogPath, "dev-update.md"),
                """
                ---
                title: Dev Update
                slug: dev-update
                date: 2026-02-18
                ---

                Development notes.
                """);

            var newsPath = Path.Combine(root, "content", "news");
            Directory.CreateDirectory(newsPath);
            File.WriteAllText(Path.Combine(newsPath, "launch.md"),
                """
                ---
                title: Launch
                slug: launch
                date: 2026-02-18
                ---

                Launch announcement.
                """);

            var spec = BuildPagesSpec();
            spec.Collections = new[]
            {
                new CollectionSpec
                {
                    Name = "pages",
                    Input = "content/pages",
                    Output = "/"
                },
                new CollectionSpec
                {
                    Name = "blog",
                    Input = "content/blog",
                    Output = "/blog"
                },
                new CollectionSpec
                {
                    Name = "news",
                    Input = "content/news",
                    Output = "/news"
                }
            };
            spec.StructuredData = new StructuredDataSpec
            {
                Enabled = true,
                Breadcrumbs = false,
                Article = true,
                NewsArticle = true
            };

            var blogHtml = BuildAndRead(root, spec, Path.Combine("blog", "dev-update", "index.html"));
            Assert.Contains("\"@type\":\"Article\"", blogHtml, StringComparison.Ordinal);

            var newsHtml = BuildAndRead(root, spec, Path.Combine("news", "launch", "index.html"));
            Assert.Contains("\"@type\":\"NewsArticle\"", newsHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("\"@type\":\"Article\"", newsHtml, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(root);
        }
    }

    private static SiteSpec BuildPagesSpec()
    {
        return new SiteSpec
        {
            Name = "Example Site",
            BaseUrl = "https://example.test",
            ContentRoot = "content",
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

    private static string BuildAndRead(string root, SiteSpec spec, string relativeOutputFile)
    {
        var configPath = Path.Combine(root, "site.json");
        File.WriteAllText(configPath, "{}");
        var plan = WebSitePlanner.Plan(spec, configPath);
        var outPath = Path.Combine(root, "_site");
        WebSiteBuilder.Build(spec, plan, outPath);
        return File.ReadAllText(Path.Combine(outPath, relativeOutputFile));
    }

    private static void WritePage(string root, string fileName, string markdown)
    {
        var pagesPath = Path.Combine(root, "content", "pages");
        Directory.CreateDirectory(pagesPath);
        File.WriteAllText(Path.Combine(pagesPath, fileName), markdown);
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
