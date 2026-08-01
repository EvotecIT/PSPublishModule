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

    [Fact]
    public void CodeFence_PreservesLeadingIndentationAndLeadingBlankLines()
    {
        var document = new MarkdownDocumentBuilder();
        document.CodeFence("powershell", "\n    if ($true) {\n        Write-Host 'hi'\n    }\n");

        var markdown = document.ToString();

        Assert.Equal(
            """
            ```powershell

                if ($true) {
                    Write-Host 'hi'
                }
            ```
            """
            .ReplaceLineEndings(Environment.NewLine) + Environment.NewLine,
            markdown);
    }

    [Fact]
    public void CodeFence_UsesDelimiterLongerThanEmbeddedBackticks()
    {
        var document = new MarkdownDocumentBuilder();
        document.CodeFence("yaml", "Default value: first\n```\nlast");

        var markdown = document.ToString();

        Assert.Equal(
            "````yaml\nDefault value: first\n```\nlast\n````\n".ReplaceLineEndings(Environment.NewLine),
            markdown);
    }

    [Theory]
    [InlineData("System.Collections.Generic.List`1", "``System.Collections.Generic.List`1``")]
    [InlineData("`boundary`", "`` `boundary` ``")]
    [InlineData("a``b", "```a``b```")]
    public void InlineCode_UsesDelimiterLongerThanEmbeddedBackticks(string value, string expected)
    {
        Assert.Equal(expected, MarkdownDocumentBuilder.InlineCode(value));
    }

    [Fact]
    public void InlineIdentityCode_EncodesDistinctLineBreakCodeUnits()
    {
        Assert.Equal("`A%u000DB%u000AC`", MarkdownDocumentBuilder.InlineIdentityCode("A\rB\nC"));
    }
}
