using System.Text.Json;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed partial class WebSearchIntelligenceTests
{
    [Fact]
    public void Normalize_CanonicalizesSignedZeroMetricsAndIdentity()
    {
        var positiveZeroBatch = CreateBatch();
        positiveZeroBatch.Observations[0].ClickThroughRate = 0d;
        positiveZeroBatch.Observations[0].AveragePosition = 0d;
        var negativeZeroBatch = CreateBatch();
        negativeZeroBatch.Observations[0].ClickThroughRate = -0d;
        negativeZeroBatch.Observations[0].AveragePosition = -0d;

        var positive = WebSearchObservationNormalizer.Normalize(positiveZeroBatch);
        var negative = WebSearchObservationNormalizer.Normalize(negativeZeroBatch);

        Assert.Equal(0L, BitConverter.DoubleToInt64Bits(negative.Observations[0].ClickThroughRate!.Value));
        Assert.Equal(0L, BitConverter.DoubleToInt64Bits(negative.Observations[0].AveragePosition!.Value));
        Assert.Equal(positive.RunId, negative.RunId);
        Assert.Equal(positive.Observations[0].ObservationKey, negative.Observations[0].ObservationKey);
    }

    [Fact]
    public void Cli_ObserveImport_RejectsAnotherOptionAsProviderValue()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var inputPath = Path.Combine(root, "observations.json");
            var databasePath = Path.Combine(root, "search.db");
            File.WriteAllText(inputPath, JsonSerializer.Serialize(CreateBatch()));

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "observe",
                new[]
                {
                    "import", "--input", inputPath, "--database", databasePath,
                    "--provider", "--site", "officeimo"
                },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(2, exitCode);
            Assert.False(File.Exists(databasePath));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Cli_OpportunityList_RejectsAnotherOptionAsFilterValue()
    {
        var exitCode = WebCliCommandHandlers.HandleSubCommand(
            "opportunity",
            new[]
            {
                "list", "--database", "search.db", "--site", "officeimo",
                "--provider", "--from", "2026-08-01"
            },
            outputJson: true,
            logger: new WebConsoleLogger(),
            outputSchemaVersion: 1);

        Assert.Equal(2, exitCode);
    }

    [Theory]
    [InlineData("--provider")]
    [InlineData("--output")]
    public void Cli_ObserveImport_RejectsWhitespaceOptionValues(string optionName)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var inputPath = Path.Combine(root, "observations.json");
            var databasePath = Path.Combine(root, "search.db");
            File.WriteAllText(inputPath, JsonSerializer.Serialize(CreateBatch()));

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "observe",
                new[]
                {
                    "import", "--input", inputPath, "--database", databasePath,
                    optionName, " "
                },
                outputJson: false,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(2, exitCode);
            Assert.False(File.Exists(databasePath));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("--provider")]
    [InlineData("--from")]
    [InlineData("--to")]
    [InlineData("--min-impressions")]
    [InlineData("--min-ctr")]
    [InlineData("--output")]
    public void Cli_OpportunityList_RejectsWhitespaceOptionValues(string optionName)
    {
        var exitCode = WebCliCommandHandlers.HandleSubCommand(
            "opportunity",
            new[]
            {
                "list", "--database", "search.db", "--site", "officeimo",
                optionName, " "
            },
            outputJson: false,
            logger: new WebConsoleLogger(),
            outputSchemaVersion: 1);

        Assert.Equal(2, exitCode);
    }
}
