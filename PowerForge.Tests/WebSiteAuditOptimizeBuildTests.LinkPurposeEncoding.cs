using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Tests;

public partial class WebSiteAuditOptimizeBuildTests
{
    [Theory]
    [InlineData("/café")]
    [InlineData("/old page")]
    [InlineData("/100%")]
    public void Audit_LinkPurposeEncoding_ResolvesRawAliasesFromGeneratedMetadata(string alias)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-link-alias-encoding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var content = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(content);
            File.WriteAllText(Path.Combine(content, "current.md"),
                $$"""
                ---
                title: Current
                slug: current
                aliases: ["{{alias}}"]
                ---

                <a href="{{System.Net.WebUtility.HtmlEncode(alias)}}">Guide</a>
                <a href="/current/">Guide</a>
                """);
            var theme = Path.Combine(root, "themes", "alias-encoding");
            Directory.CreateDirectory(Path.Combine(theme, "layouts"));
            File.WriteAllText(Path.Combine(theme, "layouts", "page.html"),
                """
                <!doctype html>
                <html>
                <head><title>Current</title>{{ canonical_html }}{{ head_html }}</head>
                <body><main>{{ content }}</main></body>
                </html>
                """);
            File.WriteAllText(Path.Combine(theme, "theme.json"),
                """
                { "name": "alias-encoding", "engine": "scriban", "defaultLayout": "page" }
                """);
            var spec = new SiteSpec
            {
                Name = "Alias encoding",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "alias-encoding",
                ThemesRoot = "themes",
                TrailingSlash = TrailingSlashMode.Always,
                Collections = new[]
                {
                    new CollectionSpec { Name = "pages", Input = "content/pages", Output = "/", DefaultLayout = "page" }
                }
            };
            var config = Path.Combine(root, "site.json");
            File.WriteAllText(config, "{}");
            var built = WebSiteBuilder.Build(spec, WebSitePlanner.Plan(spec, config), Path.Combine(root, "_site"));

            using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(built.OutputPath, "_powerforge", "redirects.json")));
            Assert.Contains(metadata.RootElement.GetProperty("redirects").EnumerateArray(),
                redirect => redirect.GetProperty("from").GetString() == alias);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = built.OutputPath,
                Include = new[] { "current/index.html" },
                CheckLinks = false,
                CheckAssets = false,
                NavRequired = false
            });
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
            Assert.DoesNotContain(result.Issues, issue => issue.Category.Equals("link-purpose", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("/caf%C3%A9", "/café")]
    [InlineData("/a%2Fb", "/a%2Fb")]
    [InlineData("/100%25", "/100%")]
    public void Audit_LinkPurposeEncoding_PreservesExistingEscapes(string redirectFrom, string href)
    {
        var result = AuditLinkPurposeFixture(
            "index.html", href, "/current",
            declaredSiteBaseUrl: "https://example.test",
            redirectsJson: JsonSerializer.Serialize(new
            {
                redirects = new[] { new { from = redirectFrom, to = "/current", status = 301 } }
            }));

        Assert.DoesNotContain(result.Issues, issue => issue.Category.Equals("link-purpose", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("/a%2Fb", "/a/b")]
    [InlineData("/a%252Fb", "/a%2Fb")]
    [InlineData("/100%2525", "/100%25")]
    public void Audit_LinkPurposeEncoding_KeepsReservedAndDoubleEscapesDistinct(string redirectFrom, string href)
    {
        var result = AuditLinkPurposeFixture(
            "index.html", href, "/current",
            declaredSiteBaseUrl: "https://example.test",
            redirectsJson: JsonSerializer.Serialize(new
            {
                redirects = new[] { new { from = redirectFrom, to = "/current", status = 301 } }
            }));

        Assert.Single(result.Issues, issue => issue.Category.Equals("link-purpose", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Audit_LinkPurposeEncoding_ResolvesRawTargetIntoEncodedNextRedirect()
    {
        var result = AuditLinkPurposeFixture(
            "index.html", "/legacy", "/current",
            declaredSiteBaseUrl: "https://example.test",
            redirectsJson:
            """
            {
              "redirects": [
                { "from": "/legacy", "to": "/café", "status": 301 },
                { "from": "/caf%C3%A9", "to": "/current", "status": 301 }
              ]
            }
            """);

        Assert.DoesNotContain(result.Issues, issue => issue.Category.Equals("link-purpose", StringComparison.OrdinalIgnoreCase));
    }
}
