using System;
using PowerForge.Web;
using Xunit;

public class WebApiDocsGeneratorSourceCoverageTests
{
    [Theory]
    [InlineData("../../../../_/CodeGlyphX/Barcode.cs")]
    [InlineData("../../CodeGlyphX/CodeGlyphX/Barcode.cs")]
    public void GitHubRepoMismatchHint_IgnoresLeadingRelativePdbSegments(string sourcePath)
    {
        var url = new Uri("https://github.com/EvotecIT/CodeGlyphX/blob/master/CodeGlyphX/Barcode.cs#L49");

        var mismatch = WebApiDocsGenerator.IsGitHubRepoMismatchHint(sourcePath, url, out var hint);

        Assert.False(mismatch);
        Assert.Equal(string.Empty, hint);
    }

    [Fact]
    public void GitHubRepoMismatchHint_StillDetectsWrongRepositoryAfterRelativeSegments()
    {
        var url = new Uri("https://github.com/EvotecIT/CodeGlyphX/blob/master/CodeGlyphX/Barcode.cs#L49");

        var mismatch = WebApiDocsGenerator.IsGitHubRepoMismatchHint(
            "../../WrongRepository/WrongRepository/Barcode.cs",
            url,
            out var hint);

        Assert.True(mismatch);
        Assert.Equal("WrongRepository -> CodeGlyphX", hint);
    }
}
