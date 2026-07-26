using PowerForge.Web;

public sealed class WebApiDocsGeneratorFooterTokenContractTests
{
    [Fact]
    public void CustomFooterToken_DoesNotRequireNavigationWhenCallerProvidesValue()
    {
        var generated = Generate(
            "<footer>{{FOOTER_LEGAL}}</footer>",
            navJson: null,
            options => options.TemplateTokens["FOOTER_LEGAL"] = "<a href=\"/legal/\">Legal</a>");

        Assert.DoesNotContain(generated.Result.Warnings, warning =>
            warning.Contains("NAV.REQUIRED", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("<a href=\"/legal/\">Legal</a>", generated.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("{{FOOTER_LEGAL}}", generated.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyConfiguredFooterMenu_ReplacesNamedTokensWithEmptyContent()
    {
        const string navJson = """
            {
              "generated": true,
              "surfaces": {
                "apidocs": {
                  "primary": [ { "text": "API", "href": "/api/" } ],
                  "footer": { "footer-community": [] }
                }
              }
            }
            """;

        var generated = Generate(
            "<footer>{{FOOTER_COMMUNITY}}|{{FOOTER_COMMUNITY_LIST_ITEMS}}</footer>",
            navJson);

        Assert.Contains("<footer", generated.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("|", generated.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("{{FOOTER_", generated.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CollidingFooterMenuNames_EmitWarningAndKeepFirstMenuDeterministically()
    {
        const string navJson = """
            {
              "generated": true,
              "surfaces": {
                "apidocs": {
                  "primary": [ { "text": "API", "href": "/api/" } ],
                  "footer": {
                    "footer-products": [ { "text": "First", "href": "/first/" } ],
                    "footer_products": [ { "text": "Second", "href": "/second/" } ]
                  }
                }
              }
            }
            """;

        var generated = Generate("<footer>{{FOOTER_PRODUCTS}}</footer>", navJson);

        Assert.Contains(generated.Result.Warnings, warning =>
            warning.Contains("both map", StringComparison.OrdinalIgnoreCase) &&
            warning.Contains("FOOTER_PRODUCTS", StringComparison.Ordinal));
        Assert.Contains("href=\"/first/\"", generated.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/second/\"", generated.Html, StringComparison.Ordinal);
    }

    private static (WebApiDocsResult Result, string Html) Generate(
        string footerHtml,
        string? navJson,
        Action<WebApiDocsOptions>? configure = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-footer-token-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath,
                """
                <doc>
                  <assembly><name>Test</name></assembly>
                  <members>
                    <member name="T:MyNamespace.Sample"><summary>Sample.</summary></member>
                  </members>
                </doc>
                """);

            var footerPath = Path.Combine(root, "api-footer.html");
            File.WriteAllText(footerPath, footerHtml);

            string? navPath = null;
            if (navJson is not null)
            {
                navPath = Path.Combine(root, "site-nav.json");
                File.WriteAllText(navPath, navJson);
            }

            var outputPath = Path.Combine(root, "api");
            var options = new WebApiDocsOptions
            {
                XmlPath = xmlPath,
                OutputPath = outputPath,
                Format = "html",
                Template = "docs",
                BaseUrl = "/api",
                NavJsonPath = navPath,
                NavSurfaceName = "apidocs",
                FooterHtmlPath = footerPath
            };
            configure?.Invoke(options);

            WebApiDocsResult result = WebApiDocsGenerator.Generate(options);
            string html = File.ReadAllText(Path.Combine(outputPath, "index.html"));
            return (result, html);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
