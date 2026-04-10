using System;

namespace PowerForge.Tests;

public class MarkdownTableBuilderTests
{
    [Fact]
    public void ToString_RendersHeadersAlignmentAndRows()
    {
        var table = new MarkdownTableBuilder(
            ["Name", "Count"],
            [MarkdownTableAlignment.Left, MarkdownTableAlignment.Right]);

        table.AddRow("Widgets", "12");

        Assert.Equal(
            """
            | Name | Count |
            | --- | ---: |
            | Widgets | 12 |
            """
            .ReplaceLineEndings(Environment.NewLine) + Environment.NewLine,
            table.ToString());
    }

    [Fact]
    public void AddRow_ThrowsWhenCellCountDoesNotMatchHeaders()
    {
        var table = new MarkdownTableBuilder(["Name", "Count"]);

        Assert.Throws<ArgumentException>(() => table.AddRow("Widgets"));
    }
}
