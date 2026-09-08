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
    public void CreateModuleAssetEntries_DirectoryIncludesNestedProducedScriptEntryPoint()
    {
        string root = CreateSandbox();
        try
        {
            string scriptPath = Path.Combine(root, "app", "Company.Tools.ps1");
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

            string[] produced = PowerForgeReleaseService.ResolveProducedModuleArtifacts(
                new[] { root },
                baseline,
                plan);
            PowerForgeReleaseAssetEntry entry = Assert.Single(
                PowerForgeReleaseService.CreateModuleAssetEntries(root, plan, produced));

            Assert.Equal(new[] { scriptPath }, produced);
            Assert.Equal(scriptPath, entry.Path);
            Assert.True(entry.IsFinalPackageOutput);
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
