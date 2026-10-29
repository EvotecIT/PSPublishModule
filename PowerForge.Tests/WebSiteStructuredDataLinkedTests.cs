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
    public void Render_ContactPageWithFaqRetainsBothTypesAndPublisherSubject()
    {
        var page = Page();
        page.Canonical = "https://product.example/contact/";
        page.Meta["schema"] = new Dictionary<string, object?> { ["page_type"] = "ContactPage" };
        page.Meta["faq"] = new Dictionary<string, object?> { ["items"] = new[] {
            new Dictionary<string, object?> { ["question"] = "How can I contact you?", ["answer"] = "Use our contact form." }
        } };
        var model = Models(RenderTemplate("{{ pf.structured_data {} }}", Site(), page))
            .Single(item => item.TryGetProperty("@id", out var id) && id.GetString() == "https://product.example/contact/#webpage");
        Assert.Equal(new[] { "ContactPage", "FAQPage" }, model.GetProperty("@type").EnumerateArray().Select(type => type.GetString()));
        Assert.Equal("https://publisher.example/#organization", model.GetProperty("about").GetProperty("@id").GetString());
        Assert.Equal("Use our contact form.", model.GetProperty("mainEntity")[0].GetProperty("acceptedAnswer").GetProperty("text").GetString());
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
    [InlineData("[\"A\",\"B\"]")]
    [InlineData("[]")]
    public void Render_HelperPreservesLiteralJsonFaqAnswer(string answer)
    {
        var template = "{{ metadata = { faq: { items: [{ question: \"Payload?\", answer: " +
            JsonSerializer.Serialize(answer) + " }] } } }}{{ pf.structured_data metadata }}";
        var model = OfType(Models(RenderTemplate(template, Site(), Page())), "FAQPage");
        Assert.Equal(answer, model.GetProperty("mainEntity")[0].GetProperty("acceptedAnswer").GetProperty("text").GetString());
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

    [Theory]
    [InlineData(false, "https://source.example/#library", "https://source.example/#library")]
    [InlineData(true, "https://source.example/#library", "https://source.example/#library")]
    [InlineData(true, null, "https://product.example/pricing/#source-code")]
    [InlineData(false, null, null)]
    public void Render_SourceCodeIdentityRespectsExplicitIdIndependentlyOfPageLinking(bool linkPages, string? id, string? expected)
    {
        var site = Site();
        site.StructuredData!.SoftwareSourceCode = true;
        site.StructuredData.WebPage = linkPages;
        var page = Page();
        page.Meta["source_code"] = new Dictionary<string, object?> { ["name"] = "Example.Documents", ["id"] = id };
        var source = OfType(Models(WebSiteBuilder.RenderStructuredData(site, page, [], null)), "SoftwareSourceCode");
        Assert.Equal(expected, source.TryGetProperty("@id", out var identity) ? identity.GetString() : null);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(true, true, false)]
    public void Render_LinksEachSoftwareEntityToCanonicalPageWithOrWithoutFaq(bool application, bool sourceCode, bool withFaq)
    {
        var site = Site();
        site.StructuredData!.SoftwareApplication = application;
        site.StructuredData.SoftwareSourceCode = sourceCode;
        var page = Page();
        page.Canonical = "https://product.example/guide/";
        page.Meta["software"] = new Dictionary<string, object?> { ["name"] = "Example App", ["id"] = "https://product.example/#software" };
        page.Meta["source_code"] = new Dictionary<string, object?> { ["name"] = "Example Library", ["id"] = "https://source.example/#library" };
        if (withFaq)
            page.Meta["faq"] = new Dictionary<string, object?> { ["items"] = new[] {
                new Dictionary<string, object?> { ["question"] = "How?", ["answer"] = "Use the library." }
            } };

        var models = Models(RenderTemplate("{{ pf.structured_data {} }}", site, page));
        var pageModel = OfType(models, withFaq ? "FAQPage" : "WebPage");
        foreach (var type in new[] { "SoftwareApplication", "SoftwareSourceCode" })
        {
            var entity = models.SingleOrDefault(model => model.GetProperty("@type").GetString() == type);
            if (entity.ValueKind != JsonValueKind.Undefined)
                Assert.Equal(pageModel.GetProperty("@id").GetString(), entity.GetProperty("mainEntityOfPage").GetProperty("@id").GetString());
        }
        if (withFaq)
            Assert.Equal("Use the library.", pageModel.GetProperty("mainEntity")[0].GetProperty("acceptedAnswer").GetProperty("text").GetString());
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

    [Theory]
    [InlineData("/support/", "https://product.example/pl/support/")]
    [InlineData("support/", "https://product.example/pl/support/")]
    [InlineData("/pl/support/", "https://product.example/pl/support/")]
    [InlineData("/support/#contact", "https://product.example/pl/support/#contact")]
    [InlineData("/support/?topic=setup", "https://product.example/pl/support/?topic=setup")]
    [InlineData("https://help.example/contact/", "https://help.example/contact/")]
    [InlineData("https://product.example/support/", "https://product.example/support/")]
    public void Render_ContactPageLocalizesSiteRelativeSupportWithoutRewritingExplicitUrls(string contactUrl, string expected)
    {
        var site = Site();
        site.TrailingSlash = TrailingSlashMode.Always;
        site.Localization = new LocalizationSpec { Enabled = true, DefaultLanguage = "en", Languages = [
            new LanguageSpec { Code = "en", Default = true }, new LanguageSpec { Code = "pl" }
        ] };
        site.StructuredData!.OrganizationContactUrl = contactUrl;
        var page = Page();
        page.Language = "pl";
        page.OutputPath = "/pl/support/";
        page.TranslationKey = "support";
        page.Meta["schema"] = new Dictionary<string, object?> { ["page_type"] = "ContactPage" };

        var models = Models(WebSiteBuilder.RenderStructuredData(site, page, [], null, [
            new ContentItem { OutputPath = "/support/", Language = "en", TranslationKey = "support" }, page
        ]));

        Assert.Equal(expected, OfType(models, "Organization").GetProperty("contactPoint")[0].GetProperty("url").GetString());
        Assert.Equal("pl", OfType(models, "ContactPage").GetProperty("inLanguage").GetString());
        Assert.Equal("https://publisher.example/#organization", OfType(models, "ContactPage").GetProperty("mainEntity").GetProperty("@id").GetString());
    }

    [Theory]
    [InlineData("pl")]
    [InlineData("pt-BR")]
    [InlineData("zh-Hant")]
    public void Render_ApplicationDeclaresTheLanguageOfItsPageMetadata(string language)
    {
        var page = Page();
        page.Language = language;
        page.Meta["software"] = new Dictionary<string, object?> { ["name"] = "Example App" };

        var app = OfType(Models(WebSiteBuilder.RenderStructuredData(Site(), page, [], null)), "SoftwareApplication");

        Assert.Equal(language, app.GetProperty("inLanguage").GetString());
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
