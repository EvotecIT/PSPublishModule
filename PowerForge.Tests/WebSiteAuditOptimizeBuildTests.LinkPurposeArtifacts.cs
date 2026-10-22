using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Tests;

public partial class WebSiteAuditOptimizeBuildTests
{
    [Theory]
    [InlineData("https://pl.example.test/install/", false)]
    [InlineData("https://example.test/install/", true)]
    public void Audit_LinkPurposeDestinations_UseDeclaredLanguageOriginForRootBuild(
        string absoluteHref, bool expectedDifferentDestination)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-link-language-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var content = Path.Combine(root, "content", "docs", "pl");
            Directory.CreateDirectory(content);
            File.WriteAllText(Path.Combine(content, "index.md"),
                $$"""
                ---
                title: Links
                ---

                # Links

                [Guide](/install/)

                [Guide]({{absoluteHref}})
                """);
            var theme = Path.Combine(root, "themes", "link-purpose");
            Directory.CreateDirectory(Path.Combine(theme, "layouts"));
            File.WriteAllText(Path.Combine(theme, "layouts", "docs.html"),
                """
                <!doctype html>
                <html>
                <head><title>Links</title>{{ canonical_html }}{{ head_html }}</head>
                <body><main>{{ content }}</main></body>
                </html>
                """);
            File.WriteAllText(Path.Combine(theme, "theme.json"),
                """
                { "name": "link-purpose", "engine": "scriban", "defaultLayout": "docs" }
                """);
            var spec = new SiteSpec
            {
                Name = "Localized link destinations",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "link-purpose",
                ThemesRoot = "themes",
                TrailingSlash = TrailingSlashMode.Always,
                Localization = new LocalizationSpec
                {
                    Enabled = true,
                    DefaultLanguage = "en",
                    Languages = new[]
                    {
                        new LanguageSpec { Code = "en", Default = true, BaseUrl = "https://example.test" },
                        new LanguageSpec { Code = "pl", BaseUrl = "https://pl.example.test" }
                    }
                },
                Collections = new[]
                {
                    new CollectionSpec { Name = "docs", Input = "content/docs", Output = "/docs", DefaultLayout = "docs" }
                }
            };
            var config = Path.Combine(root, "site.json");
            File.WriteAllText(config, "{}");
            var plan = WebSitePlanner.Plan(spec, config);
            var built = WebSiteBuilder.Build(spec, plan, Path.Combine(root, "_site"), language: "pl", languageAsRoot: true);

            var html = File.ReadAllText(Path.Combine(built.OutputPath, "docs", "index.html"));
            var doc = HtmlTinkerX.HtmlParser.ParseWithAngleSharp(html);
            Assert.Equal("https://pl.example.test/docs/", doc.QuerySelector("link[rel='canonical']")?.GetAttribute("href"));
            Assert.Contains(doc.QuerySelectorAll("a[href]"), link => link.GetAttribute("href") == absoluteHref);
            using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(built.OutputPath, "_powerforge", "site-spec.json")));
            Assert.Equal("https://example.test", metadata.RootElement.GetProperty("baseUrl").GetString());
            Assert.Contains(metadata.RootElement.GetProperty("localization").GetProperty("languages").EnumerateArray(),
                language => language.GetProperty("code").GetString() == "pl" &&
                            language.GetProperty("baseUrl").GetString() == "https://pl.example.test");

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = built.OutputPath,
                Include = new[] { "docs/index.html" },
                CheckLinks = false,
                CheckAssets = false,
                NavRequired = false
            });
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
            if (expectedDifferentDestination)
                Assert.Single(result.Issues, issue => issue.Category.Equals("link-purpose", StringComparison.OrdinalIgnoreCase));
            else
                Assert.DoesNotContain(result.Issues, issue => issue.Category.Equals("link-purpose", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("https://example.test/current", "https://example.test/current/")]
    [InlineData("https://example.test/current/", "https://example.test/current")]
    [InlineData("https://other.test/current", "https://other.test/current/")]
    [InlineData("https://other.test/current/", "https://other.test/current")]
    public void Audit_LinkPurposeDestinations_NormalizeFinalAbsoluteRedirectTarget(string redirectTo, string directHref)
    {
        var result = AuditLinkPurposeFixture(
            "index.html", "/legacy", directHref,
            declaredSiteBaseUrl: "https://example.test",
            redirectsJson: JsonSerializer.Serialize(new
            {
                redirects = new[] { new { from = "/legacy", to = redirectTo, status = 301 } }
            }));

        Assert.DoesNotContain(result.Issues, issue => issue.Category.Equals("link-purpose", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Audit_LinkPurposeDestinations_KeepExternalRedirectTargetDistinctFromLocalPath()
    {
        var result = AuditLinkPurposeFixture(
            "index.html", "/legacy", "/current/",
            declaredSiteBaseUrl: "https://example.test",
            redirectsJson:
            """
            {
              "redirects": [
                { "from": "/legacy", "to": "https://other.test/current", "status": 301 }
              ]
            }
            """);

        Assert.Single(result.Issues, issue => issue.Category.Equals("link-purpose", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Audit_LinkPurposeDestinations_ResolveRootedRedirectAgainstOriginalLocalDestination()
    {
        var result = AuditLinkPurposeFixture(
            "index.html", "https://example.test/legacy", "https://example.test/current",
            declaredSiteBaseUrl: "https://example.test",
            baseHref: "https://other.test/assets/",
            redirectsJson:
            """
            {
              "redirects": [
                { "from": "/legacy", "to": "/current", "status": 301 }
              ]
            }
            """);

        Assert.DoesNotContain(result.Issues, issue => issue.Category.Equals("link-purpose", StringComparison.OrdinalIgnoreCase));
    }
}
