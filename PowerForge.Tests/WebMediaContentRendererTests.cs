using HtmlTinkerX;
using PowerForge.Web;

namespace PowerForge.Tests;

public class WebMediaContentRendererTests
{
    [Fact]
    public void Content_PreservesPictureMarkupAndCreatesScopedLinks()
    {
        const string picture = "<picture><source srcset='/large.webp 2x'><img src='/small.png' alt='Step &amp; detail'></picture>";
        const string code = "<pre><code>&lt;img src='/example.png'&gt;</code></pre>";
        string html = "<html lang='en'><body><article data-pf-media-scope='content'><h2>Setup</h2>" + picture + "<p><img src='next.png' alt='Next'></p>" + code + "</article></body></html>";
        string result = WebMediaContentRenderer.Render(html);
        var doc = HtmlParser.ParseWithHtmlAgilityPack(result);
        Assert.Equal(2, doc.DocumentNode.SelectNodes("//a[@data-pf-media]").Count);
        Assert.Contains(picture, result);
        Assert.Contains(code, result);
        Assert.Contains("View images (2)", result);
        Assert.Contains("Setup · Step &amp; detail", result);
        Assert.Equal(result, WebMediaContentRenderer.Render(result));
    }

    [Fact]
    public void Previews_PreserveMultilineSelfClosingImageAnchorBoundaries()
    {
        const string anchor = "<a class=\"preview\" href=\"/demo.html\">\r\n  <img src=\"/demo.png\" alt=\"Demo\" />\r\n</a>";
        string result = WebMediaContentRenderer.Render("<section data-pf-media-scope=\"previews\">" + anchor + "</section>");
        Assert.Contains("\r\n</a><a class=\"pf-media-preview-action\"", result);
        Assert.DoesNotContain("</<a", result);
        var doc = HtmlParser.ParseWithHtmlAgilityPack(result);
        Assert.NotNull(doc.DocumentNode.SelectSingleNode("//section/a[@href='/demo.html']/following-sibling::a[@data-pf-media-for]"));
    }

    [Fact]
    public void Content_PreservesNavigationAndSkipsDecorationsAndOptOuts()
    {
        const string html = """
            <article data-pf-media-scope="content" data-pf-media-exclude="/placeholder.png">
            <a href="/product"><img src="/product.png" alt="Product"></a>
            <img src="/logo.png" alt="Logo" width="32"><img src="/decoration.png" alt="">
            <img src="/placeholder.png" alt="Placeholder"><img src="/off.png" alt="Off" data-pf-media="off">
            <p>Inline <img src="/inline.png" alt="inline"> text</p>
            <div hidden><img src="/hidden.png" alt="Hidden"></div>
            <div data-pf-media-scope="off"><div data-pf-media-scope="content"><img src="/nested.png" alt="Nested"></div></div>
            </article>
            """;
        Assert.Equal(html, WebMediaContentRenderer.Render(html));
    }

    [Fact]
    public void Previews_KeepDestinationsAndProvideSeparateIdempotentActions()
    {
        const string html = """
            <section data-pf-media-scope="previews">
            <a href="/demos/chart.html"><img src="/chart.png" alt="Chart"></a>
            <a href="/product/"><img id="product-img" src="/product.png" alt="Product"></a>
            <a href="/archive.zip" download><img src="/archive.png" alt="Download"></a>
            </section>
            """;
        string result = WebMediaContentRenderer.Render(html);
        var doc = HtmlParser.ParseWithHtmlAgilityPack(result);
        Assert.NotNull(doc.DocumentNode.SelectSingleNode("//a[@href='/demos/chart.html']/img"));
        Assert.NotNull(doc.DocumentNode.SelectSingleNode("//a[@href='/product/']/img"));
        var actions = doc.DocumentNode.SelectNodes("//a[@data-pf-media-for]");
        Assert.Equal(2, actions.Count);
        foreach (var action in actions) Assert.Equal("img", doc.GetElementbyId(action.GetAttributeValue("data-pf-media-for", "")).Name);
        Assert.Equal(result, WebMediaContentRenderer.Render(result));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/png;base64,AA")]
    [InlineData("file:///tmp/image.png")]
    [InlineData("&#106;avascript:alert(1)")]
    public void Content_RejectsNonHttpSources(string source)
    {
        string html = $"<div data-pf-media-scope='content'><img src='{source}' alt='Image'></div>";
        Assert.Equal(html, WebMediaContentRenderer.Render(html));
    }

    [Fact]
    public void NestedAndSingleScopes_KeepIndependentMembership()
    {
        string result = WebMediaContentRenderer.Render("""
            <article data-pf-media-scope="content"><img src="/a.png" alt="A">
            <section data-pf-media-scope="content"><img src="/b.png" alt="B"><img src="/c.png" alt="C"></section>
            <section data-pf-media-scope="single"><img src="/d.png" alt="D"></section>
            </article>
            """);
        var doc = HtmlParser.ParseWithHtmlAgilityPack(result);
        string Group(string href) => doc.DocumentNode.SelectSingleNode($"//a[@href='{href}']").GetAttributeValue("data-pf-media-group", "");
        Assert.NotEqual(Group("/a.png"), Group("/b.png"));
        Assert.Equal(Group("/b.png"), Group("/c.png"));
        Assert.Equal("", Group("/d.png"));
        Assert.Single(doc.DocumentNode.SelectNodes("//a[@data-pf-media-open-group]"));
    }
}
