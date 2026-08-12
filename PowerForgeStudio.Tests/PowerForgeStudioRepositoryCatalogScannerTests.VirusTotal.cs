using PowerForgeStudio.Orchestrator.Catalog;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioRepositoryCatalogScannerTests
{
    [Fact]
    public void InspectRepository_DisabledVirusTotalOnlyConfig_DoesNotReplaceProjectBuildWorkflow()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryPath = scope.CreateRepository("DisabledVirusTotalRepo");
        var buildPath = Path.Combine(repositoryPath, "Build");
        File.Delete(Path.Combine(buildPath, "Build-Module.ps1"));
        var projectBuildScript = Path.Combine(buildPath, "Build-Project.ps1");
        File.WriteAllText(projectBuildScript, "# project build");
        File.WriteAllText(
            Path.Combine(buildPath, "release.json"),
            """{ "VirusTotal": { "Enabled": false } }""");

        var entry = new RepositoryCatalogScanner().InspectRepository(repositoryPath);

        Assert.Null(entry.UnifiedReleaseConfigPath);
        Assert.Equal(projectBuildScript, entry.ProjectBuildScriptPath);
        Assert.Equal(projectBuildScript, entry.PrimaryBuildScriptPath);
    }
}
