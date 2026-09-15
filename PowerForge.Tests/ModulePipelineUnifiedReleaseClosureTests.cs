using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineUnifiedReleaseTests
{
    [Theory]
    [InlineData(ArtefactType.Script, ArtefactType.Script)]
    [InlineData(ArtefactType.Script, ArtefactType.Unpacked)]
    [InlineData(ArtefactType.Unpacked, ArtefactType.Unpacked)]
    public void Run_RejectsOverlappingDirectoryArtefactOutputsBeforePackaging(
        ArtefactType firstType,
        ArtefactType secondType)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            string outputRoot = Path.Combine(root.FullName, "Artifacts", "Shared");
            var runner = new ModulePipelineRunner(new NullLogger());
            ModulePipelineSpec spec = CreateArtefactCollisionSpec(
                root.FullName,
                moduleName,
                CreateArtefact(firstType, outputRoot, "first"),
                CreateArtefact(secondType, outputRoot, "second"));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));

            Assert.Contains("overlapping destructive outputs", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(outputRoot));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.Unpacked)]
    public void Run_RejectsPackedArchiveNestedInDirectoryArtefactOutputBeforePackaging(
        ArtefactType directoryType)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            string directoryRoot = Path.Combine(root.FullName, "Artifacts", "Shared");
            string packedRoot = Path.Combine(directoryRoot, "Packages");
            var runner = new ModulePipelineRunner(new NullLogger());
            ModulePipelineSpec spec = CreateArtefactCollisionSpec(
                root.FullName,
                moduleName,
                CreateArtefact(directoryType, directoryRoot, "directory"),
                CreateArtefact(ArtefactType.Packed, packedRoot, "packed"));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));

            Assert.Contains("overlapping destructive outputs", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(packedRoot, moduleName + ".zip")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_RejectsPackedCleanupThatDeletesAnotherArtefactDirectFileOutput()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            string sharedRoot = Path.Combine(root.FullName, "Artifacts", "Shared");
            string metadataPath = Path.Combine(sharedRoot, "release-info.json");
            string sourcePath = Path.Combine(root.FullName, "release-info.json");
            File.WriteAllText(sourcePath, "{}");
            ConfigurationArtefactSegment unpacked = CreateArtefact(
                ArtefactType.Unpacked,
                Path.Combine(root.FullName, "Artifacts", "Unpacked"),
                "unpacked");
            unpacked.Configuration!.DoNotClear = true;
            unpacked.Configuration.FilesOutput = new[]
            {
                new ArtefactCopyMapping { Source = sourcePath, Destination = metadataPath }
            };
            var runner = new ModulePipelineRunner(new NullLogger());
            ModulePipelineSpec spec = CreateArtefactCollisionSpec(
                root.FullName,
                moduleName,
                CreateArtefact(ArtefactType.Packed, sharedRoot, "packed"),
                unpacked);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));

            Assert.Contains("direct output files", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("file copy mapping output", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(metadataPath));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_AllowsDistinctPackedArchivesInTheSameOutputDirectory()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            string outputRoot = Path.Combine(root.FullName, "Artifacts", "Shared");
            ConfigurationArtefactSegment first = CreateArtefact(ArtefactType.Packed, outputRoot, "first");
            first.Configuration!.ArtefactName = "module.zip";
            ConfigurationArtefactSegment second = CreateArtefact(ArtefactType.ScriptPacked, outputRoot, "second");
            second.Configuration!.ArtefactName = "script.zip";
            var runner = new ModulePipelineRunner(new NullLogger());
            ModulePipelineSpec spec = CreateArtefactCollisionSpec(root.FullName, moduleName, first, second);

            runner.Run(spec);

            Assert.True(File.Exists(Path.Combine(outputRoot, "module.zip")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "script.zip")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    private static ModulePipelineSpec CreateArtefactCollisionSpec(
        string sourcePath,
        string moduleName,
        params ConfigurationArtefactSegment[] artefacts)
        => new()
        {
            Build = new ModuleBuildSpec
            {
                Name = moduleName,
                SourcePath = sourcePath,
                Version = "1.0.0"
            },
            Install = new ModulePipelineInstallOptions { Enabled = false },
            Segments = artefacts
        };

    private static ConfigurationArtefactSegment CreateArtefact(
        ArtefactType type,
        string path,
        string id)
        => new()
        {
            ArtefactType = type,
            Configuration = new ArtefactConfiguration
            {
                ID = id,
                Enabled = true,
                Path = path
            }
        };
}
