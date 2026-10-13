using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using PowerForge.Web.Protocols.Feeds;
using PowerForge.Web.Protocols.Sitemaps;

namespace PowerForge.Web.Protocols.Tests;

public sealed class FeedBaseAndSchemaContractsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RssLinksAndPermalinkFallbackUseTheirOwnInheritedBases(bool rdf)
    {
        string item = """
            <item xml:base="posts/"><link xml:base="current/">story</link></item>
            <item xml:base="archive/"><guid xml:base="2026/">story</guid></item>
            <item><guid isPermaLink="false">opaque-id</guid></item>
            """;
        string xml = rdf
            ? $"<rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#' xmlns='http://purl.org/rss/1.0/' xml:base='https://publisher.example/'><channel xml:base='news/'><link xml:base='../home/'>index</link></channel>{item}</rdf:RDF>"
            : $"<rss xml:base='https://publisher.example/'><channel xml:base='news/'><link xml:base='../home/'>index</link>{item}</channel></rss>";
        var feed = SyndicationReader.ParseSyndicationFeed(xml, new Uri("https://transport.example/feed"));
        Assert.Equal("https://publisher.example/home/index", feed.Url);
        string prefix = "https://publisher.example/" + (rdf ? "" : "news/");
        Assert.Equal(prefix + "posts/current/story", feed.Items[0].Url);
        Assert.Equal(prefix + "archive/2026/story", feed.Items[1].Url);
        Assert.Equal("story", feed.Items[1].Id);
        Assert.Equal("", feed.Items[2].Url);
        Assert.Equal("opaque-id", feed.Items[2].Id);
    }

    [Theory]
    [InlineData("http://purl.org/atom/ns#", "url", "tagline")]
    [InlineData("http://www.w3.org/2005/Atom", "uri", "subtitle")]
    public void AtomVersionsPreserveFeedAndEntryAuthorProfiles(string ns, string profile, string description)
    {
        var feed = SyndicationReader.ParseSyndicationFeed($"""
            <feed xmlns="{ns}" xml:base="https://publisher.example/">
              <id>urn:feed</id><title>News</title><updated>2026-10-13T00:00:00Z</updated><{description}>Description</{description}>
              <author xml:base="people/"><name>Editor</name><{profile} xml:base="staff/">editor</{profile}></author>
              <entry xml:base="posts/"><id>urn:item</id><title>Story</title><updated>2026-10-13T00:00:00Z</updated>
                <author xml:base="../people/"><name>Writer</name><{profile}>writer</{profile}></author>
              </entry>
            </feed>
            """, new Uri("https://transport.example/feed"));
        Assert.Equal("Description", feed.Description);
        Assert.Equal("https://publisher.example/people/staff/editor", Assert.Single(feed.Authors).Url);
        Assert.Equal("https://publisher.example/people/writer", Assert.Single(Assert.Single(feed.Items).Authors).Url);
        var roundTrip = SyndicationReader.ParseSyndicationFeed(SyndicationWriter.WriteAtom(feed));
        Assert.Equal(feed.Description, roundTrip.Description);
        Assert.Equal(feed.Authors[0].Url, roundTrip.Authors[0].Url);
        Assert.Equal(feed.Items[0].Authors[0].Url, roundTrip.Items[0].Authors[0].Url);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublishedSchemasAcceptTheProtocolCountAndFractionalDates(bool index)
    {
        var entries = Enumerable.Range(0, SitemapWriter.MaximumEntries).Select(i => new SitemapEntry
        {
            Url = "https://example.org/" + i, IsSitemap = index,
            LastModifiedText = "2026-10-13T12:00:00.1234567+02:00"
        }).ToList();
        var input = new SitemapDocument { IsIndex = index, Entries = entries };
        var output = SitemapWriter.Create(input);
        var schemas = new XmlSchemaSet();
        using var reader = XmlReader.Create(new StringReader(index ? SitemapSchemas.GetSitemapIndexSchema() : SitemapSchemas.GetSitemapSchema()));
        schemas.Add(SitemapWriter.Namespace, reader);
        var failures = new List<string>();
        output.Validate(schemas, (_, args) => failures.Add(args.Message));
        Assert.Empty(failures);
        Assert.Equal(SitemapWriter.MaximumEntries, output.Root!.Elements().Count());
        entries.Add(new SitemapEntry { Url = "https://example.org/overflow", IsSitemap = index });
        Assert.Throws<ArgumentException>(() => SitemapWriter.Create(input));
    }
}
