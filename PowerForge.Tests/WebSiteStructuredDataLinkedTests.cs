using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using PowerForge.Web;

namespace PowerForge.Tests;

public class WebSiteStructuredDataLinkedTests
{
    [Fact]
    public void Render_LinksCanonicalPageWebsiteAndIndependentPublisher()
    {
        var site = Site();
        var page = Page();
        page.Canonical = "https://product.example/guide/";
        var models = Models(WebSiteBuilder.RenderStructuredData(site, page, [], null));
        var webPage = OfType(models, "WebPage");
        Assert.Equal("https://product.example/guide/#webpage", webPage.GetProperty("@id").GetString());
        Assert.Equal("https://product.example/guide/", webPage.GetProperty("url").GetString());
        Assert.Equal("https://product.example/#website", webPage.GetProperty("isPartOf").GetProperty("@id").GetString());
        var organization = OfType(models, "Organization");
        Assert.Equal("Example Publisher", organization.GetProperty("name").GetString());
        Assert.Equal("https://publisher.example/#organization", organization.GetProperty("@id").GetString());
        Assert.Equal(organization.GetProperty("@id").GetString(),
            OfType(models, "WebSite").GetProperty("publisher").GetProperty("@id").GetString());
    }

    [Fact]
    public void Render_ContactPageUsesPublisherAsMainEntity()
    {
        var page = Page();
        page.Meta["schema"] = new Dictionary<string, object?> { ["page_type"] = "ContactPage" };
        var model = OfType(Models(WebSiteBuilder.RenderStructuredData(Site(), page, [], null)), "ContactPage");
        Assert.Equal("https://publisher.example/#organization", model.GetProperty("mainEntity").GetProperty("@id").GetString());
    }

