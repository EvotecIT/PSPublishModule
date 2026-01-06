using Xunit;

namespace PowerForge.Tests;

public sealed class SigningIncludePatternsTests
{
    [Fact]
    public void BuildSigningIncludePatterns_DefaultsToIncludeBinaries()
    {
        var signing = new SigningOptionsConfiguration
        {
            Include = null,
            IncludeBinaries = null,
            IncludeExe = null
        };

        var patterns = ModulePipelineRunner.BuildSigningIncludePatterns(signing);

        Assert.Contains("*.ps1", patterns);
        Assert.Contains("*.psm1", patterns);
        Assert.Contains("*.psd1", patterns);
        Assert.Contains("*.dll", patterns);
        Assert.Contains("*.cat", patterns);
        Assert.DoesNotContain("*.exe", patterns);
    }

    [Fact]
    public void BuildSigningIncludePatterns_ExplicitlyIncludesBinaries()
    {
        var signing = new SigningOptionsConfiguration
        {
            Include = null,
            IncludeBinaries = true,
            IncludeExe = null
        };

        var patterns = ModulePipelineRunner.BuildSigningIncludePatterns(signing);

        Assert.Contains("*.ps1", patterns);
        Assert.Contains("*.psm1", patterns);
        Assert.Contains("*.psd1", patterns);
        Assert.Contains("*.dll", patterns);
        Assert.Contains("*.cat", patterns);
        Assert.DoesNotContain("*.exe", patterns);
    }

    [Fact]
    public void BuildSigningIncludePatterns_RespectsExplicitBinaryOptOut()      
    {
        var signing = new SigningOptionsConfiguration
        {
            Include = null,
            IncludeBinaries = false,
            IncludeExe = null
        };

        var patterns = ModulePipelineRunner.BuildSigningIncludePatterns(signing);

        Assert.Contains("*.ps1", patterns);
        Assert.Contains("*.psm1", patterns);
        Assert.Contains("*.psd1", patterns);
        Assert.DoesNotContain("*.dll", patterns);
        Assert.DoesNotContain("*.cat", patterns);
    }

    [Fact]
    public void BuildSigningIncludePatterns_UsesCustomIncludeVerbatim()
    {
        var signing = new SigningOptionsConfiguration
        {
            Include = new[] { " *.ps1 ", "*.dll" },
            IncludeBinaries = false
        };

        var patterns = ModulePipelineRunner.BuildSigningIncludePatterns(signing);

        Assert.Equal(new[] { "*.ps1", "*.dll" }, patterns);
    }
}
