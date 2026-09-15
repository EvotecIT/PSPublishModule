using System.IO.Compression;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void CreateModuleAssetEntries_DirectoryIncludesProducedDefaultScriptEntryPointOnly()
    {
        string root = CreateSandbox();
        try
        {
            string scriptPath = Path.Combine(root, "Company.Tools.ps1");
            string unrelatedPath = Path.Combine(root, "Other.Tools.ps1");
            File.WriteAllText(scriptPath, "Get-Date");
            File.WriteAllText(unrelatedPath, "Get-ChildItem");
            var plan = new PowerForgeModuleReleasePlanSummary
            {
                ManifestPath = Path.Combine(root, "Company.Tools.psd1"),
                ModuleName = "Company.Tools",
                ModuleVersion = "4.0.0",
                ArtefactOutputs =
                [
                    new PowerForgeModuleArtefactOutputSummary
                    {
                        Type = ArtefactType.Script,
                        OutputPath = root,
                        EntryPointRelativePath = "Company.Tools.ps1"
                    }
                ]
            };

            PowerForgeReleaseAssetEntry entry = Assert.Single(
                PowerForgeReleaseService.CreateModuleAssetEntries(
                    root,
                    plan,
                    new[] { scriptPath, unrelatedPath }));

            Assert.Equal(scriptPath, entry.Path);
            Assert.Equal(PowerForgeReleaseAssetCategory.Module, entry.Category);
            Assert.True(entry.IsFinalPackageOutput);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ResolveProducedModuleArtifacts_ArchivesCompleteNestedScriptLayout()
    {
        string root = CreateSandbox();
        string archiveRoot = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            string scriptPath = Path.Combine(root, "app", "Company.Tools.ps1");
            string supportPath = Path.Combine(root, "support", "helper.ps1");
            var plan = new PowerForgeModuleReleasePlanSummary
            {
                ManifestPath = Path.Combine(root, "Company.Tools.psd1"),
                ModuleName = "Company.Tools",
                ModuleVersion = "4.0.0",
                ArtefactOutputs =
                [
                    new PowerForgeModuleArtefactOutputSummary
                    {
                        Type = ArtefactType.Script,
                        OutputPath = root,
                        EntryPointRelativePath = "app/Company.Tools.ps1"
                    }
                ]
            };
            IReadOnlyDictionary<string, PowerForgeReleaseService.ModuleArtifactSnapshot> baseline =
                PowerForgeReleaseService.CaptureModuleArtifactBaseline(new[] { root }, plan);
            Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
            File.WriteAllText(scriptPath, "Get-Date");
            Directory.CreateDirectory(Path.GetDirectoryName(supportPath)!);
            File.WriteAllText(supportPath, "function Get-Support { 'ok' }");
            Directory.CreateDirectory(Path.Combine(root, "runtime", "empty"));

            string[] produced = PowerForgeReleaseService.ResolveProducedModuleArtifacts(
                new[] { root },
                baseline,
                plan,
                archiveRoot);
            string archivePath = Assert.Single(produced);
            PowerForgeReleaseAssetEntry entry = Assert.Single(
                PowerForgeReleaseService.CreateModuleAssetEntries(archivePath, plan, produced));

            Assert.Equal(Path.Combine(archiveRoot, "Company.Tools.zip"), archivePath);
            using (ZipArchive archive = ZipFile.OpenRead(archivePath))
            {
                Assert.Contains(archive.Entries, static item => item.FullName == "app/Company.Tools.ps1");
                Assert.Contains(archive.Entries, static item => item.FullName == "support/helper.ps1");
                Assert.Contains(archive.Entries, static item => item.FullName == "runtime/empty/");
            }
            Assert.Equal(archivePath, entry.Path);
            Assert.True(entry.IsFinalPackageOutput);
        }
        finally
        {
            TryDelete(root);
            TryDelete(archiveRoot);
        }
    }

    [Fact]
    public void ModuleArtifactProvenance_PreservesCaseDistinctEvidenceButRejectsCollidingReleaseArchives()
    {
        string root = CreateSandbox();
        try
        {
            if (FrameworkCompatibility.GetPathStringComparisonForPath(root) != StringComparison.Ordinal)
                return;

            string lowerPath = Path.Combine(root, "app", "tool.ps1");
            string upperPath = Path.Combine(root, "App", "Tool.ps1");
            Directory.CreateDirectory(Path.GetDirectoryName(lowerPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(upperPath)!);
            File.WriteAllText(lowerPath, "'lower-before'");
            File.WriteAllText(upperPath, "'upper-before'");
            var plan = new PowerForgeModuleReleasePlanSummary
            {
                ArtefactOutputs =
                [
                    new PowerForgeModuleArtefactOutputSummary
                    {
                        Type = ArtefactType.Script,
                        OutputPath = Path.GetDirectoryName(lowerPath)!,
                        EntryPointRelativePath = Path.GetFileName(lowerPath)
                    },
                    new PowerForgeModuleArtefactOutputSummary
                    {
                        Type = ArtefactType.Script,
                        OutputPath = Path.GetDirectoryName(upperPath)!,
                        EntryPointRelativePath = Path.GetFileName(upperPath)
                    }
                ]
            };

            IReadOnlyDictionary<string, PowerForgeReleaseService.ModuleArtifactSnapshot> baseline =
                PowerForgeReleaseService.CaptureModuleArtifactBaseline(Array.Empty<string>(), plan);
            Assert.Equal(2, baseline.Count);

            File.WriteAllText(lowerPath, "'lower-after'");
            File.WriteAllText(upperPath, "'upper-after'");
            string archiveRoot = Path.Combine(root, "archives");
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                PowerForgeReleaseService.ResolveProducedModuleArtifacts(
                    Array.Empty<string>(),
                    baseline,
                    plan,
                    archiveRoot));

            Assert.Contains("archive", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("case-insensitive", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(archiveRoot));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ResolveModuleArtefactOutputs_CarriesDefaultScriptEntryPoint()
    {
        string root = CreateSandbox();
        try
        {
            var context = new ModulePipelineConfigurationContext
            {
                ProjectRoot = root,
                Spec = new ModulePipelineSpec
                {
                    Build = new ModuleBuildSpec
                    {
                        Name = "Company.Tools",
                        SourcePath = root,
                        Version = "4.0.0"
                    },
                    Segments =
                    [
                        new ConfigurationArtefactSegment
                        {
                            ArtefactType = ArtefactType.Script,
                            Configuration = new ArtefactConfiguration
                            {
                                Enabled = true,
                                Path = "scripts"
                            }
                        }
                    ]
                }
            };

            PowerForgeModuleArtefactOutputSummary output = Assert.Single(
                PowerForgeReleaseService.ResolveModuleArtefactOutputs(context));

            Assert.Equal(ArtefactType.Script, output.Type);
            Assert.Equal(Path.Combine(root, "scripts"), output.OutputPath);
            Assert.Equal("Company.Tools.ps1", output.EntryPointRelativePath);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void CreateModuleAssetEntries_DirectoryRejectsProducedPackedArchiveWithDifferentManifestVersion()
    {
        string root = CreateSandbox();
        try
        {
            string archivePath = Path.Combine(root, "Company.Tools.zip");
            WritePackedModuleArchive(archivePath, "3.9.9");
            PowerForgeModuleReleasePlanSummary plan = CreatePackedModulePlan(root, archivePath, "4.0.0");

            PowerForgeReleaseAssetEntry[] entries = PowerForgeReleaseService
                .CreateModuleAssetEntries(root, plan, new[] { archivePath })
                .ToArray();

            Assert.Empty(entries);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void CreateModuleAssetEntries_DirectPackedArchiveWithDifferentManifestVersionIsNotFinal()
    {
        string root = CreateSandbox();
        try
        {
            string archivePath = Path.Combine(root, "Company.Tools.zip");
            WritePackedModuleArchive(archivePath, "3.9.9");
            PowerForgeModuleReleasePlanSummary plan = CreatePackedModulePlan(root, archivePath, "4.0.0");

            PowerForgeReleaseAssetEntry entry = Assert.Single(
                PowerForgeReleaseService.CreateModuleAssetEntries(
                    archivePath,
                    plan,
                    new[] { archivePath }));

            Assert.False(entry.IsFinalPackageOutput);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static PowerForgeModuleReleasePlanSummary CreatePackedModulePlan(
        string root,
        string archivePath,
        string version)
        => new()
        {
            ManifestPath = Path.Combine(root, "Company.Tools.psd1"),
            ModuleName = "Company.Tools",
            ModuleVersion = version,
            ArtefactOutputs =
            [
                new PowerForgeModuleArtefactOutputSummary
                {
                    Type = ArtefactType.Packed,
                    OutputRoot = root,
                    OutputPath = archivePath
                }
            ]
        };

    private static void WritePackedModuleArchive(string archivePath, string version)
    {
        using ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        ZipArchiveEntry manifest = archive.CreateEntry("Company.Tools/Company.Tools.psd1");
        using var writer = new StreamWriter(manifest.Open());
        writer.Write("@{ RootModule = 'Company.Tools.psm1'; ModuleVersion = '" + version + "' }");
    }
}
