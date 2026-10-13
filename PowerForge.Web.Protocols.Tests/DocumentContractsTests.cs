using System.Xml;
using System.Xml.Linq;
using PowerForge.Web.Protocols.Feeds;
using PowerForge.Web.Protocols.Sitemaps;
using PowerForge.Web.Protocols.StructuredData;

namespace PowerForge.Web.Protocols.Tests;

public sealed class DocumentContractsTests
{
    [Fact]
    public void ExtensionMetadataCannotReplaceRssItemTitleOrLink()
    {
        var item = Assert.Single(SyndicationReader.ParseSyndicationFeed("""
            <rss xmlns:atom="http://www.w3.org/2005/Atom" xmlns:x="urn:extension"><channel><item>
            <atom:link rel="self" href="https://example.org/feed"/><x:title>Extension title</x:title>
            <link>https://example.org/article</link><title>Article title</title>
            </item></channel></rss>
            """).Items);
        Assert.Equal("Article title", item.Title);
        Assert.Equal("https://example.org/article", item.Url);
    }

    [Fact]
    public void FeedRoundTripsPreserveIdentifiersDatesAuthorsAndExtensions()
    {
        var published = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var feed = new SyndicationFeed
        {
            Title = "A & B", Url = "https://example.org/", Id = "urn:feed:1", SourceFeedUrl = "https://example.org/feed",
            Updated = published.AddDays(1), Description = "News",
            Authors = new[] { new FeedAuthor { Name = "Editorial team", Url = "https://example.org/team" } },
            Items = new[] { new SyndicationItem
            {
                Id = "urn:opaque:42", Url = "https://example.org/news", Title = "A < B", Summary = "Summary",
                Published = published, Updated = published.AddHours(1), Content = "<p>Full & body</p>", ContentType = "html",
                Categories = new[] { "News" }, Authors = new[] { new FeedAuthor { Name = "Alice", Url = "https://example.org/alice" } },
                Extensions = new[] { new XElement(XName.Get("rating", "urn:example:extension"), "5") }
            } }
        };
        foreach (var xml in new[] { SyndicationWriter.WriteRss(feed), SyndicationWriter.WriteAtom(feed) })
        {
            var parsed = SyndicationReader.ParseSyndicationFeed(xml);
            Assert.Equal(feed.Title, parsed.Title);
            Assert.Equal("Editorial team", Assert.Single(parsed.Authors).Name);
            var item = Assert.Single(parsed.Items);
            Assert.Equal("urn:opaque:42", item.Id);
            Assert.False(item.IdIsPermaLink);
            Assert.Equal(published, item.Published);
            Assert.Equal(published.AddHours(1), item.Updated);
            Assert.Equal("Alice", Assert.Single(item.Authors).Name);
            Assert.Equal("News", Assert.Single(item.Categories));
            Assert.Equal("<p>Full & body</p>", item.Content);
            Assert.Equal("5", Assert.Single(item.Extensions).Value);
        }
        var json = SyndicationWriter.WriteJsonFeed(feed);
#if NETFRAMEWORK
        var payload = new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(json);
        var authors = (object[])((Dictionary<string, object>)payload)["authors"];
        var author = (Dictionary<string, object>)Assert.Single(authors);
        Assert.Equal("Editorial team", author["name"]);
        Assert.Equal("https://example.org/team", author["url"]);
#else
        using var payload = System.Text.Json.JsonDocument.Parse(json);
        var author = Assert.Single(payload.RootElement.GetProperty("authors").EnumerateArray());
        Assert.Equal("Editorial team", author.GetProperty("name").GetString());
        Assert.Equal("https://example.org/team", author.GetProperty("url").GetString());
#endif
    }

    [Fact]
    public void OpaqueRssIdentifierIsNotTurnedIntoAnArticleUrl()
    {
        var item = Assert.Single(SyndicationReader.ParseSyndicationFeed(
            "<rss><channel><item><guid isPermaLink='false'>incident-42</guid></item></channel></rss>", new Uri("https://example.org/feed")).Items);
        Assert.Equal("incident-42", item.Id);
        Assert.Equal(string.Empty, item.Url);
    }

    [Fact]
    public void AtomXhtmlContentRetainsMarkupAcrossWriting()
    {
        var feed = SyndicationReader.ParseSyndicationFeed("""
            <feed xmlns="http://www.w3.org/2005/Atom"><id>urn:feed:1</id><title>Feed</title><updated>2026-10-01T00:00:00Z</updated>
              <author><name>Publisher</name></author><entry><id>urn:entry:1</id><title>Item</title><updated>2026-10-01T00:00:00Z</updated>
              <content type="xhtml"><div xmlns="http://www.w3.org/1999/xhtml"><p>A &amp; B</p></div></content></entry></feed>
            """);
        var xml = XDocument.Parse(SyndicationWriter.WriteAtom(feed));
        XNamespace atom = "http://www.w3.org/2005/Atom";
        XNamespace xhtml = "http://www.w3.org/1999/xhtml";
        Assert.Equal("A & B", xml.Root!.Element(atom + "entry")!.Element(atom + "content")!.Element(xhtml + "div")!.Element(xhtml + "p")!.Value);
    }

