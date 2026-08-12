using System.IO.Compression;

namespace PowerForge.Tests;

public sealed class PowerForgeReleaseModulePackageProvenanceTests
{
    [Fact]
    public void CreateDotNetArtefactEntries_ExecutableOnlyPublish_IsVerifiedFinalPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var executablePath = Path.Combine(root, "Example.exe");
        try
        {
            File.WriteAllText(executablePath, "signed executable");
            var entry = Assert.Single(PowerForgeReleaseService.CreateDotNetArtefactEntries(
                new DotNetPublishArtefactResult
                {
                    Target = "Example",
                    ExePath = executablePath,
                    Runtime = "win-x64",
                    Framework = "net10.0"
                },
                new DotNetPublishPlan
                {
                    Targets = [new DotNetPublishTargetPlan { Name = "Example", Version = "2.3.4" }]
                },
                sharedReleaseVersion: null));

            Assert.Equal(executablePath, entry.Path);
            Assert.Equal("2.3.4", entry.Version);
            Assert.True(entry.IsFinalPackageOutput);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CreateDotNetStorePackageEntries_UsesMatchingTargetVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var packagePath = Path.Combine(root, "Example.msixupload");
        try
        {
            File.WriteAllText(packagePath, "signed store package");
            var entry = Assert.Single(PowerForgeReleaseService.CreateDotNetStorePackageEntries(
                new DotNetPublishStorePackageResult
                {
                    Target = "Example",
                    OutputFiles = [packagePath]
                },
                new DotNetPublishPlan
                {
                    Targets = [new DotNetPublishTargetPlan { Name = "Example", Version = "2.3.4" }]
                },
                sharedReleaseVersion: null));

            Assert.Equal("2.3.4", entry.Version);
            Assert.True(entry.IsFinalPackageOutput);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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
                new PowerForgeModuleReleasePlanSummary { ModuleVersion = "1.2.3" },
                producedArtifactPaths: new[] { archivePath }));

            Assert.True(entry.IsFinalPackageOutput);
            Assert.Equal("1.2.3", entry.Version);
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
    public void CreateModuleAssetEntries_PackedModuleWithSiblingDependencies_IsVerifiedFinalPackage()
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
                archive.CreateEntry("ExampleModule/Sources/Internal.cs");
                archive.CreateEntry("BundledDependency/BundledDependency.psd1");
                archive.CreateEntry("BundledDependency/BundledDependency.psm1");
            }

            var entry = Assert.Single(PowerForgeReleaseService.CreateModuleAssetEntries(
                archivePath,
                new PowerForgeModuleReleasePlanSummary
                {
                    ManifestPath = Path.Combine(root, "ExampleModule.psd1")
                },
                new[] { archivePath }));

            Assert.True(entry.IsFinalPackageOutput);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CreateModuleAssetEntries_PackedModuleUnderConfiguredRoot_IsVerifiedFinalPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var archivePath = Path.Combine(root, "ExampleModule.v1.0.0.zip");
        try
        {
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                archive.CreateEntry("Modules/ExampleModule/ExampleModule.psd1");
                archive.CreateEntry("Modules/ExampleModule/ExampleModule.psm1");
            }

            var entry = Assert.Single(PowerForgeReleaseService.CreateModuleAssetEntries(
                archivePath,
                new PowerForgeModuleReleasePlanSummary
                {
                    ModuleName = "ExampleModule",
                    ManifestPath = Path.Combine(root, "ExampleModule.psd1"),
                    PackedModuleRoots = ["Modules"]
                },
                new[] { archivePath }));

            Assert.True(entry.IsFinalPackageOutput);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolvePackedModuleRoots_UsesEnabledPackedArtefactConfiguration()
    {
        var roots = PowerForgeReleaseService.ResolvePackedModuleRoots(
            new ModulePipelineConfigurationContext
            {
                ProjectRoot = Path.GetTempPath(),
                Spec = new ModulePipelineSpec
                {
                    Segments =
                    [
                        new ConfigurationArtefactSegment
                        {
                            ArtefactType = ArtefactType.Packed,
                            Configuration = new ArtefactConfiguration
                            {
                                Enabled = true,
                                RequiredModules = new ArtefactRequiredModulesConfiguration
                                {
                                    ModulesPath = "Payload/Modules"
                                }
                            }
                        }
                    ]
                }
            },
            "ExampleModule",
            "1.2.3",
            preRelease: null);

        Assert.Equal(new[] { "Payload/Modules" }, roots);
    }

    [Fact]
    public void ResolveProducedModuleArtifacts_OnlyReturnsNewOrChangedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var unchanged = Path.Combine(root, "unchanged.zip");
        var changed = Path.Combine(root, "changed.zip");
        var deterministicallyRebuilt = Path.Combine(root, "deterministic.zip");
        var added = Path.Combine(root, "added.zip");
        try
        {
            File.WriteAllText(unchanged, "same");
            File.WriteAllText(changed, "before");
            File.WriteAllText(deterministicallyRebuilt, "same payload");
            var rebuiltWriteTime = File.GetLastWriteTimeUtc(deterministicallyRebuilt);
            var baseline = PowerForgeReleaseService.CaptureModuleArtifactBaseline(new[] { root });

            File.WriteAllText(changed, "after");
            File.WriteAllText(deterministicallyRebuilt, "same payload");
            File.SetLastWriteTimeUtc(deterministicallyRebuilt, rebuiltWriteTime.AddMinutes(1));
            File.WriteAllText(added, "new");
            var produced = PowerForgeReleaseService.ResolveProducedModuleArtifacts(new[] { root }, baseline);

            Assert.DoesNotContain(unchanged, produced, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(changed, produced, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(deterministicallyRebuilt, produced, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(added, produced, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
