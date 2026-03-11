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

    [Fact]
    public void BuildSigningExcludeSubstrings_UsesConfiguredDeliveryInternalsPath()
    {
        var signing = new SigningOptionsConfiguration
        {
            IncludeInternals = false
        };
        var delivery = new DeliveryOptionsConfiguration
        {
            Enable = true,
            InternalsPath = "Assets"
        };

        var excludes = ModulePipelineRunner.BuildSigningExcludeSubstrings(signing, delivery);

        Assert.Contains("Assets", excludes);
        Assert.DoesNotContain("Internals", excludes);
        Assert.Contains("Modules", excludes);
    }

    [Fact]
    public void ApplyDeliverySigningPreference_EnablesInternalsAndRemovesInternalsExclude()
    {
        var signing = new SigningOptionsConfiguration
        {
            IncludeInternals = false,
            ExcludePaths = new[] { "Internals", "Modules", "IgnoreMe" }
        };
        var delivery = new DeliveryOptionsConfiguration
        {
            Enable = true,
            Sign = true,
            InternalsPath = "Internals"
        };

        var effective = ModulePipelineRunner.ApplyDeliverySigningPreference(signing, delivery);

        Assert.NotNull(effective);
        Assert.True(effective!.IncludeInternals);
        Assert.DoesNotContain("Internals", effective.ExcludePaths!);
        Assert.Contains("Modules", effective.ExcludePaths!);
        Assert.Contains("IgnoreMe", effective.ExcludePaths!);
    }
}
