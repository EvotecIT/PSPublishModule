using PowerForge.Web;

namespace PowerForge.Tests;

public class ScribanTemplateEngineTests
{
    [Fact]
    public void Render_CompletesLargeGalleryWithNestedPartials()
    {
        var cards = Enumerable.Range(0, 40).Select(index => new Dictionary<string, object?>
        {
            ["body"] = new string('x', 20_000),
            ["id"] = index
        }).ToArray();
        var context = new ThemeRenderContext
        {
            Data = new Dictionary<string, object?> { ["cards"] = cards }
        };
        var rendered = new ScribanTemplateEngine().Render(
            "<main>{{ for card in data.cards }}{{ include 'card' }}{{ end }}</main><footer>Done</footer>",
            context,
            name => name == "card" ? "<article>{{ include 'screen' }}</article>" : "<pre>{{ card.body }}</pre><a href='/demo-{{ card.id }}/'>Demo</a>");

        var expected = "<main>" + string.Concat(Enumerable.Range(0, 40).Select(index =>
            "<article><pre>" + new string('x', 20_000) + "</pre><a href='/demo-" + index + "/'>Demo</a></article>"))
            + "</main><footer>Done</footer>";
        Assert.Equal(expected, rendered);
    }

    [Fact]
    public void Render_RejectsCumulativeOutputExhaustionInsteadOfTruncating()
    {
        var context = new ThemeRenderContext
        {
            Page = new ContentItem { HtmlContent = new string('x', 1024 * 1024 - 1) }
        };
        var error = Assert.Throws<Scriban.Syntax.ScriptRuntimeException>(() =>
            new ScribanTemplateEngine().Render("{{ for i in 1..40 }}{{ content }}{{ end }}", context, _ => null));
        Assert.Contains("OutputLimit", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_RejectsOversizedStringInsteadOfReturningAnEllipsis()
    {
        var context = new ThemeRenderContext
        {
            Page = new ContentItem { HtmlContent = new string('x', 33 * 1024 * 1024) }
        };
        Assert.Throws<Scriban.Syntax.ScriptRuntimeException>(() =>
            new ScribanTemplateEngine().Render("<main>{{ content }}</main>", context, _ => null));
    }

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
