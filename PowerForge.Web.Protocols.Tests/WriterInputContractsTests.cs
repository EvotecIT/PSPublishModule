using System.Text;
using System.Xml.Linq;
using PowerForge.Web.Protocols.Feeds;
using PowerForge.Web.Protocols.Sitemaps;

namespace PowerForge.Web.Protocols.Tests;

public sealed class WriterInputContractsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyTypedSitemapsAreRejected(bool index)
        => Assert.Throws<ArgumentException>(() => SitemapWriter.Create(new SitemapDocument { IsIndex = index }));

    [Fact]
    public void EmptyPartitionsAreRejectedBeforePublishing()
        => Assert.Throws<ArgumentException>(() => SitemapWriter.Partition(Array.Empty<XElement>()));

    [Theory]
    [InlineData("yesterday")]
    [InlineData("2026-02-30")]
    [InlineData("2026-10")]
    [InlineData("")]
    [InlineData("2026-10-13T25:00:00Z")]
    public void PreservedInvalidDatesAreRejectedByBothWriterPaths(string date)
    {
        XNamespace ns = SitemapWriter.Namespace;
        var element = new XElement(ns + "url", new XElement(ns + "loc", "https://example.org/page"), new XElement(ns + "lastmod", date));
        var parsed = SitemapReader.Read(new XElement(ns + "urlset", element).ToString());
        Assert.Throws<ArgumentException>(() => SitemapWriter.Create(parsed));
        Assert.Throws<ArgumentException>(() => SitemapWriter.Partition(new[] { element }));
        parsed.Entries[0].IsSitemap = true;
        parsed.IsIndex = true;
        Assert.Throws<ArgumentException>(() => SitemapWriter.Create(parsed));
    }

    [Theory]
    [InlineData("/feeds/news")]
    [InlineData("relative-id")]
    [InlineData("//example.org/feed")]
    [InlineData("https://example.org/white space")]
    public void AtomRejectsRelativeOrWhitespaceIdentifiersAtEitherLevel(string id)
    {
        var item = new SyndicationItem { Id = "urn:item", Updated = DateTimeOffset.UtcNow };
        var feed = new SyndicationFeed { Id = id, Updated = DateTimeOffset.UtcNow,
            Authors = new[] { new FeedAuthor { Name = "Editor" } }, Items = new[] { item } };
        Assert.Throws<ArgumentException>(() => SyndicationWriter.WriteAtom(feed));
        feed.Id = "urn:feed";
        item.Id = id;
        Assert.Throws<ArgumentException>(() => SyndicationWriter.WriteAtom(feed));
        item.Id = "https://example.org/zażółć";
        Assert.Equal(item.Id, Assert.Single(SyndicationReader.ParseSyndicationFeed(SyndicationWriter.WriteAtom(feed)).Items).Id);
    }

    [Theory]
    [InlineData("utf-16")]
    [InlineData("iso-8859-1")]
    public void StreamReaderHonorsDeclaredEncodingAndLeavesInputOpen(string encodingName)
    {
        string xml = $"<?xml version='1.0' encoding='{encodingName}'?><urlset xmlns='{SitemapWriter.Namespace}'><url><loc>https://example.org/café</loc></url></urlset>";
        using var input = new MemoryStream(Encoding.GetEncoding(encodingName).GetBytes(xml));
        var document = SitemapReader.Read(input, strict: true);
        Assert.Equal("https://example.org/café", Assert.Single(document.Entries).Url);
        Assert.True(input.CanRead);
    }
}
