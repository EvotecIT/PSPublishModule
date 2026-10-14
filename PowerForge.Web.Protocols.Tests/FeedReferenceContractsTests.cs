using System.Xml.Linq;
using PowerForge.Web.Protocols.Feeds;
using PowerForge.Web.Protocols.Sitemaps;

namespace PowerForge.Web.Protocols.Tests;

public sealed class FeedReferenceContractsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AtomRemoteContentAndFeedExtensionsSurviveRoundTrip(bool typed)
    {
        var feed = SyndicationReader.ParseSyndicationFeed($"""
            <feed xmlns="http://www.w3.org/2005/Atom" xmlns:x="urn:extension" xml:base="https://publisher.example/">
              <id>urn:feed</id><title>News</title><updated>2026-10-13T00:00:00Z</updated><author><name>Editor</name></author><x:rating>5</x:rating>
              <entry xml:base="articles/"><id>urn:item</id><title>Story</title><updated>2026-10-13T00:00:00Z</updated>
                <summary>Summary</summary><content {(typed ? "type='text/html'" : "")} xml:base="full/" src="article.html"/>
              </entry>
            </feed>
            """, new Uri("https://transport.example/feed"));
        var item = Assert.Single(feed.Items);
        Assert.Equal("https://publisher.example/articles/full/article.html", item.ContentSourceUrl);
        Assert.Null(item.Content);
        Assert.Equal(typed ? "text/html" : null, item.ContentType);
        XNamespace atom = "http://www.w3.org/2005/Atom";
        var output = XDocument.Parse(SyndicationWriter.WriteAtom(feed));
        var children = output.Root!.Elements().ToArray();
        Assert.True(Array.FindIndex(children, e => e.Name == XName.Get("rating", "urn:extension")) < Array.FindIndex(children, e => e.Name == atom + "entry"));
        var content = output.Root.Element(atom + "entry")!.Element(atom + "content")!;
        Assert.Equal(item.ContentSourceUrl, content.Attribute("src")!.Value);
        Assert.Empty(content.Nodes());
        Assert.Equal(item.ContentSourceUrl, Assert.Single(SyndicationReader.ParseSyndicationFeed(output.ToString()).Items).ContentSourceUrl);
        item.Content = "contradictory body";
        Assert.Throws<ArgumentException>(() => SyndicationWriter.WriteAtom(feed));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RdfSequenceDefinesOrderWithDocumentFallbackWhenAbsent(bool sequence)
    {
        var feed = SyndicationReader.ParseSyndicationFeed($"""
            <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#" xmlns="http://purl.org/rss/1.0/" xml:base="https://publisher.example/">
              <channel><title>News</title>{(sequence ? "<items xml:base='posts/'><rdf:Seq><rdf:li rdf:resource='b'/><rdf:li rdf:resource='missing'/><rdf:li rdf:resource='a'/></rdf:Seq></items>" : "")}</channel>
              <item xml:base="posts/" rdf:about="a"><title>A</title><link>a</link></item>
              <item rdf:about="https://publisher.example/posts/b"><title>B</title><link>posts/b</link></item>
              <item rdf:about="https://publisher.example/extra"><title>Extra</title><link>extra</link></item>
            </rdf:RDF>
            """, new Uri("https://transport.example/feed"));
        Assert.Equal(sequence ? new[] { "B", "A" } : new[] { "A", "B", "Extra" }, feed.Items.Select(i => i.Title));
        Assert.Equal(sequence ? "https://publisher.example/posts/b" : "https://publisher.example/posts/a", feed.Items[0].Url);
    }

    [Theory]
    [InlineData("https://example.org/a b", "https://example.org/a%20b")]
    [InlineData("https://example.org/café?q=a b&x=%2F", "https://example.org/caf%C3%A9?q=a%20b&x=%2F")]
    public void BothSitemapWritersEscapeLocationsWithoutMutatingInputs(string location, string expected)
    {
        foreach (bool index in new[] { false, true })
        {
            var entry = new SitemapEntry { Url = location, IsSitemap = index };
            var output = SitemapWriter.Create(new SitemapDocument { IsIndex = index, Entries = new[] { entry } });
            Assert.Equal(expected, Assert.Single(SitemapReader.Read(output.ToString()).Entries).Url);
            Assert.Equal(location, entry.Url);
        }
        XNamespace ns = SitemapWriter.Namespace;
        var element = new XElement(ns + "url", new XElement(ns + "loc", location));
        var partition = Assert.Single(SitemapWriter.Partition(new[] { element }));
        Assert.Equal(expected, Assert.Single(SitemapReader.Read(partition.ToString()).Entries).Url);
        Assert.Equal(location, element.Element(ns + "loc")!.Value);
    }

    [Fact]
    public void EmptyReferencesResolveToTheirXmlBaseWhileMissingAttributesDoNotMatch()
    {
        var feed = SyndicationReader.ParseSyndicationFeed("""
            <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#" xmlns="http://purl.org/rss/1.0/">
              <channel><items><rdf:Seq><rdf:li rdf:resource="https://example.org/a"/><rdf:li xml:base="https://example.org/b" rdf:resource=""/><rdf:li xml:base="https://example.org/c"/></rdf:Seq></items></channel>
              <item xml:base="https://example.org/a" rdf:about=""><title>A</title></item>
              <item rdf:about="https://example.org/b"><title>B</title></item>
              <item xml:base="https://example.org/c"><title>Missing</title></item>
            </rdf:RDF>
            """);
        Assert.Equal(new[] { "A", "B" }, feed.Items.Select(i => i.Title));
        var atom = SyndicationReader.ParseSyndicationFeed("""
            <feed xmlns="http://www.w3.org/2005/Atom"><entry><content xml:base="https://example.org/body" src=""/></entry></feed>
            """);
        Assert.Equal("https://example.org/body", Assert.Single(atom.Items).ContentSourceUrl);
    }

    [Fact]
    public void EscapingCannotExceedTheLocationLimit()
    {
        string location = "https://example.org/" + new string('é', 400);
        Assert.Throws<ArgumentException>(() => SitemapWriter.Create(new SitemapDocument { Entries = new[] { new SitemapEntry { Url = location } } }));
        XNamespace ns = SitemapWriter.Namespace;
        Assert.Throws<ArgumentException>(() => SitemapWriter.Partition(new[] { new XElement(ns + "url", new XElement(ns + "loc", location)) }));
    }
}
