using System;
using System.IO;
using System.Linq;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineHostedOperationsTests
{
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
    [InlineData(false)]
    [InlineData(true)]
    public void Run_RejectsFilteredBinaryDependencyBeforePackedOrRepositoryDelivery(bool publishToRepository)
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
                    ArtefactType = ArtefactType.Packed,
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
            if (!publishToRepository)
                Assert.False(Directory.Exists(archivePath) && Directory.EnumerateFiles(archivePath, "*.zip").Any());
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
