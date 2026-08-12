using System.IO.Compression;

namespace PowerForge.Tests;

public sealed class PowerForgeReleaseModulePackageProvenanceTests
{
    [Fact]
    public void CreateModuleAssetEntries_ValidBuiltModuleArchive_IsVerifiedFinalPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var archivePath = Path.Combine(root, "ExampleModule.v1.0.0.zip");
        try
        {
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                archive.CreateEntry("ExampleModule/ExampleModule.psd1");
                archive.CreateEntry("ExampleModule/ExampleModule.psm1");
            }

            var entry = Assert.Single(PowerForgeReleaseService.CreateModuleAssetEntries(
                archivePath,
                producedArtifactPaths: new[] { archivePath }));

            Assert.True(entry.IsFinalPackageOutput);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CreateModuleAssetEntries_SourceRepositoryArchive_IsNotVerifiedFinalPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var archivePath = Path.Combine(root, "ExampleModule-source.zip");
        try
        {
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                archive.CreateEntry("ExampleModule-main/ExampleModule-main.psd1");
                archive.CreateEntry("ExampleModule-main/ExampleModule-main.psm1");
                archive.CreateEntry("ExampleModule-main/.github/workflows/build.yml");
                archive.CreateEntry("ExampleModule-main/tests/Example.Tests.ps1");
            }

            var entry = Assert.Single(PowerForgeReleaseService.CreateModuleAssetEntries(
                archivePath,
                producedArtifactPaths: new[] { archivePath }));

            Assert.False(entry.IsFinalPackageOutput);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CreateModuleAssetEntries_ConfiguredArchiveWithoutProducerProof_IsNotFinalPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var archivePath = Path.Combine(root, "ExampleModule.v1.0.0.zip");
        try
        {
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                archive.CreateEntry("ExampleModule/ExampleModule.psd1");
                archive.CreateEntry("ExampleModule/ExampleModule.psm1");
            }

            var entry = Assert.Single(PowerForgeReleaseService.CreateModuleAssetEntries(archivePath));

            Assert.False(entry.IsFinalPackageOutput);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveProducedModuleArtifacts_OnlyReturnsNewOrChangedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var unchanged = Path.Combine(root, "unchanged.zip");
        var changed = Path.Combine(root, "changed.zip");
        var added = Path.Combine(root, "added.zip");
        try
        {
            File.WriteAllText(unchanged, "same");
            File.WriteAllText(changed, "before");
            var baseline = PowerForgeReleaseService.CaptureModuleArtifactBaseline(new[] { root });

            File.WriteAllText(changed, "after");
            File.WriteAllText(added, "new");
            var produced = PowerForgeReleaseService.ResolveProducedModuleArtifacts(new[] { root }, baseline);

            Assert.DoesNotContain(unchanged, produced, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(changed, produced, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(added, produced, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
