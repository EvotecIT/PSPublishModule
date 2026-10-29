using System.Text.Json;
using System.Text.RegularExpressions;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebSiteStructuredContactRoutingTests
{
    [Theory]
    [InlineData(true, false, false, "https://product.example/pl/pomoc/#contact")]
    [InlineData(false, false, true, "https://product.example/support/#contact")]
    [InlineData(false, false, false, "https://product.example/support/#contact")]
    [InlineData(false, true, true, "https://product.example/pl/support/#contact")]
    public void Build_ContactUsesPublishedTranslationOrExistingFallback(
        bool translated, bool materialize, bool fallback, string expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-contact-route-" + Guid.NewGuid().ToString("N"));
        try
        {
            var content = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(Path.Combine(content, "pl"));
            File.WriteAllText(Path.Combine(content, "support.md"), """
                ---
                title: Support
                translation_key: support
                ---
                Contact support.
                """);
            File.WriteAllText(Path.Combine(content, "pl", "index.md"), """
                ---
                title: Produkt
                ---
                Produkt.
                """);
            if (translated)
                File.WriteAllText(Path.Combine(content, "pl", "pomoc.md"), """
                    ---
                    title: Pomoc
                    translation_key: support
                    ---
                    Kontakt.
                    """);
            var site = new SiteSpec
            {
                Name = "Example", BaseUrl = "https://product.example", ContentRoot = "content",
                TrailingSlash = TrailingSlashMode.Always,
                Collections = [new CollectionSpec { Name = "pages", Input = "content/pages", Output = "/" }],
                Localization = new LocalizationSpec { Enabled = true, DefaultLanguage = "en",
                    FallbackToDefaultLanguage = fallback, MaterializeFallbackPages = materialize, Languages = [
                        new LanguageSpec { Code = "en", Default = true }, new LanguageSpec { Code = "pl" }
                    ] },
                StructuredData = new StructuredDataSpec { Enabled = true, WebPage = true,
                    Organization = true, OrganizationContactUrl = "/support/#contact" }
            };
            var config = Path.Combine(root, "site.json");
            File.WriteAllText(config, "{}");
            var output = Path.Combine(root, "_site");
            WebSiteBuilder.Build(site, WebSitePlanner.Plan(site, config), output);
            AssertContactUrl(File.ReadAllText(Path.Combine(output, "pl", "index.html")), expected);

            // Themes using the metadata helper must receive the same complete page catalog.
            var source = new ContentItem { OutputPath = "/support/", Language = "en", TranslationKey = "support" };
            var target = new ContentItem { OutputPath = "/pl/pomoc/", Language = "pl", TranslationKey = "support" };
            if (materialize) target.OutputPath = "/pl/support/";
            var page = new ContentItem { OutputPath = "/pl/", Language = "pl" };
            var context = new ThemeRenderContext { Site = site, Page = page,
                AllItems = translated || materialize ? [source, target, page] : [source, page] };
            var html = new ScribanTemplateEngine().Render("{{ pf.structured_data {} }}", context, _ => null);
            AssertContactUrl(html, expected);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("https://help.example/support/", "https://help.example/support/#contact")]
    [InlineData("https://help.example/pomoc/", "https://help.example/pomoc/#contact")]
    [InlineData("https://help.example/pomoc/#form", "https://help.example/pomoc/#form")]
    [InlineData("https://help.example/pomoc/?topic=setup", "https://help.example/pomoc/?topic=setup")]
    [InlineData("https://help.example/pomoc/?topic=setup#form", "https://help.example/pomoc/?topic=setup#form")]
    [InlineData("/pl/pomoc/#form", "https://product.example/pl/pomoc/#form")]
    [InlineData("/pl/pomoc?topic=setup#form", "https://product.example/pl/pomoc/?topic=setup#form")]
    public void Render_ExplicitTranslationOwnsItsOriginAndSuffix(string translatedUrl, string expected)
    {
        var source = new ContentItem { OutputPath = "/support/", Language = "en", TranslationKey = "support",
            Meta = new Dictionary<string, object?> { ["i18n"] = new Dictionary<string, object?> {
                ["routes"] = new Dictionary<string, object?> { ["pl"] = translatedUrl }
            } } };
        var page = new ContentItem { OutputPath = "/pl/", Language = "pl" };
        var site = new SiteSpec { BaseUrl = "https://product.example", TrailingSlash = TrailingSlashMode.Always, StructuredData = new StructuredDataSpec {
            Enabled = true, WebPage = true, Organization = true, OrganizationContactUrl = "/support/#contact"
        }, Localization = new LocalizationSpec { Enabled = true, DefaultLanguage = "en", Languages = [
            new LanguageSpec { Code = "en", Default = true }, new LanguageSpec { Code = "pl" }
        ] } };
        AssertContactUrl(WebSiteBuilder.RenderStructuredData(site, page, [], null, [source, page]), expected);
    }

    private static void AssertContactUrl(string html, string expected)
    {
        var models = Regex.Matches(html, "<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Singleline)
            .Select(match => JsonDocument.Parse(match.Groups[1].Value)).ToArray();
        try
        {
            var organization = Assert.Single(models, m => m.RootElement.GetProperty("@type").GetString() == "Organization");
            Assert.Equal(expected, organization.RootElement.GetProperty("contactPoint")[0].GetProperty("url").GetString());
        }
        finally
        {
            foreach (var model in models) model.Dispose();
        }
    }
}
