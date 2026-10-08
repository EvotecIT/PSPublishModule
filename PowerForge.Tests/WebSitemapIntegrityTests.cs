using System.Globalization;
using System.Xml.Linq;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebSitemapIntegrityTests
{
    private static readonly XNamespace Ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

    [Theory]
    [InlineData("./", "")]
    [InlineData("../topic/", "")]
    [InlineData("//example.test/docs/topic/", "")]
    [InlineData("topic/", "<base href='/docs/'>")]
    public void RenderedSelfCanonicalUsesDocumentUrlAndBase(string canonical, string documentBase)
    {
        WithSite(root =>
        {
            Directory.CreateDirectory(Path.Combine(root, "docs", "topic"));
            File.WriteAllText(Path.Combine(root, "docs", "topic", "index.html"),
                $"{documentBase}<link rel='canonical' href='{canonical}'>");
            var result = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = root, BaseUrl = "https://example.test", IncludeTextFiles = false
            });
            Assert.Equal(new[] { "https://example.test/docs/topic/" },
                XDocument.Load(result.OutputPath).Descendants(Ns + "loc").Select(node => node.Value));
        });
    }

    [Fact]
    public void CanonicalAndExclusionPolicyAppliesAfterAllEntrySources()
    {
        WithSite(root =>
        {
            Directory.CreateDirectory(Path.Combine(root, "variant"));
            File.WriteAllText(Path.Combine(root, "variant", "index.html"), "<link rel=canonical href=https://example.test/original/>");
            Directory.CreateDirectory(Path.Combine(root, "original"));
            File.WriteAllText(Path.Combine(root, "original", "index.html"), "<link rel='canonical' href='https://example.test/original/'>");
            var json = Path.Combine(root, "entries.json");
            File.WriteAllText(json, """[{"path":"/404.html"},{"path":"/nested/404.html"},{"path":"/private/","noIndex":true}]""");
            var result = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = root, BaseUrl = "https://example.test", IncludeHtmlFiles = false, IncludeTextFiles = false,
                EntriesJsonPath = json, ExtraPaths = new[] { "/variant/", "/original/", "/404.html" },
                Entries = new[] { new WebSitemapEntry { Path = "/external/", Canonical = "https://other.test/external/" } }
            });
            Assert.Equal(new[] { "https://example.test/original/" }, XDocument.Load(result.OutputPath).Descendants(Ns + "loc").Select(node => node.Value));
        });
    }

    [Theory]
    [InlineData("regular")]
    [InlineData("image")]
    [InlineData("video")]
    public void LargeSitemapsPartitionAndIndexesReferenceOnlyLeaves(string kind)
    {
        WithSite(root =>
        {
            var options = new WebSitemapOptions
            {
                SiteRoot = root, BaseUrl = "https://example.test", IncludeHtmlFiles = false, IncludeTextFiles = false,
                SitemapIndexPath = string.Empty,
                Entries = Enumerable.Range(0, 50_001).Select(index => new WebSitemapEntry
                {
                    Path = $"/page-{index}/", ImageUrls = kind == "image" ? new[] { "/image.png" } : Array.Empty<string>(),
                    VideoUrls = kind == "video" ? new[] { "/video.mp4" } : Array.Empty<string>()
                }).ToArray(),
                ImageSitemap = kind == "image" ? new WebSitemapImageOptions() : null,
                VideoSitemap = kind == "video" ? new WebSitemapVideoOptions() : null
            };
            var result = WebSitemapGenerator.Generate(options);
            var endpoint = kind switch { "image" => result.ImageOutputPath!, "video" => result.VideoOutputPath!, _ => result.OutputPath };
            var index = XDocument.Load(endpoint);
            Assert.Equal(Ns + "sitemapindex", index.Root!.Name);
            var count = 0;
            foreach (var loc in index.Descendants(Ns + "loc"))
            {
                var path = Path.Combine(root, Path.GetFileName(new Uri(loc.Value).AbsolutePath));
                var leaf = XDocument.Load(path);
                Assert.Equal(Ns + "urlset", leaf.Root!.Name);
                var leafCount = leaf.Descendants(Ns + "url").Count();
                Assert.InRange(leafCount, 1, 50_000);
                Assert.True(new FileInfo(path).Length <= 50L * 1024 * 1024);
                count += leafCount;
                Assert.Contains(path, result.XmlOutputPaths);
            }
            Assert.Equal(50_001, count);
            foreach (var loc in XDocument.Load(result.IndexOutputPath!).Descendants(Ns + "loc"))
                Assert.Equal(Ns + "urlset", XDocument.Load(Path.Combine(root, Path.GetFileName(new Uri(loc.Value).AbsolutePath))).Root!.Name);

            var unrelated = Path.Combine(root, "sitemap.part-custom.xml");
            File.WriteAllText(unrelated, "User-owned sitemap.");
            options.Entries = new[] { options.Entries![0] };
            var smaller = WebSitemapGenerator.Generate(options);
            Assert.Equal(Ns + "urlset", XDocument.Load(smaller.OutputPath).Root!.Name);
            Assert.False(File.Exists(Path.Combine(root, "sitemap.part-0001.xml")));
            Assert.Equal("User-owned sitemap.", File.ReadAllText(unrelated));
        });
    }

    [Fact]
    public void NewsUsesOriginalPublicationDateAndPartitionsRecentRootAndLocalizedRoutes()
    {
        WithSite(root =>
        {
            var published = DateTimeOffset.UtcNow.AddHours(-12).ToString("O", CultureInfo.InvariantCulture);
            var entries = Enumerable.Range(0, 1001).Select(index => new WebSitemapEntry
            {
                Path = index % 2 == 0 ? $"/news/story-{index}/" : $"/pl/news/story-{index}/",
                Title = "Story", PublicationDate = published, LastModified = "2000-01-01"
            }).Concat(new[]
            {
                new WebSitemapEntry { Path = "/news/old/", PublicationDate = DateTimeOffset.UtcNow.AddDays(-3).ToString("O") },
                new WebSitemapEntry { Path = "/news/future/", PublicationDate = DateTimeOffset.UtcNow.AddDays(1).ToString("O") },
                new WebSitemapEntry { Path = "/news/undated/", LastModified = published }
            }).ToArray();
            var result = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = root, BaseUrl = "https://example.test", IncludeHtmlFiles = false, IncludeTextFiles = false,
                Entries = entries, NewsSitemap = new WebSitemapNewsOptions(), SitemapIndexPath = string.Empty
            });
            var index = XDocument.Load(result.NewsOutputPath!);
            Assert.Equal(Ns + "sitemapindex", index.Root!.Name);
            var total = 0;
            foreach (var loc in index.Descendants(Ns + "loc"))
            {
                var leaf = XDocument.Load(Path.Combine(root, Path.GetFileName(new Uri(loc.Value).AbsolutePath)));
                var count = leaf.Descendants(Ns + "url").Count();
                Assert.InRange(count, 1, 1000);
                total += count;
                Assert.All(leaf.Descendants(XNamespace.Get("http://www.google.com/schemas/sitemap-news/0.9") + "publication_date"),
                    node => Assert.Equal(DateTimeOffset.Parse(published), DateTimeOffset.Parse(node.Value)));
                Assert.DoesNotContain("/old/", leaf.ToString());
                Assert.DoesNotContain("/future/", leaf.ToString());
                Assert.DoesNotContain("/undated/", leaf.ToString());
            }
            Assert.Equal(1001, total);
            Assert.Equal(entries.Length, result.UrlCount); // Regular sitemap retains older and undated articles.
        });
    }

    private static void WithSite(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-sitemap-integrity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(root); }
        finally { Directory.Delete(root, true); }
    }
}
