using System;

namespace PowerForge.Tests;

public class MarkdownDocumentBuilderTests
{
    [Fact]
    public void ToString_RendersFrontMatterAndMarkdownBlocks()
    {
        var document = new MarkdownDocumentBuilder();
        document.FrontMatter("title", "Hello");
        document.FrontMatter("tags", new[] { "release", "announcement" });
        document.RawLine("# Hello");
        document.BlankLine();
        document.Paragraph("Starter content.");
        document.Bullets(new[] { "one", "two" });
        document.CodeFence("powershell", "Get-Thing");

        var markdown = document.ToString();

        Assert.Equal(
            """
            ---
            title: Hello
            tags:
            - release
            - announcement
            ---

            # Hello

            Starter content.

            - one
            - two

            ```powershell
            Get-Thing
            ```
            
            """
            .ReplaceLineEndings(Environment.NewLine) + Environment.NewLine,
            markdown);
    }
}
