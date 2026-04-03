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

    [Fact]
    public void HandleSubCommand_Scaffold_WithMultiProjectApiSuiteStarter_CreatesSuiteStarter()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-scaffold-api-suite-" + Guid.NewGuid().ToString("N"));

        try
        {
            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "scaffold",
                new[] { "--out", root, "--engine", "scriban", "--starter-profile", "multi-project-api-suite" },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(Path.Combine(root, "data", "projects", "catalog.json")));
            Assert.True(File.Exists(Path.Combine(root, "data", "projects", "catalog.project-template.json")));
            Assert.True(File.Exists(Path.Combine(root, "data", "projects", "api-suite-narrative.json")));
            Assert.True(File.Exists(Path.Combine(root, "data", "projects", "sample-project-api-guides.json")));
            Assert.True(File.Exists(Path.Combine(root, "projects-sources", "README.md")));
            Assert.True(File.Exists(Path.Combine(root, "content", "docs", "projects", "api-guide-template.md")));
            Assert.True(File.Exists(Path.Combine(root, "themes", "nova", "partials", "api-header.html")));
            Assert.True(File.Exists(Path.Combine(root, "themes", "nova", "partials", "api-footer.html")));
            Assert.True(File.Exists(Path.Combine(root, "themes", "nova", "assets", "api.css")));

            using var presetDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "config", "presets", "pipeline.web-quality.json")));
            var apiStep = presetDoc.RootElement.GetProperty("steps").EnumerateArray().First(step =>
                string.Equals(step.GetProperty("task").GetString(), "project-apidocs", StringComparison.OrdinalIgnoreCase));

            Assert.Equal("./data/projects/catalog.json", apiStep.GetProperty("catalog").GetString());
            Assert.Equal("./projects-sources", apiStep.GetProperty("sourcesRoot").GetString());
            Assert.Equal("./_site/projects", apiStep.GetProperty("outRoot").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void HandleSubCommand_Scaffold_WithSuiteProjectSeed_MaterializesFirstProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-scaffold-api-suite-seeded-" + Guid.NewGuid().ToString("N"));

        try
        {
            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "scaffold",
                new[]
                {
                    "--out", root,
                    "--engine", "scriban",
                    "--starter-profile", "multi-project-api-suite",
                    "--suite-project-slug", "adplayground",
                    "--suite-project-name", "ADPlayground",
                    "--suite-project-surface", "dotnet"
                },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(0, exitCode);

            using var catalogDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "data", "projects", "catalog.json")));
            var project = catalogDoc.RootElement.GetProperty("projects")[0];
            Assert.Equal("adplayground", project.GetProperty("slug").GetString());
            Assert.True(project.GetProperty("surfaces").GetProperty("apiDotNet").GetBoolean());
            Assert.Equal("ADPlaygroundClient", project.GetProperty("apiDocs").GetProperty("quickStartTypes").GetString());

            Assert.True(File.Exists(Path.Combine(root, "content", "docs", "projects", "adplayground-quick-start.md")));
            Assert.True(File.Exists(Path.Combine(root, "data", "projects", "adplayground-api-guides.json")));
            Assert.True(File.Exists(Path.Combine(root, "projects-sources", "adplayground", "README.md")));
            Assert.True(File.Exists(Path.Combine(root, "projects-sources", "adplayground", "dotnet", "README.md")));
            Assert.True(File.Exists(Path.Combine(root, "projects-sources", "adplayground", "dotnet", "templates", "ADPlaygroundClient.xml.template")));
            Assert.True(File.Exists(Path.Combine(root, "projects-sources", "adplayground", "dotnet", "templates", "ADPlaygroundClient.dll.placeholder.txt")));
            Assert.True(File.Exists(Path.Combine(root, "projects-sources", "adplayground", "dotnet", "promote-from-build.ps1")));

            var promoteScript = File.ReadAllText(Path.Combine(root, "projects-sources", "adplayground", "dotnet", "promote-from-build.ps1"));
            Assert.Contains("[string] $XmlPath", promoteScript, StringComparison.Ordinal);
            Assert.Contains("[string] $AssemblyPath", promoteScript, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
