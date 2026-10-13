using System.Xml.Linq;
using PowerForge.Web.Protocols.Feeds;
using PowerForge.Web.Protocols.Sitemaps;

namespace PowerForge.Web.Protocols.Tests;

public sealed class FeedSourceAndSitemapLimitTests
{
    [Theory]
    [InlineData("rss", null)]
    [InlineData("rss", "https://caller.example/source")]
    [InlineData("atom", null)]
    [InlineData("atom", "https://caller.example/source")]
    [InlineData("legacy", null)]
    public void FeedAndItemsShareResolvedSourceWithExplicitOverride(string format, string? source)
    {
        string xml = format == "rss" ? """
            <rss xmlns:atom="http://www.w3.org/2005/Atom" xml:base="https://publisher.example/">
              <channel><atom:link rel="self" xml:base="feeds/" href="news.xml"/><item><title>Item</title></item></channel>
            </rss>
            """ : $"""
            <feed xmlns="{(format == "atom" ? "http://www.w3.org/2005/Atom" : "http://purl.org/atom/ns#")}" xml:base="https://publisher.example/">
              <id>urn:feed</id><title>News</title><updated>2026-10-01T00:00:00Z</updated><author><name>Editor</name></author>
              <link rel="self" xml:base="feeds/" href="news.xml"/>
              <entry><id>urn:item</id><title>Item</title><updated>2026-10-01T00:00:00Z</updated></entry>
            </feed>
            """;
        var feed = SyndicationReader.ParseSyndicationFeed(xml, new Uri("https://transport.example/document"), source);
        var expected = source ?? "https://publisher.example/feeds/news.xml";
        Assert.Equal(expected, feed.SourceFeedUrl);
        Assert.Equal(expected, Assert.Single(feed.Items).SourceFeedUrl);
        var output = format == "rss" ? SyndicationWriter.WriteRss(feed) : SyndicationWriter.WriteAtom(feed);
        var roundTrip = SyndicationReader.ParseSyndicationFeed(output);
        Assert.Equal(expected, roundTrip.SourceFeedUrl);
        Assert.Equal(expected, Assert.Single(roundTrip.Items).SourceFeedUrl);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypedSitemapWriterEnforcesLocationLengthForBothDocumentKinds(bool index)
    {
        string location = "https://example.org/" + new string('a', 2048 - "https://example.org/".Length);
        var entry = new SitemapEntry { Url = location, IsSitemap = index };
        var document = new SitemapDocument { IsIndex = index, Entries = new[] { entry } };
        Assert.Equal(location, Assert.Single(SitemapReader.Read(SitemapWriter.Create(document).ToString(), strict: true).Entries).Url);
        entry.Url += "a";
        Assert.Throws<ArgumentException>(() => SitemapWriter.Create(document));
    }

    [Fact]
    public void PartitionUsesTheSameLocationLimitAsTypedWriting()
    {
        XNamespace ns = SitemapWriter.Namespace;
        string location = "https://example.org/" + new string('a', 2048 - "https://example.org/".Length);
        Assert.Single(SitemapWriter.Partition(new[] { new XElement(ns + "url", new XElement(ns + "loc", location)) }));
        Assert.Throws<ArgumentException>(() => SitemapWriter.Partition(new[] { new XElement(ns + "url", new XElement(ns + "loc", location + "a")) }));
    }
}
