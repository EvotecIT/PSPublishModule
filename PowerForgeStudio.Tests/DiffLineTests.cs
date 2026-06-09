using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Tests;

public sealed class DiffLineTests
{
    [Fact]
    public void Parse_PlusLine_IsAdded()
    {
        var result = DiffLine.Parse("+added line");
        Assert.Equal(DiffLineKind.Added, result.Kind);
    }

    [Fact]
    public void Parse_MinusLine_IsRemoved()
    {
        var result = DiffLine.Parse("-removed line");
        Assert.Equal(DiffLineKind.Removed, result.Kind);
    }

    [Fact]
    public void Parse_HunkHeader_IsHunk()
    {
        var result = DiffLine.Parse("@@ -1,3 +1,4 @@");
        Assert.Equal(DiffLineKind.Hunk, result.Kind);
    }

    [Fact]
    public void Parse_TriplePlusLine_IsContext()
    {
        var result = DiffLine.Parse("+++ b/file.txt");
        Assert.Equal(DiffLineKind.Context, result.Kind);
    }

    [Fact]
    public void Parse_RegularLine_IsContext()
    {
        var result = DiffLine.Parse(" context line");
        Assert.Equal(DiffLineKind.Context, result.Kind);
    }
}
