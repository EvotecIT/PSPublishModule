using PowerForge;

namespace PowerForge.Tests;

public sealed class FormatConfigurationFactoryTests
{
    [Fact]
    public void Create_builds_merge_and_standard_formatting_options()
    {
        var factory = new FormatConfigurationFactory();

        var segment = factory.Create(new FormatConfigurationRequest
        {
            ApplyTo = new[] { "OnMergePSM1", "DefaultPSD1" },
            EnableFormatting = true,
            Sort = "Asc",
            RemoveCommentsSpecified = true,
            RemoveComments = true,
            RemoveEmptyLinesSpecified = true,
            RemoveEmptyLines = true,
            PlaceOpenBraceEnable = true,
            PlaceOpenBraceOnSameLine = true,
            UseCorrectCasingEnable = true,
            PSD1Style = "Native",
            UpdateProjectRoot = true
        });

        var config = Assert.IsType<ConfigurationFormattingSegment>(segment);
        Assert.True(config.Options.UpdateProjectRoot);
        var mergePsm1 = Assert.IsType<FormatCodeOptions>(config.Options.Merge.FormatCodePSM1);
        Assert.True(mergePsm1.Enabled);
        Assert.Equal("Asc", mergePsm1.Sort);
        Assert.True(mergePsm1.RemoveComments);
        Assert.True(mergePsm1.RemoveEmptyLines);
        var formatterSettings = Assert.IsType<FormatterSettingsOptions>(mergePsm1.FormatterSettings);
        var includeRules = Assert.IsType<string[]>(formatterSettings.IncludeRules);
        Assert.Contains("PSPlaceOpenBrace", includeRules);
        Assert.Contains("PSUseCorrectCasing", includeRules);
        var standardPsd1 = Assert.IsType<FormatCodeOptions>(config.Options.Standard.FormatCodePSD1);
        Assert.True(standardPsd1.Enabled);
        var style = Assert.IsType<FormattingStyleOptions>(config.Options.Standard.Style);
        Assert.Equal("Native", style.PSD1);
    }

    [Fact]
    public void Create_returns_null_when_no_settings_are_requested()
    {
        var factory = new FormatConfigurationFactory();

        var segment = factory.Create(new FormatConfigurationRequest
        {
            ApplyTo = new[] { "OnMergePSM1" }
        });

        Assert.Null(segment);
    }
}
