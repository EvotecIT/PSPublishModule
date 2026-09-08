using HtmlTinkerX;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebMarkdownHeadingAnchorTests
{
    [Fact]
    public void GeneratedAnchorsRemainUsableForNonLatinAndRepeatedHeadings()
    {
        var html = MarkdownRenderer.RenderToHtml("""
            <h2>プライバシー</h2>
            <h2>隐私</h2>
            <h2>隐私</h2>
            <h2>Evotecは私のホームを見ることができますか？</h2>
            <h2>Evotecはホームを操作できますか？</h2>
            <h2>!!!</h2>
            """);
        var headings = HtmlParser.ParseWithAngleSharp(html).QuerySelectorAll("h2");
        var ids = headings.Select(heading => heading.GetAttribute("id")).ToArray();

        Assert.Equal(new[] { "プライバシー", "隐私", "隐私-2", "evotec", "evotec-2", "heading" }, ids);
        Assert.Contains("Evotecはホームを操作できますか？", html);
    }

    [Fact]
    public void GeneratedAnchorsReserveExplicitTargetsAnywhereInTheDocument()
    {
        var html = MarkdownRenderer.RenderToHtml("""
            <h2>Privacy</h2>
            <h2 id="privacy">Authored heading</h2>
            <div id="privacy-2">Existing target</div>
            <h2>Privacy</h2>
            <h2>Terms &amp; conditions</h2>
            """);
        var document = HtmlParser.ParseWithAngleSharp(html);
        var ids = document.QuerySelectorAll("h2").Select(heading => heading.GetAttribute("id")).ToArray();

        Assert.Equal(new[] { "privacy-3", "privacy", "privacy-4", "terms-amp-conditions" }, ids);
        Assert.NotNull(document.QuerySelector("div#privacy-2"));
    }

    [Fact]
    public void RawHeadingsDoNotCollideWithMarkdownGeneratedAnchors()
    {
        var html = MarkdownRenderer.RenderToHtml("""
            ## Privacy

            <h2>Privacy</h2>
            """);
        var ids = HtmlParser.ParseWithAngleSharp(html).QuerySelectorAll("h2")
            .Select(heading => heading.GetAttribute("id")).ToArray();

        Assert.Equal(new[] { "privacy", "privacy-2" }, ids);
    }
}
