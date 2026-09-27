using PowerForge.Web.Cli;
using Xunit;

namespace PowerForge.Tests;

public sealed class WebPipelineRunnerExampleTitleTests
{
    [Theory]
    [InlineData("Example-BuildingTags", "Building Tags")]
    [InlineData("Example-SearchBuilder", "Search Builder")]
    [InlineData("Example-InlineOtherHtmlFile.ps1", "Inline Other Html File")]
    [InlineData("Example-IE", "IE")]
    [InlineData("Example44", "Example 44")]
    [InlineData("Example-01-BackupGPOs", "01 Backup GPOs")]
    [InlineData("csharp_functionality", "Csharp Functionality")]
    [InlineData("Test", "Test")]
    [InlineData("", "Example")]
    public void HumanizeExampleTitle_KeepsCapitalsAndDropsRedundantPrefix(string input, string expected)
    {
        Assert.Equal(expected, WebPipelineRunner.HumanizeExampleTitle(input));
    }
}
