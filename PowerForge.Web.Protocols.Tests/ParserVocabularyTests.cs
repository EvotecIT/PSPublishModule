using System.Xml.Linq;
using PowerForge.Web.Protocols.Feeds;
using PowerForge.Web.Protocols.Sitemaps;

namespace PowerForge.Web.Protocols.Tests;

public sealed class ParserVocabularyTests
{
    [Theory]
    [InlineData("<root><item><title>Wrong</title></item></root>")]
    [InlineData("<feed xmlns='urn:vendor'><entry><title>Wrong</title></entry></feed>")]
    public void UnrelatedXmlDoesNotProduceFeedEntries(string xml)
    {
        Assert.Empty(SyndicationReader.ParseSyndicationFeed(xml).Items);
    }

    [Theory]
    [InlineData("http://www.w3.org/2005/Atom")]
    [InlineData("http://purl.org/atom/ns#")]
    public void SupportedAtomVocabulariesProduceDirectEntries(string ns)
    {
        var feed = SyndicationReader.ParseSyndicationFeed($"<feed xmlns='{ns}'><entry><title>News</title></entry><extension xmlns='urn:x'><entry xmlns='{ns}'><title>Wrong</title></entry></extension></feed>");
        Assert.Equal("News", Assert.Single(feed.Items).Title);
    }

    [Theory]
    [InlineData("<rss><channel><item><title>News</title></item><extension><item><title>Wrong</title></item></extension></channel><item><title>Wrong</title></item></rss>")]
    [InlineData("<rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#' xmlns='http://purl.org/rss/1.0/'><channel><title>Feed</title><item><title>Wrong</title></item></channel><item><title>News</title></item></rdf:RDF>")]
    public void RssEntriesComeFromTheirCoreContainer(string xml)
    {
        Assert.Equal("News", Assert.Single(SyndicationReader.ParseSyndicationFeed(xml).Items).Title);
    }

    [Fact]
    public void RssPreservesAtomDiscoveryLinksAndConsumesChannelSelfLink()
    {
        var feed = SyndicationReader.ParseSyndicationFeed("""
            <rss xmlns:atom="http://www.w3.org/2005/Atom"><channel><title>News</title><link>https://example.org/</link>
              <atom:link rel="self" href="https://example.org/feed"/><atom:link rel="hub" href="https://hub.example/"/>
              <item><title>Item</title><atom:link rel="next" href="https://example.org/next"/></item>
            </channel></rss>
            """);
        Assert.Equal("https://example.org/feed", feed.SourceFeedUrl);
        Assert.Equal("hub", Assert.Single(feed.Extensions).Attribute("rel")!.Value);
        Assert.Equal("next", Assert.Single(Assert.Single(feed.Items).Extensions).Attribute("rel")!.Value);
        XNamespace atom = "http://www.w3.org/2005/Atom";
        var channel = XDocument.Parse(SyndicationWriter.WriteRss(feed)).Root!.Element("channel")!;
        Assert.Equal(new[] { "self", "hub" }, channel.Elements(atom + "link").Select(link => link.Attribute("rel")!.Value));
    }

    [Theory]
    [InlineData("urlset", "url")]
    [InlineData("sitemapindex", "sitemap")]
    public void SitemapStrictModeRequiresProtocolNamespace(string root, string entry)
    {
        string Xml(string ns) => $"<{root} xmlns='{ns}'><{entry}><loc>https://example.org/page</loc></{entry}></{root}>";
        Assert.Throws<InvalidOperationException>(() => SitemapReader.Read(Xml("urn:vendor"), strict: true));
        Assert.Empty(SitemapReader.Read(Xml("urn:vendor")).Entries);
        Assert.Throws<InvalidOperationException>(() => SitemapReader.Read(Xml(""), strict: true));
        Assert.Single(SitemapReader.Read(Xml("")).Entries);
        Assert.Single(SitemapReader.Read(Xml(SitemapWriter.Namespace), strict: true).Entries);
    }
}
