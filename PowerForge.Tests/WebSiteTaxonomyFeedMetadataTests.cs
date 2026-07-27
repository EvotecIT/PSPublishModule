using System.Xml.Linq;
using PowerForge.Web;

namespace PowerForge.Tests;

public class WebSiteTaxonomyFeedMetadataTests
{
    [Fact]
    public void Build_TaxonomyIndexDescription_StaysWithinSearchSnippetRangeForMultiDigitCounts()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-taxonomy-seo-counts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var contentPath = Path.Combine(root, "content", "blog");
            Directory.CreateDirectory(contentPath);
            for (var index = 1; index <= 12; index++)
            {
                var tags = string.Join(", ", new[] { "shared" }.Concat(
                    Enumerable.Range(1, 27)
                        .Where(tag => tag == index || index == 1)
                        .Select(tag => $"tag-{tag}")));
                File.WriteAllText(Path.Combine(contentPath, $"post-{index}.md"),
                    $"""
                    ---
                    title: Post {index}
                    description: Published content item {index}.
                    tags: [{tags}]
                    ---

                    Content
                    """);
            }

            var themeRoot = Path.Combine(root, "themes", "taxonomy-seo-counts");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "page.html"),
                "<!doctype html><html><head>{{ description_meta_html }}{{ head_html }}</head><body>{{ content }}</body></html>");
            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                """{"name":"taxonomy-seo-counts","engine":"scriban","defaultLayout":"page"}""");

            var spec = new SiteSpec
            {
                Name = "OfficeIMO",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "taxonomy-seo-counts",
                ThemesRoot = "themes",
                Collections =
                [
                    new CollectionSpec
                    {
                        Name = "blog",
                        Input = "content/blog",
                        Output = "/blog"
                    }
                ],
                Taxonomies =
                [
                    new TaxonomySpec
                    {
                        Name = "tags",
                        BasePath = "/tags",
                        PageSize = 5,
                        ListLayout = "page",
                        TermLayout = "page"
                    }
                ]
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var result = WebSiteBuilder.Build(spec, WebSitePlanner.Plan(spec, configPath), Path.Combine(root, "_site"));
            var description = ReadMetaDescription(File.ReadAllText(Path.Combine(result.OutputPath, "tags", "index.html")));

            Assert.InRange(description.Length, 120, 160);
            Assert.Contains("Browse 12 published OfficeIMO pages through 28 Tags terms", description, StringComparison.Ordinal);

            var paginatedDescription = ReadMetaDescription(
                File.ReadAllText(Path.Combine(result.OutputPath, "tags", "shared", "page", "2", "index.html")));
            Assert.DoesNotContain("in one place", paginatedDescription, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("available result set", paginatedDescription, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void FitTaxonomyDescription_PreservesUnicodeScalars()
    {
        var method = typeof(WebSiteBuilder).GetMethod(
            "FitTaxonomyDescription",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var input = new string('界', 158) + "😀tail";
        var description = Assert.IsType<string>(method!.Invoke(null, [input, false]));

        Assert.InRange(description.Length, 120, 160);
        Assert.DoesNotContain('\uFFFD', description);
        for (var index = 0; index < description.Length; index++)
        {
            if (char.IsHighSurrogate(description[index]))
            {
                Assert.True(index + 1 < description.Length && char.IsLowSurrogate(description[index + 1]));
                index++;
            }
            else
            {
                Assert.False(char.IsLowSurrogate(description[index]));
            }
        }
    }

    [Fact]
    public void Build_TaxonomyFeeds_UseConfiguredTaxonomyMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-taxonomy-feed-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var blogPath = Path.Combine(root, "content", "blog");
            Directory.CreateDirectory(blogPath);
            File.WriteAllText(Path.Combine(blogPath, "_index.md"),
                """
                ---
                title: Blog
                description: Product blog.
                ---

                Blog home
                """);
            File.WriteAllText(Path.Combine(blogPath, "first-post.md"),
                """
                ---
                title: First Post
                description: First release notes entry.
                date: 2026-01-01
                tags: [release]
                authors: [Alice]
                ---

                Hello
                """);
            File.WriteAllText(Path.Combine(blogPath, "pierwszy-wpis.md"),
                """
                ---
                title: Pierwszy wpis
                description: Krótki opis.
                date: 2026-01-02
                language: pl
                tags: [wydanie]
                ---

                Opisuje polskie wydanie produktu, najważniejsze poprawki, zgodność pakietów oraz praktyczne informacje potrzebne podczas aktualizacji środowiska.
                """);

            var themeRoot = Path.Combine(root, "themes", "taxonomy-feed-meta");
            Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "blog.html"),
                """
                <!doctype html>
                <html><head>{{ head_html }}</head><body>{{ content }}</body></html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "taxonomy.html"),
                """
                <!doctype html>
                <html><head>{{ description_meta_html }}{{ head_html }}</head><body>{{ content }}</body></html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "term.html"),
                """
                <!doctype html>
                <html><head>{{ description_meta_html }}{{ head_html }}</head><body>{{ content }}</body></html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
                """
                {
                  "name": "taxonomy-feed-meta",
                  "engine": "scriban",
                  "defaultLayout": "blog"
                }
                """);

            var spec = new SiteSpec
            {
                Name = "Feed Metadata Test",
                BaseUrl = "https://example.test",
                ContentRoot = "content",
                DefaultTheme = "taxonomy-feed-meta",
                ThemesRoot = "themes",
                Localization = new LocalizationSpec
                {
                    Enabled = true,
                    DefaultLanguage = "en",
                    Languages =
                    [
                        new LanguageSpec { Code = "en", Default = true },
                        new LanguageSpec { Code = "pl" }
                    ]
                },
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "blog",
                        Input = "content/blog",
                        Output = "/blog",
                        ListLayout = "blog"
                    }
                },
                Taxonomies = new[]
                {
                    new TaxonomySpec
                    {
                        Name = "tags",
                        BasePath = "/tags",
                        ListLayout = "taxonomy",
                        TermLayout = "term",
                        FeedTitle = "Blog Topics and Tags",
                        FeedDescription = "Browse release notes and tutorial topics.",
                        TermFeedTitleTemplate = "{site} tag: {term}",
                        TermFeedDescriptionTemplate = "Posts filed under {term} in {site}."
                    },
                    new TaxonomySpec
                    {
                        Name = "authors",
                        BasePath = "/authors",
                        ListLayout = "taxonomy",
                        TermLayout = "term",
                        FeedTitle = "Blog Authors",
                        FeedDescription = "Browse articles by author.",
                        TermFeedTitleTemplate = "{site} author: {term}",
                        TermFeedDescriptionTemplate = "Posts written by {term} in {site}."
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var outPath = Path.Combine(root, "_site");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var result = WebSiteBuilder.Build(spec, plan, outPath);

            var taxonomyFeed = XDocument.Load(Path.Combine(result.OutputPath, "tags", "index.xml"));
            Assert.Equal("Blog Topics and Tags", taxonomyFeed.Root?.Element("channel")?.Element("title")?.Value);
            Assert.Equal("Browse release notes and tutorial topics.", taxonomyFeed.Root?.Element("channel")?.Element("description")?.Value);

            var termFeed = XDocument.Load(Path.Combine(result.OutputPath, "tags", "release", "index.xml"));
            Assert.Equal("Feed Metadata Test tag: release", termFeed.Root?.Element("channel")?.Element("title")?.Value);
            Assert.Equal("Posts filed under release in Feed Metadata Test.", termFeed.Root?.Element("channel")?.Element("description")?.Value);

            var taxonomyHtml = File.ReadAllText(Path.Combine(result.OutputPath, "tags", "index.html"));
            var taxonomyDescription = ReadMetaDescription(taxonomyHtml);
            Assert.InRange(taxonomyDescription.Length, 120, 160);
            Assert.Contains("Browse 1 published Feed Metadata Test page through 1 Tags term", taxonomyDescription, StringComparison.Ordinal);
            Assert.DoesNotContain("guides", taxonomyDescription, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("articles", taxonomyDescription, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("examples", taxonomyDescription, StringComparison.OrdinalIgnoreCase);

            var termHtml = File.ReadAllText(Path.Combine(result.OutputPath, "tags", "release", "index.html"));
            var termDescription = ReadMetaDescription(termHtml);
            Assert.InRange(termDescription.Length, 120, 160);
            Assert.Contains("Explore 1 published Feed Metadata Test page tagged release", termDescription, StringComparison.Ordinal);
            Assert.DoesNotContain("guides", termDescription, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("articles", termDescription, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("examples", termDescription, StringComparison.OrdinalIgnoreCase);

            var authorHtml = File.ReadAllText(Path.Combine(result.OutputPath, "authors", "alice", "index.html"));
            var authorDescription = ReadMetaDescription(authorHtml);
            Assert.InRange(authorDescription.Length, 120, 160);
            Assert.Contains("filed under Alice in the Authors taxonomy", authorDescription, StringComparison.Ordinal);
            Assert.DoesNotContain("tagged Alice", authorDescription, StringComparison.Ordinal);

            var polishTaxonomyHtml = File.ReadAllText(Path.Combine(result.OutputPath, "pl", "tags", "index.html"));
            var polishTaxonomyDescription = ReadMetaDescription(polishTaxonomyHtml);
            Assert.InRange(polishTaxonomyDescription.Length, 120, 160);
            Assert.Contains("Opisuje polskie wydanie produktu", polishTaxonomyDescription, StringComparison.Ordinal);
            Assert.Contains("Krótki opis", polishTaxonomyDescription, StringComparison.Ordinal);
            Assert.DoesNotContain("Browse", polishTaxonomyDescription, StringComparison.Ordinal);
            Assert.DoesNotContain("Explore", polishTaxonomyDescription, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static string ReadMetaDescription(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            html,
            "<meta\\s+name=\"description\"\\s+content=\"(?<content>[^\"]*)\"",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        Assert.True(match.Success, "Expected a generated meta description.");
        return System.Net.WebUtility.HtmlDecode(match.Groups["content"].Value);
    }
}
