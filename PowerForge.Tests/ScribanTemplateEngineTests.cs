using PowerForge.Web;

namespace PowerForge.Tests;

public class ScribanTemplateEngineTests
{
    [Fact]
    public void Render_PreservesWhitespaceInsideMultilineHtmlFragments()
    {
        var engine = new ScribanTemplateEngine();
        var context = new ThemeRenderContext
        {
            Page = new ContentItem
            {
                HtmlContent = "<pre><code>first();\n\nsecond();</code></pre>"
            }
        };

        var rendered = engine.Render(
            "<main>\n    {{ content }}\n</main>",
            context,
            _ => null);

        Assert.Contains("first();\n\nsecond();", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("first();\n    \n    second();", rendered, StringComparison.Ordinal);
    }
}
