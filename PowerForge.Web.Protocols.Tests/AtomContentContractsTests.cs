using System.Xml;
using System.Xml.Linq;
using PowerForge.Web.Protocols.Feeds;

namespace PowerForge.Web.Protocols.Tests;

public sealed class AtomContentContractsTests
{
    [Theory]
    [InlineData("application/xml")]
    [InlineData("text/xml")]
    [InlineData("application/vnd.example+xml")]
    [InlineData("APPLICATION/XML; charset=utf-8")]
    public void XmlMediaPayloadPreservesMarkupAndNamespacesThroughReadingAndWriting(string type)
    {
        var feed = SyndicationReader.ParseSyndicationFeed($"""
            <feed xmlns="http://www.w3.org/2005/Atom" xmlns:p="urn:payload">
              <id>urn:feed</id><title>News</title><updated>2026-10-13T00:00:00Z</updated><author><name>Editor</name></author>
              <entry><id>urn:item</id><title>Item</title><updated>2026-10-13T00:00:00Z</updated>
                <content type="{type}"><p:payload p:key="a&amp;b">before <p:child><![CDATA[<raw>]]></p:child> after</p:payload></content>
              </entry>
            </feed>
            """);
        var item = Assert.Single(feed.Items);
        var expected = XElement.Parse(item.Content!);
        Assert.Equal(XName.Get("payload", "urn:payload"), expected.Name);
        Assert.Equal("a&b", expected.Attribute(XName.Get("key", "urn:payload"))!.Value);
        Assert.Equal("<raw>", expected.Element(XName.Get("child", "urn:payload"))!.Value);
        XNamespace atom = "http://www.w3.org/2005/Atom";
        var output = XDocument.Parse(SyndicationWriter.WriteAtom(feed));
        var content = output.Root!.Element(atom + "entry")!.Element(atom + "content")!;
        Assert.Equal(type, content.Attribute("type")!.Value);
        Assert.True(XNode.DeepEquals(expected, Assert.Single(content.Elements())));
        var reread = Assert.Single(SyndicationReader.ParseSyndicationFeed(output.ToString()).Items);
        Assert.True(XNode.DeepEquals(expected, XElement.Parse(reread.Content!)));
    }

    [Theory]
    [InlineData("text", "a < b & c")]
    [InlineData("html", "<p>A & B</p>")]
    [InlineData("text/plain", "a < b & c")]
    [InlineData("image/png", "YWJjZA==")]
    public void NonXmlBodiesRemainEscapedText(string type, string body)
    {
        var feed = MakeFeed(type, body);
        XNamespace atom = "http://www.w3.org/2005/Atom";
        var output = SyndicationWriter.WriteAtom(feed);
        var content = XDocument.Parse(output).Root!.Element(atom + "entry")!.Element(atom + "content")!;
        Assert.Empty(content.Elements());
        Assert.Equal(body, content.Value);
        Assert.Equal(body, Assert.Single(SyndicationReader.ParseSyndicationFeed(output).Items).Content);
    }

    [Theory]
    [InlineData("<!DOCTYPE payload [<!ENTITY external SYSTEM 'file:///not-read'>]><payload>&external;</payload>")]
    [InlineData("<payload>&unknown;</payload>")]
    [InlineData("<payload>")]
    public void XmlWritingRejectsDtdEntitiesAndMalformedFragments(string body)
        => Assert.Throws<XmlException>(() => SyndicationWriter.WriteAtom(MakeFeed("application/xml", body)));

    private static SyndicationFeed MakeFeed(string type, string body) => new()
    {
        Id = "urn:feed", Title = "News", Updated = DateTimeOffset.UtcNow,
        Authors = new[] { new FeedAuthor { Name = "Editor" } },
        Items = new[] { new SyndicationItem { Id = "urn:item", Title = "Item", Updated = DateTimeOffset.UtcNow, ContentType = type, Content = body } }
    };
}
