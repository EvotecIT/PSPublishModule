using PowerForge.Web;

namespace PowerForge.Tests;

public class HtmlFragmentBuilderTests
{
    [Fact]
    public void ToString_RendersNestedIndentedLines()
    {
        var html = new HtmlFragmentBuilder(initialIndent: 2);

        html.Line("<div>");
        using (html.Indent())
        {
            html.Line("<span>Hi</span>");
        }
        html.Line("</div>");

        Assert.Equal(
            """
              <div>
                <span>Hi</span>
              </div>
            """
            .ReplaceLineEndings(Environment.NewLine) + Environment.NewLine,
            html.ToString());
    }

    [Fact]
    public void IsEmpty_TracksWrittenContent()
    {
        var html = new HtmlFragmentBuilder();

        Assert.True(html.IsEmpty);

        html.Line("<div></div>");

        Assert.False(html.IsEmpty);
    }
}
