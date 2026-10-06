using PowerForge.Web;
using OfficeIMO.Markdown;

namespace PowerForge.Tests;

public sealed class MarkdownLinkMapperTests
{
    private static readonly IReadOnlyDictionary<string, string> Mappings = new Dictionary<string, string>
    {
        ["/docs/alpha/"] = "/projects/alpha/docs/",
        ["/docs/alpha/special/"] = "/guides/",
        ["/api/powershell/"] = "/projects/alpha/api/"
    };

    [Fact]
    public void Rewrite_MapsNavigationPreservingOriginalMarkdownAndCode()
    {
        var original = "\uFEFF---\r\ntitle: Guide\r\n---\r\n" +
            "[Guide](/docs/alpha/start/?format=word#example \"Title\")\r\n" +
            "[API](</api/powershell/Get-Thing/>)\r\n" +
            "[Special](/docs/alpha/special/page/)\r\n" +
            "![Image](/docs/alpha/image.png) [External](https://example.com/docs/alpha/)\r\n" +
            "[Other](/docs/alphabet/page/) [Fragment](#top)\r\n\r\n" +
            "`[Inline code](/docs/alpha/code/)`\r\n\r\n" +
            "```powershell\r\n[Code](/docs/alpha/code/)\r\n```\r\n";
        var expected = original
            .Replace("[Guide](/docs/alpha/", "[Guide](/projects/alpha/docs/", StringComparison.Ordinal)
            .Replace("[API](</api/powershell/", "[API](</projects/alpha/api/", StringComparison.Ordinal)
            .Replace("[Special](/docs/alpha/special/", "[Special](/guides/", StringComparison.Ordinal);

        Assert.Equal(expected, MarkdownLinkMapper.Rewrite(original, Mappings));
    }

    [Fact]
    public void Rewrite_MapsSharedReferenceDestinationOnceInNestedContent()
    {
        const string original = "> [First][guide]\n>\n> - **[Second][guide]**\n\n" +
            "| Link |\n| --- |\n| [Table][guide] |\n\n" +
            "[guide]: /docs/alpha/start/?x=1#part \"Guide\"\n";
        var expected = original.Replace("[guide]: /docs/alpha/", "[guide]: /projects/alpha/docs/", StringComparison.Ordinal);

        Assert.Equal(expected, MarkdownLinkMapper.Rewrite(original, Mappings));
    }

    [Fact]
    public void Rewrite_DoesNotCascadeMappings()
    {
        var mappings = new Dictionary<string, string>
        {
            ["/docs/"] = "/projects/",
            ["/projects/"] = "/other/"
        };
        Assert.Equal("[Guide](/projects/page/)", MarkdownLinkMapper.Rewrite("[Guide](/docs/page/)", mappings));
    }

    [Fact]
    public void Rewrite_RootMappingPreservesProtocolRelativeExternalLinks()
    {
        const string original = "[External](//example.com/guide/) [Local](/guide/)\n\n[Reference][ext]\n\n[ext]: //example.com/guide/";
        var expected = original.Replace("[Local](/guide/)", "[Local](/projects/alpha/guide/)", StringComparison.Ordinal);
        Assert.Equal(expected, MarkdownLinkMapper.Rewrite(original,
            new Dictionary<string, string> { ["/"] = "/projects/alpha/" }));
    }

    [Theory]
    [InlineData("[![Preview](/images/preview.png)](/docs/alpha/start/)")]
    [InlineData("See [![Preview](/images/preview.png)](/docs/alpha/start/) next.")]
    public void Rewrite_MapsLinkedImageNavigationAndPreservesImageSource(string markdown)
    {
        Assert.Equal(markdown.Replace("](/docs/alpha/", "](/projects/alpha/docs/", StringComparison.Ordinal),
            MarkdownLinkMapper.Rewrite(markdown, Mappings));
    }

    [Theory]
    [InlineData("[Guide](/docs/alpha&#47;start/?x=1&amp;amp;y=2#part)")]
    [InlineData("[Guide](/docs/alpha\\/start/?x=1&amp;amp;y=2#part)")]
    [InlineData("[Guide][g]\n\n[g]: /docs/alpha&#47;start/?x=1&amp;amp;y=2#part")]
    public void Rewrite_MapsDecodedPrefixesPreservingDestinationSemantics(string markdown)
    {
        var mapped = MarkdownLinkMapper.Rewrite(markdown, Mappings);
        var parsed = MarkdownReader.ParseWithSyntaxTree(mapped);
        var link = Assert.IsType<LinkInline>(Assert.IsType<ParagraphBlock>(parsed.Document.Blocks[0]).Inlines.Nodes[0]);
        Assert.Equal("/projects/alpha/docs/start/?x=1&amp;y=2#part", link.Url);
    }

    [Theory]
    [InlineData("https://example.com/", "/docs/")]
    [InlineData("//example.com/", "/docs/")]
    [InlineData("/docs", "/docs/")]
    [InlineData("/docs/", "/target/?x=1/")]
    public void Rewrite_RejectsInvalidRoutePrefixesEvenForEmptyMarkdown(string source, string target)
    {
        Assert.Throws<ArgumentException>(() => MarkdownLinkMapper.Rewrite(string.Empty,
            new Dictionary<string, string> { [source] = target }));
    }
}
