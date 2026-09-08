using System.IO.Compression;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
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
