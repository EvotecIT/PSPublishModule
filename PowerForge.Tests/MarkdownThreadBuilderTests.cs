using PowerForgeStudio.Domain.Hub;

namespace PowerForge.Tests;

public class MarkdownThreadBuilderTests
{
    [Fact]
    public void Paragraph_AddsBlankLineLikeMarkdownDocumentBuilder()
    {
        var builder = new MarkdownThreadBuilder();

        builder.Paragraph("Hello");

        Assert.Equal("Hello" + Environment.NewLine + Environment.NewLine, builder.ToString());
    }

    [Fact]
    public void Heading_ThrowsForInvalidLevels()
    {
        var builder = new MarkdownThreadBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Heading(0, "Bad"));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Heading(7, "Bad"));
    }
}
