using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineUnifiedReleaseTests
{
    [Fact]
    public void Run_RejectsPackedArtefactsThatResolveToTheSameArchiveBeforePackaging()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            string outputRoot = Path.Combine(root.FullName, "Artifacts", "Shared");
            string archivePath = Path.Combine(outputRoot, moduleName + ".zip");
            var runner = new ModulePipelineRunner(new NullLogger());
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments =
                [
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.ScriptPacked,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = outputRoot,
                            ArtefactName = moduleName + ".zip"
                        }
                    },
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Packed,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = outputRoot,
                            ArtefactName = moduleName + ".zip"
                        }
                    }
                ]
            };

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));

            Assert.Contains("same zip output file", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("unique Path or ArtefactName", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(archivePath));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_AllowsCaseDistinctPackedArchiveNamesOnCaseSensitiveFileSystem()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            if (FrameworkCompatibility.GetPathStringComparisonForPath(root.FullName) != StringComparison.Ordinal)
                return;

            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            string outputRoot = Path.Combine(root.FullName, "Artifacts", "Shared");
            var runner = new ModulePipelineRunner(new NullLogger());
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments =
                [
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Packed,
                        Configuration = new ArtefactConfiguration
                        {
                            ID = "LowercaseArchive",
                            Enabled = true,
                            Path = outputRoot,
                            ArtefactName = "testmodule.zip"
                        }
                    },
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Packed,
                        Configuration = new ArtefactConfiguration
                        {
                            ID = "UppercaseArchive",
                            Enabled = true,
                            Path = outputRoot,
                            ArtefactName = "TESTMODULE.zip"
                        }
                    }
                ]
            };

            runner.Run(spec);

            Assert.True(File.Exists(Path.Combine(outputRoot, "testmodule.zip")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "TESTMODULE.zip")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.Unpacked)]
    public void Run_RejectsSplitDirectoryLayoutBeforeBindingCoordinatedReleasePayload(
        ArtefactType artefactType)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var stagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        var externalScriptRoot = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.SplitApp", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            const string synchronizedVersion = "2.0.11";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            WriteSynchronizedProjectBuildConfig(root.FullName, "project.build.json", moduleName, publishNuGet: false);
            var hosted = new FakeHostedOperations(new List<string>())
            {
                ModulePublishAction = (_, _) => throw new InvalidOperationException("Publishing should not start.")
            };
            var runner = CreateRunner(
                hosted,
                (request, configuration, configPath) => CreateProjectBuildResult(
                    root.FullName,
                    moduleName,
                    synchronizedVersion,
                    Path.Combine(root.FullName, "PackageOutput", "NuGet"),
                    request,
                    configPath,
                    includePackage: false));
            ModulePipelineSpec spec = CreateGalleryReleaseSpec(root.FullName, stagingPath, moduleName);
            string scriptOutputRoot = Path.Combine(root.FullName, "Artifacts", "Script");
            spec.Segments = spec.Segments.Take(1)
                .Concat(
                [
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = artefactType,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = scriptOutputRoot,
                            RequiredModules = new ArtefactRequiredModulesConfiguration
                            {
                                ModulesPath = externalScriptRoot
                            }
                        }
                    }
                ])
                .Concat(spec.Segments.Skip(1))
                .ToArray();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));

            Assert.Contains($"coordinated release artefact '{artefactType}'", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("split layout", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("outside its cached output root", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(hosted.PublishedModuleVersions);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, recursive: true); } catch { }
            try { if (Directory.Exists(externalScriptRoot)) Directory.Delete(externalScriptRoot, recursive: true); } catch { }
        }
    }
}