    [Fact]
    public void SitemapRetainsDatePrecisionAndExtensionLocationsAreNotPages()
    {
        var document = SitemapReader.Read("""
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:image="urn:image">
            <url><loc>/Article</loc><lastmod>2026-10-01</lastmod><image:image><image:loc>https://example.org/photo</image:loc></image:image></url>
            <url><loc></loc></url></urlset>
            """, new Uri("https://example.org/sitemap.xml"));
        var entry = Assert.Single(document.Entries);
        Assert.False(entry.IsSitemap);
        Assert.Equal("https://example.org/Article", entry.Url);
        Assert.Equal("2026-10-01", entry.LastModifiedText);
        var parsed = SitemapReader.Read(SitemapWriter.Create(document).ToString());
        Assert.Equal(entry.LastModifiedText, Assert.Single(parsed.Entries).LastModifiedText);
        Assert.Single(parsed.Entries[0].Extensions);
    }

    [Fact]
    public void PartitionUsesUtf8BytesBeforeReachingTheEntryLimit()
    {
        XNamespace ns = SitemapWriter.Namespace;
        string path = new string('\u0142', 1800);
        var entries = Enumerable.Range(0, 16000).Select(i => new XElement(ns + "url",
            new XElement(ns + "loc", "https://example.org/" + path + "/" + i)));
        var partitions = SitemapWriter.Partition(entries);
        Assert.Equal(2, partitions.Count);
        Assert.Equal(16000, partitions.Sum(p => p.Root!.Elements().Count()));
        Assert.All(partitions, p => Assert.InRange(SitemapWriter.GetUtf8Size(p), 1L, SitemapWriter.MaximumBytes));
    }

    [Fact]
    public void PartitionPreservesOrderAndDoesNotModifyCallerElements()
    {
        XNamespace ns = SitemapWriter.Namespace;
        var source = new XElement(ns + "urlset", Enumerable.Range(0, 5).Select(i => new XElement(ns + "url", new XElement(ns + "loc", "https://example.org/" + i))));
        var partitions = SitemapWriter.Partition(source.Elements(), stylesheet: "/style?a=1&b=2", entryLimit: 2);
        Assert.Equal(new[] { 2, 2, 1 }, partitions.Select(p => p.Root!.Elements().Count()));
        Assert.Equal(5, source.Elements().Count());
        Assert.Equal(Enumerable.Range(0, 5).Select(i => "https://example.org/" + i), partitions.SelectMany(p => SitemapReader.Read(p.ToString()).Entries).Select(e => e.Url));
        Assert.All(partitions, p => Assert.True(SitemapWriter.GetUtf8Size(p) < SitemapWriter.MaximumBytes));
    }

    [Theory]
    [InlineData("<!DOCTYPE rss [<!ENTITY value 'secret'>]><rss><channel><title>&value;</title></channel></rss>")]
    [InlineData("<!DOCTYPE urlset SYSTEM 'file:///private.xml'><urlset/>")]
    public void XmlReadersRejectDtds(string xml)
    {
        Assert.Throws<XmlException>(() => SitemapReader.Read(xml));
        Assert.Throws<XmlException>(() => SyndicationReader.ParseSyndicationFeed(xml));
    }

    [Fact]
    public void JsonLdCannotCloseScriptAndPreservesPublisherValues()
    {
        var value = "</script><script>alert('x')</script> & Łódź 🏡";
        var document = new Dictionary<string, object?> { ["@context"] = "https://schema.org", ["@type"] = "Article", ["headline"] = value };
        var script = JsonLdWriter.WriteScript(document);
        Assert.Equal(1, script.Split(new[] { "</script>" }, StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("<script>alert", script);
        Assert.Equal(value, ReadJson(JsonLdWriter.Serialize(document))["headline"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void JsonFeedProvidesRequiredContentWhenFullContentIsOmitted(string? content)
    {
        var json = SyndicationWriter.WriteJsonFeed(new SyndicationFeed
        {
            Title = "News", Items = new[] { new SyndicationItem { Id = "42", Content = content, Summary = "Short summary" } }
        });
        Assert.Contains("content_text", json);
        Assert.Contains("Short summary", json);
        Assert.Contains("https://jsonfeed.org/version/1.1", json);
#if NETFRAMEWORK
        var model = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
        var entry = Assert.IsType<Dictionary<string, object>>(Assert.Single(Assert.IsAssignableFrom<System.Collections.IEnumerable>(model["items"])));
        Assert.Equal("Short summary", entry["content_text"]);
#else
        using var model = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("Short summary", Assert.Single(model.RootElement.GetProperty("items").EnumerateArray()).GetProperty("content_text").GetString());
#endif
    }

    private static IDictionary<string, object?> ReadJson(string json)
    {
#if NETFRAMEWORK
        return new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object?>>(json);
#else
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.GetString());
#endif
    }
}
