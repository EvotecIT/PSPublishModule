namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void ResolveProducedModuleArtifacts_RejectsScriptArchiveCollisionBeforeOverwrite()
    {
        string root = CreateSandbox();
        try
        {
            string scriptRoot = Directory.CreateDirectory(Path.Combine(root, "script-layout")).FullName;
            string scriptPath = Path.Combine(scriptRoot, "Company.Tools.ps1");
            File.WriteAllText(scriptPath, "Get-Date");
            string archiveRoot = Directory.CreateDirectory(Path.Combine(root, "archives")).FullName;
            string archivePath = Path.Combine(archiveRoot, "Company.Tools.zip");
            File.WriteAllText(archivePath, "preserve-existing-artefact");
            var plan = new PowerForgeModuleReleasePlanSummary
            {
                ModuleName = "Company.Tools",
                ModuleVersion = "4.0.0",
                ArtefactOutputs =
                [
                    new PowerForgeModuleArtefactOutputSummary
                    {
                        Type = ArtefactType.Script,
                        OutputRoot = scriptRoot,
                        OutputPath = scriptRoot,
                        EntryPointRelativePath = "Company.Tools.ps1"
                    },
                    new PowerForgeModuleArtefactOutputSummary
                    {
                        Type = ArtefactType.ScriptPacked,
                        OutputRoot = archiveRoot,
                        OutputPath = archivePath,
                        EntryPointRelativePath = "Company.Tools.ps1"
                    }
                ]
            };

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                PowerForgeReleaseService.ResolveProducedModuleArtifacts(
                    new[] { archivePath },
                    new Dictionary<string, PowerForgeReleaseService.ModuleArtifactSnapshot>(),
                    plan,
                    archiveRoot));

            Assert.Contains("archive", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("produced", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve-existing-artefact", File.ReadAllText(archivePath));
        }
        finally
        {
            TryDelete(root);
        }
    }
}
