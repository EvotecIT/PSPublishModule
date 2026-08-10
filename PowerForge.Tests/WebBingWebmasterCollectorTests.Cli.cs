using System.Text.Json;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed partial class WebBingWebmasterCollectorTests
{
    [Fact]
    public void Cli_ImportBing_RejectsADisabledProviderBeforeCreatingStorage()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-bing-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var configPath = Path.Combine(root, "providers.json");
            var inputPath = Path.Combine(root, "bing.csv");
            var databasePath = Path.Combine(root, "search.db");
            var configuration = CreateConfiguration(BingWebmasterCsvExportParser.ProviderKind);
            configuration.Sites[0].Providers[0].Enabled = false;
            File.WriteAllText(configPath, JsonSerializer.Serialize(configuration));
            File.WriteAllText(inputPath, "Date,Page,Clicks,Impressions\n2026-08-01,https://officeimo.com/,1,10\n");

            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "observe",
                [
                    "import-bing", "--config", configPath, "--input", inputPath, "--database", databasePath,
                    "--site", "officeimo", "--provider", "bing-webmaster", "--from", "2026-08-01", "--to", "2026-08-01",
                    "--collected-at", "2026-08-10T12:34:56Z", "--output", "json"
                ],
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(2, exitCode);
            Assert.False(File.Exists(databasePath));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
