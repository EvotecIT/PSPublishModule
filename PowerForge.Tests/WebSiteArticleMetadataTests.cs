using System.Text.Json;
using System.Text.RegularExpressions;
using PowerForge.Web;

namespace PowerForge.Tests;

public class WebSiteArticleMetadataTests
{
    [Theory]
    [InlineData("Article", "Organization")]
    [InlineData("BlogPosting", "Person")]
    [InlineData("NewsArticle", "Organization")]
    public void Build_UndatedGuideUsesExplicitArticleAndAuthorMetadata(string articleType, string authorType)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-article-metadata-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "content", "docs"));
            File.WriteAllText(Path.Combine(root, "content", "docs", "guide.md"), $$"""
                ---
                title: Read an event report
                description: Explain the events before handing over an investigation.
                meta.author.name: Report Editors
                meta.author.type: {{authorType}}
                meta.author.url: https://publisher.example/editors/
                meta.schema.type: {{articleType}}
                image: /images/event-report.png
                ---

                The report contains the events collected from the selected computer.
                """);
            var site = Site();
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(site, configPath);
            WebSiteBuilder.Build(site, plan, Path.Combine(root, "_site"));
            var html = File.ReadAllText(Path.Combine(root, "_site", "docs", "guide", "index.html"));
            var models = Models(html);
            var article = Assert.Single(models, model => model.GetProperty("@type").GetString() == articleType);
            Assert.Equal("Read an event report", article.GetProperty("headline").GetString());
            Assert.Equal(authorType, article.GetProperty("author").GetProperty("@type").GetString());
            Assert.Equal("Report Editors", article.GetProperty("author").GetProperty("name").GetString());
            Assert.Equal("https://publisher.example/editors/", article.GetProperty("author").GetProperty("url").GetString());
            Assert.Equal("https://product.example/images/event-report.png", article.GetProperty("image").GetString());
            Assert.Equal("https://product.example/docs/guide/", article.GetProperty("url").GetString());
            Assert.Equal("en", article.GetProperty("inLanguage").GetString());
            Assert.False(article.TryGetProperty("datePublished", out _));
            Assert.False(article.TryGetProperty("dateModified", out _));
            var webPage = Assert.Single(models, model => model.GetProperty("@type").GetString() == "WebPage");
            Assert.Equal(webPage.GetProperty("@id").GetString(), article.GetProperty("mainEntityOfPage").GetProperty("@id").GetString());
            Assert.Equal(article.GetProperty("@id").GetString(), webPage.GetProperty("mainEntity").GetProperty("@id").GetString());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("Article", false, false)]
    [InlineData("BlogPosting", false, false)]
    [InlineData("NewsArticle", false, false)]
    [InlineData("NewsArticle", true, false)]
    [InlineData("Article", false, true)]
    [InlineData("Product", true, true)]
    public void Render_ExplicitArticleRetainsSiteProfileGates(string declaredType, bool articleEnabled, bool newsEnabled)
    {
        var site = Site();
        site.StructuredData!.Article = articleEnabled;
        site.StructuredData.NewsArticle = newsEnabled;
        var page = new ContentItem { Title = "Guide", OutputPath = "/guide/", Collection = "docs" };
        page.Meta["schema"] = new Dictionary<string, object?> { ["type"] = declaredType };
        var models = Models(WebSiteBuilder.RenderStructuredData(site, page, [], null));
        Assert.DoesNotContain(models, model => new[] { "Article", "BlogPosting", "NewsArticle" }.Contains(model.GetProperty("@type").GetString()));
    }

    [Fact]
    public void Render_ArticleRetainsMultipleAuthorsAndOmitsGenericSiteImage()
    {
        var site = Site();
        site.Social = new SocialSpec { Image = "/brand-logo.png" };
        var page = new ContentItem { Title = "Guide", OutputPath = "/guide/", Collection = "blog" };
        page.Meta["author_names"] = new[] { "First Editor", "Second Editor" };
        page.Meta["author_urls"] = new[] { "https://publisher.example/first/", "https://publisher.example/second/" };
        var article = Assert.Single(Models(WebSiteBuilder.RenderStructuredData(site, page, [], null)),
            model => model.GetProperty("@type").GetString() == "Article");
        Assert.Equal(2, article.GetProperty("author").GetArrayLength());
        Assert.Equal("Second Editor", article.GetProperty("author")[1].GetProperty("name").GetString());
        Assert.Equal("https://publisher.example/second/", article.GetProperty("author")[1].GetProperty("url").GetString());
        Assert.False(article.TryGetProperty("image", out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_ArticleReusesTheGeneratedOpenGraphAsset(bool themeHelper)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-article-card-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "content", "docs"));
            File.WriteAllText(Path.Combine(root, "content", "docs", "guide.md"),
                "---\ntitle: Event report\nmeta.schema.type: Article\n---\nRead the event report.");
            var site = Site();
            site.Social = new SocialSpec { Enabled = true, AutoGenerateCards = true,
                GeneratedCardCollections = ["docs"], Image = "/brand-logo.png" };
            if (themeHelper)
            {
                var theme = Path.Combine(root, "themes", "test");
                Directory.CreateDirectory(Path.Combine(theme, "layouts"));
                File.WriteAllText(Path.Combine(theme, "theme.json"),
                    """{"name":"test","engine":"scriban","defaultLayout":"page"}""");
                File.WriteAllText(Path.Combine(theme, "layouts", "page.html"),
                    """{{ opengraph_html }}{{ pf.structured_data {} }}<main>{{ content }}</main>""");
                site.DefaultTheme = "test";
                site.ThemesRoot = "themes";
            }
            var config = Path.Combine(root, "site.json");
            File.WriteAllText(config, "{}");
            var output = Path.Combine(root, "_site");
            WebSiteBuilder.Build(site, WebSitePlanner.Plan(site, config), output);
            var html = File.ReadAllText(Path.Combine(output, "docs", "guide", "index.html"));
            var article = Assert.Single(Models(html), model => model.GetProperty("@type").GetString() == "Article");
            var image = article.GetProperty("image").GetString()!;
            Assert.Contains("/assets/social/generated/", image, StringComparison.Ordinal);
            Assert.Contains($"property=\"og:image\" content=\"{image}\"", html, StringComparison.Ordinal);
            var asset = Path.Combine(output, new Uri(image).AbsolutePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(asset));
            Assert.True(new FileInfo(asset).Length > 0);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Render_ArticleDoesNotAdvertiseAnUnrenderedOrFailedCard()
    {
        var site = Site();
        site.Social = new SocialSpec { Enabled = true, AutoGenerateCards = true, Image = "/brand-logo.png" };
        var page = new ContentItem { Title = "Unrendered report", OutputPath = "/report/", Collection = "blog" };
        var route = WebSiteBuilder.ResolveGeneratedSocialCardRoute(site, page);
        Assert.NotEmpty(route);
        try
        {
            foreach (var failed in new[] { false, true })
            {
                if (failed) WebSiteBuilder.RecordFailedGeneratedSocialCardRenderForTesting(route, string.Empty);
                var html = new ScribanTemplateEngine().Render("{{ pf.structured_data {} }}",
                    new ThemeRenderContext { Site = site, Page = page }, _ => null);
                var article = Assert.Single(Models(html), model => model.GetProperty("@type").GetString() == "Article");
                Assert.False(article.TryGetProperty("image", out _));
            }
        }
        finally
        {
            WebSiteBuilder.ClearGeneratedSocialCardRenderOutcomeForTesting(route, string.Empty);
        }
    }

    private static SiteSpec Site() => new()
    {
        Name = "Example Product", BaseUrl = "https://product.example", ContentRoot = "content",
        TrailingSlash = TrailingSlashMode.Always,
        Localization = new LocalizationSpec { DefaultLanguage = "en" },
        Collections = [new CollectionSpec { Name = "docs", Input = "content/docs", Output = "/docs", SitemapLastModified = SitemapLastModifiedPolicy.ExplicitOnly }],
        StructuredData = new StructuredDataSpec { WebPage = true, Article = true, NewsArticle = true,
            OrganizationName = "Example Publisher", OrganizationUrl = "https://publisher.example/" }
    };

    private static JsonElement[] Models(string html) => Regex.Matches(html, "<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Singleline)
        .Select(match => { using var json = JsonDocument.Parse(match.Groups[1].Value); return json.RootElement.Clone(); }).ToArray();
}
