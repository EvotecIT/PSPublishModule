using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebCliEcosystemStatsTests
{
    [Fact]
    public void HandleSubCommand_EcosystemStats_FailsWhenOutMissing()
    {
        var exitCode = WebCliCommandHandlers.HandleSubCommand(
            "ecosystem-stats",
            new[] { "--github-org", "EvotecIT" },
            outputJson: true,
            logger: new WebConsoleLogger(),
            outputSchemaVersion: 1);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public void HandleSubCommand_EcosystemStats_FailsWhenNoSourceProvided()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-ecosystem-stats-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var outPath = Path.Combine(root, "ecosystem-stats.json");
            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "ecosystem-stats",
                new[] { "--out", outPath },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(2, exitCode);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // ignore cleanup failures in tests
        }
    }
}