    [Fact]
    public void Render_HelperUsesDataDrivenSubscriptionFactsWithoutChangingPageMetadata()
    {
        var page = Page();
        var template = """
            {{ metadata = { software: {
                name: "Example App", id: "https://product.example/#software", url: "/",
                price: 20.5, price_currency: "EUR", offer_url: "https://product.example/pricing/",
                eligible_quantity: { min_value: 20, unit_text: "seats" },
                reference_quantity: { value: 1, unit_code: "MON", unit_text: "month" }
            } } }}{{ pf.structured_data metadata }}
            """;
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
            var html = RenderTemplate(template, Site(), page);
            var models = Models(html);
            var app = OfType(models, "SoftwareApplication");
            var offer = app.GetProperty("offers");
            Assert.Equal("20.5", offer.GetProperty("price").GetString());
            Assert.Equal("https://product.example/pricing/", offer.GetProperty("url").GetString());
            Assert.Equal(20, offer.GetProperty("eligibleQuantity").GetProperty("minValue").GetDecimal());
            var specification = offer.GetProperty("priceSpecification");
            Assert.Equal("20.5", specification.GetProperty("price").GetString());
            Assert.Equal("MON", specification.GetProperty("referenceQuantity").GetProperty("unitCode").GetString());
            Assert.Equal("https://product.example/", app.GetProperty("url").GetString());
            Assert.Equal(app.GetProperty("@id").GetString(), OfType(models, "WebPage").GetProperty("mainEntity").GetProperty("@id").GetString());
            Assert.False(page.Meta.ContainsKey("software"));
            Assert.DoesNotContain(Models(RenderTemplate("{{ pf.structured_data null }}", Site(), page)),
                model => model.GetProperty("@type").GetString() == "SoftwareApplication");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Render_HelperFlattensGroupedFaqAndSafelyPreservesParagraphs()
    {
        const string text = "Quotes \" & Unicode Łódź </script><script>alert(1)</script>";
        var data = new Dictionary<string, object?>
        {
            ["sections"] = new[] { new Dictionary<string, object?> { ["items"] = new[] {
                new Dictionary<string, object?> { ["question"] = "How?", ["answer"] = new[] { text, "Second paragraph." } }
            } } }
        };
        var context = new ThemeRenderContext { Site = Site(), Page = Page(), Data = new Dictionary<string, object?> { ["faq"] = data } };
        var html = new ScribanTemplateEngine().Render("{{ metadata = { faq: { sections: data.faq.sections } } }}{{ pf.structured_data metadata }}", context, _ => null);
        var models = Models(html);
        var faq = OfType(models, "FAQPage");
        Assert.Equal("https://product.example/pricing/#webpage", faq.GetProperty("@id").GetString());
        Assert.Equal(text + "\n\nSecond paragraph.", faq.GetProperty("mainEntity")[0].GetProperty("acceptedAnswer").GetProperty("text").GetString());
        Assert.DoesNotContain("</script><script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain(models, model => model.GetProperty("@type").GetString() == "WebPage");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Render_HelperRespectsPageAndSiteOptOut(bool siteEnabled, bool pageEnabled)
    {
        var site = Site();
        site.StructuredData!.Enabled = siteEnabled;
        var page = Page();
        page.Meta["structured_data"] = pageEnabled;
        Assert.Empty(RenderTemplate("{{ metadata = { structured_data: true, software: { name: \"Hidden\" } } }}{{ pf.structured_data metadata }}", site, page));
    }

    [Fact]
    public void Render_SourceCodeProfileDescribesLibraryWithoutInventingAnOfferOrRating()
    {
        var site = Site();
        site.StructuredData!.SoftwareSourceCode = true;
        var metadata = new Dictionary<string, object?>
        {
            ["source_code"] = new Dictionary<string, object?>
            {
                ["name"] = "Example.Documents", ["code_repository"] = "https://github.com/example/documents",
                ["programming_language"] = "C#", ["runtime_platform"] = ".NET",
                ["license"] = "https://opensource.org/license/mit/"
            }
        };
        var models = Models(WebSiteBuilder.RenderStructuredData(site, Page(), [], metadata));
        var source = OfType(models, "SoftwareSourceCode");
        Assert.Equal("Example.Documents", source.GetProperty("name").GetString());
        Assert.Equal("https://github.com/example/documents", source.GetProperty("codeRepository").GetString());
        Assert.False(source.TryGetProperty("offers", out _));
        Assert.False(source.TryGetProperty("aggregateRating", out _));
        Assert.DoesNotContain(models, model => model.GetProperty("@type").GetString() == "SoftwareApplication");
    }

    [Fact]
    public void Render_HelperPreservesArticleModificationDateAndPublisher()
    {
        var page = Page();
        page.Collection = "blog";
        page.Date = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        page.LastModifiedUtc = DateTimeOffset.Parse("2026-10-01T12:00:00Z", CultureInfo.InvariantCulture);
        var model = OfType(Models(RenderTemplate("{{ pf.structured_data {} }}", Site(), page)), "Article");
        Assert.Equal(page.LastModifiedUtc.Value.ToString("O"), model.GetProperty("dateModified").GetString());
        Assert.Equal("Example Publisher", model.GetProperty("publisher").GetProperty("name").GetString());
    }

    private static string RenderTemplate(string template, SiteSpec site, ContentItem page) =>
        new ScribanTemplateEngine().Render(template, new ThemeRenderContext { Site = site, Page = page }, _ => null);

    private static SiteSpec Site() => new()
    {
        Name = "Example App", BaseUrl = "https://product.example",
        StructuredData = new StructuredDataSpec { WebPage = true, FaqPage = true, SoftwareApplication = true,
            OrganizationName = "Example Publisher", OrganizationUrl = "https://publisher.example/" }
    };

    private static ContentItem Page() => new() { Title = "Pricing", OutputPath = "/pricing/", Language = "en" };

    private static JsonElement[] Models(string html) => Regex.Matches(html, "<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Singleline)
        .Select(match => { using var json = JsonDocument.Parse(match.Groups[1].Value); return json.RootElement.Clone(); }).ToArray();

    private static JsonElement OfType(JsonElement[] models, string type) => Assert.Single(models, model => model.GetProperty("@type").GetString() == type);
}
