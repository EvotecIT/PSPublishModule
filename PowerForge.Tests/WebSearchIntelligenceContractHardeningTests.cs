using System.Text.Json;
using DBAClientX;
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

    [Fact]
    public void Cli_HumanOutputEscapesProviderControlCharacters()
    {
        var escaped = WebCliCommandHandlers.EscapeSearchConsoleText(
            "line one\nline two\u001b[31m\u2028end",
            "fallback");

        Assert.Equal("line one\\u000Aline two\\u001B[31m\\u2028end", escaped);
        Assert.Equal("fallback", WebCliCommandHandlers.EscapeSearchConsoleText(null, "fallback"));
    }

    [Fact]
    public async Task SqliteStore_RefusesToClaimUnrelatedVersionZeroDatabase()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(root, "unrelated.db");
            await using var client = new SQLite();
            await client.ExecuteNonQueryAsync(databasePath, "CREATE TABLE unrelated_data (id INTEGER PRIMARY KEY);");
            var store = new SqliteWebSearchObservationStore(databasePath);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.ImportAsync(WebSearchObservationNormalizer.Normalize(CreateBatch())));

            Assert.Contains("nonempty schema-version-zero", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, Convert.ToInt32(await client.ExecuteScalarAsync(databasePath, "PRAGMA user_version;")));
            var searchObjects = await client.QueryAsListAsync(
                databasePath,
                "SELECT name FROM sqlite_master WHERE name LIKE 'search_%';",
                static record => record.GetString(0));
            Assert.Empty(searchObjects);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }
}
