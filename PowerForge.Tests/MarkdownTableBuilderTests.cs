using System;

namespace PowerForge.Tests;

public class MarkdownTableBuilderTests
{
    [Fact]
    public void ToString_RendersHeadersAlignmentAndRows()
    {
        var table = new MarkdownTableBuilder(
            ["Name", "Status", "Count"],
            [MarkdownTableAlignment.Left, MarkdownTableAlignment.Center, MarkdownTableAlignment.Right]);

        table.AddRow("Widgets", "stable", "12");

        Assert.Equal(
            """
            | Name | Status | Count |
            | --- | :---: | ---: |
            | Widgets | stable | 12 |
            """
            .ReplaceLineEndings(Environment.NewLine) + Environment.NewLine,
            table.ToString());
    }

    [Fact]
    public void ToString_EscapesPipesInHeadersAndCells()
    {
        var table = new MarkdownTableBuilder(["Name | Alias", "Count"]);

        table.AddRow(@"Widget \| Gadget", "12");

        Assert.Equal(
            """
            | Name \| Alias | Count |
            | --- | --- |
            | Widget \\\| Gadget | 12 |
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
