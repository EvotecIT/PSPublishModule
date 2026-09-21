using System;
using System.IO;
using System.Linq;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineHostedOperationsTests
{
    [Fact]
    public void Run_RejectsExternalUnpackedModuleContentChangedAfterValidation()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            string externalModules = Path.Combine(root.FullName, "ExternalModules");
            var hostedOperations = new FakeHostedOperations { ExternalLooseArtifactRootToTamper = externalModules };
            var runner = new ModulePipelineRunner(
                new NullLogger(), new ThrowingPowerShellRunner(), new FakeMetadataProvider(), hostedOperations);
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec { Name = moduleName, SourcePath = root.FullName, Version = "1.0.0" },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments =
                [
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Unpacked,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = Path.Combine(root.FullName, "Artefacts", "Unpacked"),
                            RequiredModules = new ArtefactRequiredModulesConfiguration
                            {
                                ModulesPath = externalModules
                            }
                        }
                    },
                    new ConfigurationActionSegment
                    {
                        Configuration = new ModulePipelineActionConfiguration
                        {
                            Enabled = true,
                            At = ModulePipelineActionStage.AfterArtefacts,
                            Name = "Change external module"
                        }
                    }
                ]
            };

            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));
            Assert.Contains("changed after package validation", failure.Message, StringComparison.Ordinal);
            Assert.Contains(".psm1", failure.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_RejectsLooseArtifactDirectoryModeChangedAfterValidation()
    {
        if (OperatingSystem.IsWindows())
            return;

        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            string artefactRoot = Path.Combine(root.FullName, "Artefacts", "Unpacked");
            var hostedOperations = new FakeHostedOperations { LooseArtifactDirectoryToChangeMode = artefactRoot };
            var runner = new ModulePipelineRunner(
                new NullLogger(), new ThrowingPowerShellRunner(), new FakeMetadataProvider(), hostedOperations);
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec { Name = moduleName, SourcePath = root.FullName, Version = "1.0.0" },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments =
                [
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Unpacked,
                        Configuration = new ArtefactConfiguration { Enabled = true, Path = artefactRoot }
                    },
                    new ConfigurationActionSegment
                    {
                        Configuration = new ModulePipelineActionConfiguration
                        {
                            Enabled = true,
                            At = ModulePipelineActionStage.AfterArtefacts,
                            Name = "Change directory mode"
                        }
                    }
                ]
            };

            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));
            Assert.Contains("changed after package validation", failure.Message, StringComparison.Ordinal);
            Assert.Contains(artefactRoot, failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_ChecksBinaryDependenciesInDeliveredArtifactAndInstallPackage()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var artefactRoot = Path.Combine(root.FullName, "Artefacts", "Unpacked");
            var installRoot = Path.Combine(root.FullName, "InstalledModules");
            var hostedOperations = new FakeHostedOperations { AllowModuleImportValidation = true };
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions
                {
                    Enabled = true,
                    Roots = [installRoot]
                },
                Segments =
                [
                    new ConfigurationImportModulesSegment
                    {
                        ImportModules = new ImportModulesConfiguration { Self = true }
                    },
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Unpacked,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = artefactRoot
                        }
                    }
                ]
            };

            var result = runner.Run(spec);

            Assert.NotNull(result.InstallResult);
            Assert.Contains(hostedOperations.BinaryDependencyRoots, path =>
                path.StartsWith(artefactRoot, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(hostedOperations.BinaryDependencyRoots, path =>
                path.Contains(Path.Combine("PowerForge", "install"), StringComparison.OrdinalIgnoreCase));
            Assert.Contains(hostedOperations.BinaryDependencyRoots, path =>
                path.StartsWith(Path.Combine(installRoot, moduleName), StringComparison.OrdinalIgnoreCase));
            Assert.True(hostedOperations.BinaryDependencyRoots.Count >= 4);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Run_RejectsFilteredBinaryDependencyBeforePackedOrRepositoryDelivery(bool publishToRepository, bool scriptPacked)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var core = Directory.CreateDirectory(Path.Combine(root.FullName, "Lib", "Core"));
            File.WriteAllText(Path.Combine(core.FullName, "Consumer.dll"), "consumer");
            File.WriteAllText(Path.Combine(core.FullName, "Dependency.dll"), "dependency");
            var archivePath = Path.Combine(root.FullName, "Artefacts", "Packed");
            var hostedOperations = new FakeHostedOperations
            {
                AllowModuleImportValidation = true,
                RejectIncompleteBinaryPayload = true,
                PrepareRepositoryPackageOnPublish = publishToRepository
            };
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);

            var segments = new System.Collections.Generic.List<IConfigurationSegment>
            {
                new ConfigurationImportModulesSegment
                {
                    ImportModules = new ImportModulesConfiguration { Self = true }
                },
                new ConfigurationInformationSegment
                {
                    Configuration = new InformationConfiguration
                    {
                        ExcludeFromPackage = ["Dependency.dll"]
                    }
                }
            };
            if (publishToRepository)
            {
                segments.Add(new ConfigurationPublishSegment
                {
                    Configuration = new PublishConfiguration
                    {
                        Enabled = true,
                        Destination = PublishDestination.PowerShellGallery,
                        RepositoryName = "TestRepository"
                    }
                });
            }
            else
            {
                segments.Add(new ConfigurationArtefactSegment
                {
                    ArtefactType = scriptPacked ? ArtefactType.ScriptPacked : ArtefactType.Packed,
                    Configuration = new ArtefactConfiguration
                    {
                        Enabled = true,
                        Path = archivePath
                    }
                });
            }

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = segments.ToArray()
            };

            var failure = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));
            Assert.Contains("Delivered binary dependency is missing", failure.Message, StringComparison.Ordinal);
            Assert.Equal(0, hostedOperations.RemotePublishCalls);
            Assert.Contains(hostedOperations.BinaryDependencyRoots, path =>
                path.Contains(
                    Path.Combine("PowerForge", publishToRepository ? "publish" : "artefacts"),
                    StringComparison.OrdinalIgnoreCase));
            if (scriptPacked)
                Assert.All(hostedOperations.BinaryDependencyManifestsAvailable, Assert.True);
            if (!publishToRepository)
                Assert.False(Directory.Exists(archivePath) && Directory.EnumerateFiles(archivePath, "*.zip").Any());
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_RejectsUnpackedArtifactChangedByAfterArtefactsAction()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var core = Directory.CreateDirectory(Path.Combine(root.FullName, "Lib", "Core"));
            File.WriteAllText(Path.Combine(core.FullName, "Consumer.dll"), "consumer");
            File.WriteAllText(Path.Combine(core.FullName, "Dependency.dll"), "dependency");
            var hostedOperations = new FakeHostedOperations
            {
                AllowModuleImportValidation = true,
                RejectIncompleteBinaryPayload = true,
                RemoveBinaryDependencyAfterArtefacts = true
            };
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec { Name = moduleName, SourcePath = root.FullName, Version = "1.0.0" },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments =
                [
                    new ConfigurationImportModulesSegment
                    {
                        ImportModules = new ImportModulesConfiguration { Self = true }
                    },
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Unpacked,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = Path.Combine(root.FullName, "Artefacts", "Unpacked")
                        }
                    },
                    new ConfigurationActionSegment
                    {
                        Configuration = new ModulePipelineActionConfiguration
                        {
                            Enabled = true,
                            At = ModulePipelineActionStage.AfterArtefacts,
                            Name = "Remove dependency"
                        }
                    }
                ]
            };

            var failure = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));
            Assert.Contains("changed after package validation", failure.Message, StringComparison.Ordinal);
            Assert.Single(hostedOperations.ActionContexts);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_UnsignedAutoRevisionRollsBackInstalledPayloadWhenDependencyDisappears()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var core = Directory.CreateDirectory(Path.Combine(root.FullName, "Lib", "Core"));
            File.WriteAllText(Path.Combine(core.FullName, "Consumer.dll"), "consumer");
            File.WriteAllText(Path.Combine(core.FullName, "Dependency.dll"), "dependency");
            var installRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "InstalledModules"));
            var previous = Directory.CreateDirectory(Path.Combine(installRoot.FullName, moduleName, "0.9.0"));
            File.WriteAllText(Path.Combine(previous.FullName, "keep.txt"), "usable previous version");
            var hostedOperations = new FakeHostedOperations
            {
                AllowModuleImportValidation = true,
                RejectIncompleteBinaryPayload = true,
                CorruptInstalledBinaryUnder = Path.Combine(installRoot.FullName, moduleName)
            };
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec { Name = moduleName, SourcePath = root.FullName, Version = "1.0.0" },
                Install = new ModulePipelineInstallOptions
                {
                    Enabled = true,
                    Strategy = InstallationStrategy.AutoRevision,
                    Roots = [installRoot.FullName]
                },
                Segments =
                [
                    new ConfigurationImportModulesSegment
                    {
                        ImportModules = new ImportModulesConfiguration { Self = true }
                    }
                ]
            };

            var failure = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));
            Assert.Contains("Delivered binary dependency is missing", failure.Message, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(previous.FullName, "keep.txt")));
            Assert.Equal(new[] { "0.9.0" }, Directory.EnumerateDirectories(
                Path.Combine(installRoot.FullName, moduleName)).Select(Path.GetFileName).ToArray());
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_RejectsUnsignedPackedArtifactChangedAfterValidation()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var hostedOperations = new FakeHostedOperations
            {
                TamperPackedArtifactAfterArtefacts = true
            };
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec { Name = moduleName, SourcePath = root.FullName, Version = "1.0.0" },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments =
                [
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Packed,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = Path.Combine(root.FullName, "Artefacts", "Packed")
                        }
                    },
                    new ConfigurationActionSegment
                    {
                        Configuration = new ModulePipelineActionConfiguration
                        {
                            Enabled = true,
                            At = ModulePipelineActionStage.AfterArtefacts,
                            Name = "Change packed artifact"
                        }
                    }
                ]
            };

            var failure = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));
            Assert.Contains("changed after package validation", failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData(ArtefactType.Unpacked, ".psm1")]
    [InlineData(ArtefactType.Script, ".ps1")]
    public void Run_RejectsLooseArtifactContentChangedAfterValidation(ArtefactType artifactType, string extension)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var hostedOperations = new FakeHostedOperations
            {
                LooseArtifactExtensionToTamper = extension
            };
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec { Name = moduleName, SourcePath = root.FullName, Version = "1.0.0" },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments =
                [
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = artifactType,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = Path.Combine(root.FullName, "Artefacts", artifactType.ToString())
                        }
                    },
                    new ConfigurationActionSegment
                    {
                        Configuration = new ModulePipelineActionConfiguration
                        {
                            Enabled = true,
                            At = ModulePipelineActionStage.AfterArtefacts,
                            Name = "Change loose artifact"
                        }
                    }
                ]
            };

            var failure = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));
            Assert.Contains("changed after package validation", failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
