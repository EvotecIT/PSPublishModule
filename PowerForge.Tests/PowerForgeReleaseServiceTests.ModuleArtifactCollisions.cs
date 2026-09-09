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

    [Fact]
    public void ResolveProducedModuleArtifacts_RejectsCaseOnlyProducedAssetCollisionBeforeWritingArchive()
    {
        string root = CreateSandbox();
        try
        {
            string scriptRoot = Directory.CreateDirectory(Path.Combine(root, "script-layout")).FullName;
            string scriptPath = Path.Combine(scriptRoot, "company.tools.ps1");
            File.WriteAllText(scriptPath, "Get-Date");
            string archiveRoot = Directory.CreateDirectory(Path.Combine(root, "archives")).FullName;
            string packedPath = Path.Combine(archiveRoot, "COMPANY.TOOLS.zip");
            File.WriteAllText(packedPath, "preserve-existing-artefact");
            string scriptArchivePath = Path.Combine(archiveRoot, "company.tools.zip");
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
                        EntryPointRelativePath = "company.tools.ps1"
                    },
                    new PowerForgeModuleArtefactOutputSummary
                    {
                        Type = ArtefactType.ScriptPacked,
                        OutputRoot = archiveRoot,
                        OutputPath = packedPath,
                        EntryPointRelativePath = "Company.Tools.ps1"
                    }
                ]
            };

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                PowerForgeReleaseService.ResolveProducedModuleArtifacts(
                    new[] { packedPath },
                    new Dictionary<string, PowerForgeReleaseService.ModuleArtifactSnapshot>(),
                    plan,
                    archiveRoot));

            Assert.Contains("archive", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("case-insensitive", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve-existing-artefact", File.ReadAllText(packedPath));
            if (FrameworkCompatibility.GetPathStringComparisonForPath(root) == StringComparison.Ordinal)
                Assert.False(File.Exists(scriptArchivePath));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ResolveProducedModuleArtifacts_RejectsReportedScriptLayoutWithMissingEntryPoint()
    {
        string root = CreateSandbox();
        try
        {
            string scriptRoot = Directory.CreateDirectory(Path.Combine(root, "script-layout")).FullName;
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
                    }
                ]
            };

            IReadOnlyDictionary<string, PowerForgeReleaseService.ModuleArtifactSnapshot> baseline =
                PowerForgeReleaseService.CaptureModuleArtifactBaseline(Array.Empty<string>(), plan);

            Assert.Empty(baseline);
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                PowerForgeReleaseService.ResolveProducedModuleArtifacts(
                    Array.Empty<string>(),
                    baseline,
                    plan,
                    Path.Combine(root, "archives")));

            Assert.Contains("reported Script artefact", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("entry point", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }
}
