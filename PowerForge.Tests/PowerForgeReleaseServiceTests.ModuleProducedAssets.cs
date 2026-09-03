using System.IO.Compression;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void CreateModuleAssetEntries_ClassifiesProjectBuildNuGetPackage()
    {
        string root = CreateSandbox();
        try
        {
            string packagePath = Path.Combine(root, "Company.Library.4.0.0.nupkg");
            using (ZipArchive archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                ZipArchiveEntry nuspec = archive.CreateEntry("Company.Library.nuspec");
                using var writer = new StreamWriter(nuspec.Open());
                writer.Write("""
                    <?xml version="1.0"?>
                    <package>
                      <metadata>
                        <id>Company.Library</id>
                        <version>4.0.0</version>
                      </metadata>
                    </package>
                    """);
            }

            PowerForgeReleaseAssetEntry entry = Assert.Single(
                PowerForgeReleaseService.CreateModuleAssetEntries(
                    packagePath,
                    new PowerForgeModuleReleasePlanSummary { ModuleVersion = "4.0.0" },
                    new[] { packagePath }));

            Assert.Equal(PowerForgeReleaseAssetCategory.Package, entry.Category);
            Assert.Equal("ModuleProjectBuild", entry.Source);
            Assert.Equal("Company.Library", entry.Target);
            Assert.Equal("Company.Library", entry.PackageId);
            Assert.Equal("4.0.0", entry.Version);
            Assert.True(entry.IsFinalPackageOutput);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("Company.Library.4.0.0.snupkg")]
    [InlineData("Company.Library.4.0.0.symbols.nupkg")]
    public void CreateModuleAssetEntries_ClassifiesCurrentRunSymbolPackageAsFinal(string fileName)
    {
        string root = CreateSandbox();
        try
        {
            string packagePath = Path.Combine(root, fileName);
            using (ZipArchive archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                ZipArchiveEntry nuspec = archive.CreateEntry("Company.Library.nuspec");
                using var writer = new StreamWriter(nuspec.Open());
                writer.Write("<package><metadata><id>Company.Library</id><version>4.0.0</version></metadata></package>");
            }

            PowerForgeReleaseAssetEntry entry = Assert.Single(
                PowerForgeReleaseService.CreateModuleAssetEntries(
                    packagePath,
                    new PowerForgeModuleReleasePlanSummary { ModuleVersion = "4.0.0" },
                    new[] { packagePath }));

            Assert.Equal(PowerForgeReleaseAssetCategory.Package, entry.Category);
            Assert.Equal("Company.Library", entry.PackageId);
            Assert.True(entry.IsFinalPackageOutput);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void CreateModuleAssetEntries_ExistingNuGetPackageWithoutCurrentRunProofIsNotFinal()
    {
        string root = CreateSandbox();
        try
        {
            string packagePath = Path.Combine(root, "Company.Library.4.0.0.nupkg");
            using (ZipArchive archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                ZipArchiveEntry nuspec = archive.CreateEntry("Company.Library.nuspec");
                using var writer = new StreamWriter(nuspec.Open());
                writer.Write("<package><metadata><id>Company.Library</id><version>4.0.0</version></metadata></package>");
            }

            PowerForgeReleaseAssetEntry entry = Assert.Single(
                PowerForgeReleaseService.CreateModuleAssetEntries(
                    packagePath,
                    new PowerForgeModuleReleasePlanSummary { ModuleVersion = "4.0.0" },
                    producedArtifactPaths: Array.Empty<string>()));

            Assert.False(entry.IsFinalPackageOutput);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void CreateModuleAssetEntries_PreservesModuleArchiveClassification()
    {
        string root = CreateSandbox();
        try
        {
            string archivePath = Path.Combine(root, "Company.Tools.4.0.0.zip");
            File.WriteAllText(archivePath, "module");

            PowerForgeReleaseAssetEntry entry = Assert.Single(
                PowerForgeReleaseService.CreateModuleAssetEntries(archivePath));

            Assert.Equal(PowerForgeReleaseAssetCategory.Module, entry.Category);
            Assert.Equal("Module", entry.Source);
            Assert.Null(entry.PackageId);
        }
        finally
        {
            TryDelete(root);
        }
    }
}
