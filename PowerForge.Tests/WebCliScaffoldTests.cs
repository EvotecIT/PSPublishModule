using System.Text.Json;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public class WebCliScaffoldTests
{
    [Fact]
    public void HandleSubCommand_Scaffold_AppliesMaintenanceProfile()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-scaffold-" + Guid.NewGuid().ToString("N"));

        try
        {
            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "scaffold",
                new[] { "--out", root, "--maintenance-profile", "aggressive" },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(0, exitCode);

            using var maintenancePresetDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "config", "presets", "pipeline.web-maintenance.json")));
            var maintenanceSteps = maintenancePresetDoc.RootElement.GetProperty("steps").EnumerateArray().ToArray();
            var maintenanceArtifactsStep = maintenanceSteps.First(step =>
                string.Equals(step.GetProperty("task").GetString(), "github-artifacts-prune", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(3, maintenanceArtifactsStep.GetProperty("keep").GetInt32());
            Assert.Equal(7, maintenanceArtifactsStep.GetProperty("maxAgeDays").GetInt32());
            Assert.Equal(250, maintenanceArtifactsStep.GetProperty("maxDelete").GetInt32());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
