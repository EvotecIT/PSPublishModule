using System.IO.Compression;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void ResolveModuleArtefactOutputs_CarriesConfiguredProducingTypes()
    {
        string root = CreateSandbox();
        try
        {
            var context = new ModulePipelineConfigurationContext
            {
                ProjectRoot = root,
                Spec = new ModulePipelineSpec
                {
                    Build = new ModuleBuildSpec { Name = "Company.Tools", SourcePath = root, Version = "4.0.0" },
                    Segments = new IConfigurationSegment[]
                    {
                        new ConfigurationArtefactSegment
                        {
                            ArtefactType = ArtefactType.Packed,
                            Configuration = new ArtefactConfiguration
                            {
                                Enabled = true,
                                Path = Path.Combine(root, "packed")
                            }
                        },
                        new ConfigurationArtefactSegment
                        {
                            ArtefactType = ArtefactType.ScriptPacked,
                            Configuration = new ArtefactConfiguration
                            {
                                Enabled = true,
                                Path = Path.Combine(root, "scripts")
                            }
                        }
                    }
                }
            };

            PowerForgeModuleArtefactOutputSummary[] outputs =
                PowerForgeReleaseService.ResolveModuleArtefactOutputs(context);

            Assert.Collection(
                outputs,
                output =>
                {
                    Assert.Equal(ArtefactType.Packed, output.Type);
                    Assert.Equal(Path.Combine(root, "packed"), output.OutputRoot);
                },
                output =>
                {
                    Assert.Equal(ArtefactType.ScriptPacked, output.Type);
                    Assert.Equal(Path.Combine(root, "scripts"), output.OutputRoot);
                });
        }
        finally
        {
            TryDelete(root);
        }
    }

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
                        ModuleVersion = "4.0.0",
                        ArtefactOutputs = new[]
                        {
                            new PowerForgeModuleArtefactOutputSummary
                            {
                                Type = ArtefactType.ScriptPacked,
                                OutputRoot = root
                            }
                        }
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
                        ModuleVersion = "4.0.0",
                        ArtefactOutputs = new[]
                        {
                            new PowerForgeModuleArtefactOutputSummary
                            {
                                Type = ArtefactType.ScriptPacked,
                                OutputRoot = root
                            }
                        }
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

    [Theory]
    [InlineData("app/Company.Tools.ps1", null)]
    [InlineData("Company.Tools.ps1", "Invoke-Helper.ps1")]
    public void CreateModuleAssetEntries_RecognizesSupportedManifestlessScriptPackedLayouts(
        string entryPoint,
        string? additionalScript)
    {
        string root = CreateSandbox();
        try
        {
            string scriptPackedPath = Path.Combine(root, "Company.Tools.zip");
            using (ZipArchive archive = ZipFile.Open(scriptPackedPath, ZipArchiveMode.Create))
            {
                ZipArchiveEntry script = archive.CreateEntry(entryPoint);
                using (var writer = new StreamWriter(script.Open()))
                    writer.Write("Get-Date");
                if (!string.IsNullOrWhiteSpace(additionalScript))
                {
                    ZipArchiveEntry helper = archive.CreateEntry(additionalScript);
                    using var writer = new StreamWriter(helper.Open());
                    writer.Write("Get-ChildItem");
                }
            }

            PowerForgeReleaseAssetEntry entry = Assert.Single(
                PowerForgeReleaseService.CreateModuleAssetEntries(
                    root,
                    new PowerForgeModuleReleasePlanSummary
                    {
                        ManifestPath = Path.Combine(root, "Company.Tools.psd1"),
                        ModuleName = "Company.Tools",
                        ModuleVersion = "4.0.0",
                        ArtefactOutputs = new[]
                        {
                            new PowerForgeModuleArtefactOutputSummary
                            {
                                Type = ArtefactType.ScriptPacked,
                                OutputRoot = root
                            }
                        }
                    },
                    new[] { scriptPackedPath }));

            Assert.Equal(scriptPackedPath, entry.Path);
            Assert.True(entry.IsFinalPackageOutput);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void CreateModuleAssetEntries_DoesNotReclassifyMalformedPackedArchiveAsScriptPacked()
    {
        string root = CreateSandbox();
        try
        {
            string packedPath = Path.Combine(root, "Company.Tools.zip");
            using (ZipArchive archive = ZipFile.Open(packedPath, ZipArchiveMode.Create))
            {
                ZipArchiveEntry helper = archive.CreateEntry("Invoke-Helper.ps1");
                using var writer = new StreamWriter(helper.Open());
                writer.Write("Get-Date");
            }

            PowerForgeReleaseAssetEntry entry = Assert.Single(
                PowerForgeReleaseService.CreateModuleAssetEntries(
                    packedPath,
                    new PowerForgeModuleReleasePlanSummary
                    {
                        ModuleName = "Company.Tools",
                        ModuleVersion = "4.0.0",
                        ArtefactOutputs = new[]
                        {
                            new PowerForgeModuleArtefactOutputSummary
                            {
                                Type = ArtefactType.Packed,
                                OutputRoot = root
                            }
                        }
                    },
                    new[] { packedPath }));

            Assert.False(entry.IsFinalPackageOutput);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void CreateModuleAssetEntries_UsesExactLegacyBuildArtefactOutputType()
    {
        string root = CreateSandbox();
        try
        {
            string scriptPackedPath = Path.Combine(root, "Company.Tools.zip");
            using (ZipArchive archive = ZipFile.Open(scriptPackedPath, ZipArchiveMode.Create))
            {
                ZipArchiveEntry script = archive.CreateEntry("Company.Tools.ps1");
                using var writer = new StreamWriter(script.Open());
                writer.Write("Get-Date");
            }

            PowerForgeReleaseAssetEntry entry = Assert.Single(
                PowerForgeReleaseService.CreateModuleAssetEntries(
                    scriptPackedPath,
                    new PowerForgeModuleReleasePlanSummary
                    {
                        ScriptPath = Path.Combine(root, "Build-Module.ps1"),
                        ModuleName = "Company.Tools",
                        ModuleVersion = "4.0.0",
                        ArtefactOutputs = new[]
                        {
                            new PowerForgeModuleArtefactOutputSummary
                            {
                                Type = ArtefactType.ScriptPacked,
                                OutputPath = scriptPackedPath
                            }
                        }
                    },
                    new[] { scriptPackedPath }));

            Assert.True(entry.IsFinalPackageOutput);
        }
        finally
        {
            TryDelete(root);
        }
    }
}
