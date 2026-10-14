using System.Xml.Linq;
using PowerForge.Web.Protocols.Feeds;

namespace PowerForge.Web.Protocols.Tests;

public sealed class FeedMetadataContractsTests
{
    [Theory]
    [InlineData("<rss xmlns:x='urn:extension'><x:channel><x:title>Wrong</x:title></x:channel><channel><title>News</title><link>https://example.org/</link></channel></rss>")]
    [InlineData("<rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#' xmlns='http://purl.org/rss/1.0/' xmlns:x='urn:extension'><x:channel><x:title>Wrong</x:title></x:channel><channel><title>News</title><link>https://example.org/</link></channel></rdf:RDF>")]
    public void ChannelMetadataUsesTheCoreNamespace(string xml)
    {
        var feed = SyndicationReader.ParseSyndicationFeed(xml);
        Assert.Equal("News", feed.Title);
        Assert.Equal("https://example.org/", feed.Url);
    }

    [Fact]
    public void AtomUsesInheritedBasesAndCategoryTerms()
    {
        var feed = SyndicationReader.ParseSyndicationFeed("""
            <feed xmlns="http://www.w3.org/2005/Atom" xmlns:x="urn:extension" xml:base="https://publisher.example/news/">
              <link href="home"/><author xml:base="../people/"><name>Editor</name><uri xml:base="team/">alice</uri></author>
              <entry xml:base="2026/"><link xml:base="october/" href="article"/>
                <category term="release"><x:label>Stable</x:label></category>
                <author><name>Writer</name><uri>profile</uri></author>
              </entry>
            </feed>
            """, new Uri("https://transport.example/feed"));
        Assert.Equal("https://publisher.example/news/home", feed.Url);
        Assert.Equal("https://publisher.example/people/team/alice", Assert.Single(feed.Authors).Url);
        var item = Assert.Single(feed.Items);
        Assert.Equal("https://publisher.example/news/2026/october/article", item.Url);
        Assert.Equal("https://publisher.example/news/2026/profile", Assert.Single(item.Authors).Url);
        Assert.Equal("release", Assert.Single(item.Categories));
    }

    [Fact]
    public void AtomRequiresAuthorsAndAllowsFeedInheritanceOrEntryAuthors()
    {
        var date = DateTimeOffset.UtcNow;
        var author = new[] { new FeedAuthor { Name = "Editor" } };
        var missing = new SyndicationFeed { Id = "urn:feed", Updated = date,
            Items = new[] { new SyndicationItem { Id = "urn:item", Updated = date } } };
        Assert.Throws<ArgumentException>(() => SyndicationWriter.WriteAtom(missing));
        Assert.Throws<ArgumentException>(() => SyndicationWriter.WriteAtom(new SyndicationFeed { Id = "urn:feed", Updated = date }));
        XNamespace atom = "http://www.w3.org/2005/Atom";
        var inherited = new SyndicationFeed { Id = "urn:feed", Updated = date, Authors = author, Items = missing.Items };
        Assert.Equal("Editor", XDocument.Parse(SyndicationWriter.WriteAtom(inherited)).Root!.Element(atom + "author")!.Element(atom + "name")!.Value);
        var entries = new SyndicationFeed { Id = "urn:feed", Updated = date,
            Items = new[] { new SyndicationItem { Id = "urn:item", Updated = date, Authors = author } } };
        Assert.Equal("Editor", XDocument.Parse(SyndicationWriter.WriteAtom(entries)).Root!.Element(atom + "entry")!.Element(atom + "author")!.Element(atom + "name")!.Value);
    }

    [Fact]
    public void JsonFeedOmitsUnavailableOptionalMetadata()
    {
        var json = SyndicationWriter.WriteJsonFeed(new SyndicationFeed { Title = "News",
            Items = new[] { new SyndicationItem { Id = "42" } } });
#if NETFRAMEWORK
        var document = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
        var item = Assert.IsType<Dictionary<string, object>>(Assert.Single(Assert.IsAssignableFrom<System.Collections.IEnumerable>(document["items"])));
        Assert.Equal(string.Empty, item["content_text"]);
        Assert.False(item.ContainsKey("url"));
        Assert.False(item.ContainsKey("summary"));
        Assert.False(document.ContainsKey("home_page_url"));
        Assert.False(document.ContainsKey("feed_url"));
        Assert.False(document.ContainsKey("description"));
#else
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(string.Empty, item.GetProperty("content_text").GetString());
        Assert.False(item.TryGetProperty("url", out _));
        Assert.False(item.TryGetProperty("summary", out _));
        Assert.False(document.RootElement.TryGetProperty("home_page_url", out _));
        Assert.False(document.RootElement.TryGetProperty("feed_url", out _));
        Assert.False(document.RootElement.TryGetProperty("description", out _));
#endif
    }
}
