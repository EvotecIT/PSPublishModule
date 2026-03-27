using System.Xml.Linq;
using PowerForge.Web;

namespace PowerForge.Tests;

public class WebSiteTaxonomyFeedMetadataTests
{
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
                ---

                Hello
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
                <html><head>{{ head_html }}</head><body>{{ content }}</body></html>
                """);
            File.WriteAllText(Path.Combine(themeRoot, "layouts", "term.html"),
                """
                <!doctype html>
                <html><head>{{ head_html }}</head><body>{{ content }}</body></html>
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
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
