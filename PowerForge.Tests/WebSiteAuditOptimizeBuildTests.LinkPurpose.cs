using PowerForge.Web;

namespace PowerForge.Tests;

public partial class WebSiteAuditOptimizeBuildTests
{
    [Theory]
    [InlineData("docs/cli/index.html", "../markup/", "/docs/markup/")]
    [InlineData("docs/index.html", "./install/", "/docs/install/")]
    [InlineData("projects/chartforgex/docs/cli/index.html", "../markup/", "/projects/chartforgex/docs/markup/")]
    [InlineData("projects/chartforgex/docs/index.html", "./install/", "/projects/chartforgex/docs/install/")]
    public void Audit_LinkPurposeDestinations_ResolvePageRelativeReferences(
        string pagePath, string relativeHref, string rootedHref)
    {
        var result = AuditLinkPurposeFixture(pagePath, relativeHref, rootedHref);

        Assert.DoesNotContain(result.Issues, issue => issue.Category.Equals("link-purpose", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Audit_LinkPurposeDestinations_PreserveDeclaredSiteBasePath()
    {
        var result = AuditLinkPurposeFixture(
            "docs/cli/index.html", "../markup/", "/projects/chartforgex/docs/markup/",
            declaredSiteBaseUrl: "https://example.test/projects/chartforgex");

        Assert.DoesNotContain(result.Issues, issue => issue.Category.Equals("link-purpose", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("../reference/", "/docs/reference/install/")]
    [InlineData("../reference", "/docs/install/")]
    public void Audit_LinkPurposeDestinations_HonorHtmlBaseTrailingSlash(string baseHref, string rootedHref)
    {
        var result = AuditLinkPurposeFixture(
            "docs/start/index.html", "install/", rootedHref,
            canonicalUrl: "https://example.test/docs/start/", baseHref: baseHref);

        Assert.DoesNotContain(result.Issues, issue => issue.Category.Equals("link-purpose", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("https://example.test/playground/")]
    [InlineData("//example.test/playground/")]
    public void Audit_LinkPurposeDestinations_TreatKnownSameOriginUrlsAsEquivalent(string absoluteHref)
    {
        var result = AuditLinkPurposeFixture(
            "docs/index.html", absoluteHref, "/playground/",
            canonicalUrl: "https://example.test/docs/");

        Assert.DoesNotContain(result.Issues, issue => issue.Category.Equals("link-purpose", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("https://example.test/playground/", false)]
    [InlineData("https://canonical.test/playground/", true)]
    public void Audit_LinkPurposeDestinations_PreferDeclaredSiteOriginToForeignCanonical(
        string absoluteHref, bool expectedDifferentDestination)
    {
        var result = AuditLinkPurposeFixture(
            "docs/index.html", absoluteHref, "/playground/",
            canonicalUrl: "https://canonical.test/docs/",
            declaredSiteBaseUrl: "https://example.test");

        if (expectedDifferentDestination)
            Assert.Single(result.Issues, issue => issue.Category.Equals("link-purpose", StringComparison.OrdinalIgnoreCase));
        else
            Assert.DoesNotContain(result.Issues, issue => issue.Category.Equals("link-purpose", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("../markup/", "/docs/editor/")]
    [InlineData("https://other.test/playground/", "/playground/")]
    public void Audit_LinkPurposeDestinations_KeepDifferentPathsAndOriginsDistinct(string firstHref, string secondHref)
    {
        var result = AuditLinkPurposeFixture(
            "docs/cli/index.html", firstHref, secondHref,
            canonicalUrl: "https://example.test/docs/cli/");

        Assert.Single(result.Issues, issue => issue.Category.Equals("link-purpose", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://example.test/docs/")]
    public void Audit_LinkPurposeDestinations_DoNotApplyLocalRedirectsToUnprovenOrigins(string? canonicalUrl)
    {
        var result = AuditLinkPurposeFixture(
            "docs/index.html", "https://other.test/legacy/", "/blog/current/",
            canonicalUrl: canonicalUrl,
            redirectsJson:
            """
            {
              "redirects": [
                { "from": "/legacy", "to": "/blog/current", "status": 301 }
              ]
            }
            """);

        Assert.Single(result.Issues, issue => issue.Category.Equals("link-purpose", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("blog/index.html", "../legacy/", "/blog/current/")]
    [InlineData("pl/blog/index.html", "../../legacy/", "/pl/blog/current/")]
    public void Audit_LinkPurposeDestinations_ResolveRelativeRedirectsForCurrentLanguage(
        string pagePath, string relativeHref, string rootedHref)
    {
        var result = AuditLinkPurposeFixture(
            pagePath, relativeHref, rootedHref,
            redirectsJson:
            """
            {
              "redirects": [
                { "from": "/legacy", "to": "/blog/current", "status": 301 },
                { "from": "/legacy", "to": "/pl/blog/current", "status": 301 }
              ]
            }
            """);

        Assert.DoesNotContain(result.Issues, issue => issue.Category.Equals("link-purpose", StringComparison.OrdinalIgnoreCase));
    }

    private static WebAuditResult AuditLinkPurposeFixture(
        string pagePath,
        string firstHref,
        string secondHref,
        string? canonicalUrl = null,
        string? baseHref = null,
        string? redirectsJson = null,
        string? declaredSiteBaseUrl = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-link-destinations-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, pagePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file) ?? root);
            var canonical = canonicalUrl is null
                ? string.Empty
                : $"<link rel=\"canonical\" href=\"{System.Net.WebUtility.HtmlEncode(canonicalUrl)}\" />";
            var htmlBase = baseHref is null
                ? string.Empty
                : $"<base href=\"{System.Net.WebUtility.HtmlEncode(baseHref)}\" />";
            File.WriteAllText(file,
                $$"""
                <!doctype html>
                <html>
                <head><title>Links</title>{{canonical}}{{htmlBase}}</head>
                <body>
                  <main>
                    <h1>Links</h1>
                    <a href="{{System.Net.WebUtility.HtmlEncode(firstHref)}}">Guide</a>
                    <a href="{{System.Net.WebUtility.HtmlEncode(secondHref)}}">Guide</a>
                  </main>
                </body>
                </html>
                """);
            if (redirectsJson is not null || declaredSiteBaseUrl is not null)
                Directory.CreateDirectory(Path.Combine(root, "_powerforge"));
            if (redirectsJson is not null)
            {
                File.WriteAllText(Path.Combine(root, "_powerforge", "redirects.json"), redirectsJson);
            }
            if (declaredSiteBaseUrl is not null)
            {
                File.WriteAllText(Path.Combine(root, "_powerforge", "site-spec.json"),
                    System.Text.Json.JsonSerializer.Serialize(new { baseUrl = declaredSiteBaseUrl }));
            }

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                CheckLinks = false,
                CheckAssets = false,
                NavRequired = false
            });
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
            return result;
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
