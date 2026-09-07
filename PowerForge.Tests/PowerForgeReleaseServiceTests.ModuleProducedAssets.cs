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

    [Fact]
    public void CreateModuleAssetEntries_DirectoryUsesCurrentRunManifestlessScriptPackedArchiveOnly()
    {
        string root = CreateSandbox();
        try
        {
            string currentScriptPackedPath = Path.Combine(root, "Company.Tools.ScriptPacked.zip");
            using (ZipArchive archive = ZipFile.Open(currentScriptPackedPath, ZipArchiveMode.Create))
            {
                ZipArchiveEntry script = archive.CreateEntry("Company.Tools.ps1");
                using var writer = new StreamWriter(script.Open());
                writer.Write("Get-Date");
            }

            string staleUnrelatedPath = Path.Combine(root, "Legacy.Tools.4.0.0.zip");
            using (ZipArchive archive = ZipFile.Open(staleUnrelatedPath, ZipArchiveMode.Create))
            {
                ZipArchiveEntry script = archive.CreateEntry("Legacy.Tools.ps1");
                using var writer = new StreamWriter(script.Open());
                writer.Write("Get-ChildItem");
            }

            PowerForgeReleaseAssetEntry entry = Assert.Single(
                PowerForgeReleaseService.CreateModuleAssetEntries(
                    root,
                    new PowerForgeModuleReleasePlanSummary
                    {
                        ManifestPath = Path.Combine(root, "Company.Tools.psd1"),
                        ModuleName = "Company.Tools",
                        ModuleVersion = "4.0.0"
                    },
                    new[] { currentScriptPackedPath }));

            Assert.Equal(currentScriptPackedPath, entry.Path);
            Assert.Equal(PowerForgeReleaseAssetCategory.Module, entry.Category);
            Assert.Equal("Module", entry.Source);
            Assert.True(entry.IsFinalPackageOutput);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void CreateModuleAssetEntries_ProducedPathsRespectCaseSensitiveFileSystem()
    {
        string root = CreateSandbox();
        try
        {
            if (FrameworkCompatibility.GetPathStringComparison(root) != StringComparison.Ordinal)
                return;

            string producedPath = Path.Combine(root, "Company.Tools.ScriptPacked.zip");
            string caseDistinctPath = Path.Combine(root, "company.tools.scriptpacked.zip");
            foreach (string archivePath in new[] { producedPath, caseDistinctPath })
            {
                using ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
                ZipArchiveEntry script = archive.CreateEntry("Company.Tools.ps1");
                using var writer = new StreamWriter(script.Open());
                writer.Write("Get-Date");
            }

            PowerForgeReleaseAssetEntry entry = Assert.Single(
                PowerForgeReleaseService.CreateModuleAssetEntries(
                    root,
                    new PowerForgeModuleReleasePlanSummary
                    {
                        ManifestPath = Path.Combine(root, "Company.Tools.psd1"),
                        ModuleName = "Company.Tools",
                        ModuleVersion = "4.0.0"
                    },
                    new[] { producedPath }));

            Assert.Equal(producedPath, entry.Path);
            Assert.True(entry.IsFinalPackageOutput);
        }
        finally
        {
            TryDelete(root);
        }
    }
}
