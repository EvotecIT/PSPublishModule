using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ArtefactBuilderCopyMappingTests
{
    [Theory]
    [InlineData(ArtefactType.Unpacked, false)]
    [InlineData(ArtefactType.Unpacked, true)]
    [InlineData(ArtefactType.Packed, false)]
    [InlineData(ArtefactType.Packed, true)]
    [InlineData(ArtefactType.Script, false)]
    [InlineData(ArtefactType.Script, true)]
    [InlineData(ArtefactType.ScriptPacked, false)]
    [InlineData(ArtefactType.ScriptPacked, true)]
    public void Build_LegacyFileMappingsPreserveEmptyDirectoriesAndSelectedFiles(ArtefactType type, bool reverse)
    {
        using var fixture = new Fixture();
        string source = Directory.CreateDirectory(Path.Combine(fixture.Root, "GPO")).FullName;
        string empty = Directory.CreateDirectory(Path.Combine(source, "Machine", "windows nt", "empty")).FullName;
        string policy = Path.Combine(source, "policy.xml");
        File.WriteAllText(policy, "<policy>selected</policy>");
        File.WriteAllText(Path.Combine(source, "excluded.txt"), "must not be copied");
        var segment = fixture.Segment(type);
        segment.Configuration.FilesOutput =
        [
            new() { Source = source, Destination = "backup" },
            new() { Source = empty, Destination = "backup/Machine/windows nt/empty" },
            new() { Source = policy, Destination = "backup/policy.xml" }
        ];
        if (reverse)
            Array.Reverse(segment.Configuration.FilesOutput);

        string output = fixture.BuildAndInspect(segment);

        Assert.Equal("<policy>selected</policy>", File.ReadAllText(Path.Combine(output, "backup", "policy.xml")));
        Assert.True(Directory.Exists(Path.Combine(output, "backup", "Machine", "windows nt", "empty")));
        Assert.False(File.Exists(Path.Combine(output, "backup", "excluded.txt")));
        if (type is ArtefactType.Packed or ArtefactType.ScriptPacked)
        {
            using var archive = ZipFile.OpenRead(Path.Combine(fixture.Output, "bundle.zip"));
            Assert.NotNull(archive.GetEntry("backup/Machine/windows nt/empty/"));
        }
    }

    [Theory]
    [InlineData(ArtefactType.Unpacked)]
    [InlineData(ArtefactType.Packed)]
    public void Build_DirectoryMappingsPreserveTheWholeBackupTree(ArtefactType type)
    {
        using var fixture = new Fixture();
        string source = Directory.CreateDirectory(Path.Combine(fixture.Root, "GPO")).FullName;
        Directory.CreateDirectory(Path.Combine(source, "empty"));
        File.WriteAllText(Path.Combine(source, "policy.xml"), "whole-tree");
        var segment = fixture.Segment(type);
        segment.Configuration.DirectoryOutput = [new() { Source = source, Destination = "backup" }];

        string output = fixture.BuildAndInspect(segment);

        Assert.Equal("whole-tree", File.ReadAllText(Path.Combine(output, "backup", "policy.xml")));
        Assert.True(Directory.Exists(Path.Combine(output, "backup", "empty")));
    }

    [Theory]
    [InlineData(ArtefactType.Unpacked, false)]
    [InlineData(ArtefactType.Unpacked, true)]
    [InlineData(ArtefactType.Packed, false)]
    [InlineData(ArtefactType.Packed, true)]
    public void Build_MissingMappingSourcePreservesExistingOutput(ArtefactType type, bool directory)
    {
        using var fixture = new Fixture();
        var segment = fixture.Segment(type);
        var mapping = new ArtefactCopyMapping { Source = "missing-input", Destination = "backup" };
        if (directory)
            segment.Configuration.DirectoryOutput = [mapping];
        else
            segment.Configuration.FilesOutput = [mapping];

        var error = Assert.Throws<InvalidOperationException>(() => fixture.Build(segment));

        Assert.Contains("copy source", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not exist", error.Message, StringComparison.OrdinalIgnoreCase);
        fixture.AssertPreserved();
    }

    [Theory]
    [InlineData(ArtefactType.Unpacked)]
    [InlineData(ArtefactType.Packed)]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void Build_FileAndDirectoryDestinationConflictPreservesExistingOutput(ArtefactType type)
    {
        using var fixture = new Fixture();
        string source = Directory.CreateDirectory(Path.Combine(fixture.Root, "GPO")).FullName;
        string policy = Path.Combine(source, "policy.xml");
        File.WriteAllText(policy, "selected");
        var segment = fixture.Segment(type);
        segment.Configuration.FilesOutput =
        [
            new() { Source = policy, Destination = "backup" },
            new() { Source = source, Destination = "backup/empty" }
        ];

        var error = Assert.Throws<InvalidOperationException>(() => fixture.Build(segment));

        Assert.Contains("file copy destination", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("directory copy destination", error.Message, StringComparison.OrdinalIgnoreCase);
        fixture.AssertPreserved();
    }

    [Theory]
    [InlineData(ArtefactType.Unpacked)]
    [InlineData(ArtefactType.Packed)]
    public void Build_CopySourceClearedByOutputCleanupIsRejectedBeforeMutation(ArtefactType type)
    {
        using var fixture = new Fixture();
        var segment = fixture.Segment(type);
        segment.Configuration.FilesOutput = [new() { Source = fixture.Marker, Destination = "notice.txt" }];

        var error = Assert.Throws<InvalidOperationException>(() => fixture.Build(segment));

        Assert.Contains("would be removed", error.Message, StringComparison.OrdinalIgnoreCase);
        fixture.AssertPreserved();
    }

    [Theory]
    [InlineData(ArtefactType.Unpacked)]
    [InlineData(ArtefactType.Packed)]
    public void Build_FileSuppliedToDirectoryMappingReportsTheRemedy(ArtefactType type)
    {
        using var fixture = new Fixture();
        string source = Path.Combine(fixture.Root, "policy.xml");
        File.WriteAllText(source, "policy");
        var segment = fixture.Segment(type);
        segment.Configuration.DirectoryOutput = [new() { Source = source, Destination = "backup" }];

        var error = Assert.Throws<InvalidOperationException>(() => fixture.Build(segment));

        Assert.Contains("is a file", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CopyFiles", error.Message, StringComparison.Ordinal);
        Assert.Contains(source, error.Message, StringComparison.Ordinal);
        fixture.AssertPreserved();
    }

    [Theory]
    [InlineData(ArtefactType.Unpacked)]
    [InlineData(ArtefactType.Packed)]
    public void Build_FileInsideDirectoryCopyCannotBecomeAParentDirectory(ArtefactType type)
    {
        using var fixture = new Fixture();
        string source = Directory.CreateDirectory(Path.Combine(fixture.Root, "GPO")).FullName;
        string policy = Path.Combine(source, "policy.xml");
        File.WriteAllText(policy, "policy");
        var segment = fixture.Segment(type);
        segment.Configuration.DirectoryOutput = [new() { Source = source, Destination = "backup" }];
        segment.Configuration.FilesOutput = [new() { Source = policy, Destination = "backup/policy.xml/child.xml" }];

        var error = Assert.Throws<InvalidOperationException>(() => fixture.Build(segment));

        Assert.Contains("file copy destination", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("directory copy destination", error.Message, StringComparison.OrdinalIgnoreCase);
        fixture.AssertPreserved();
    }

    [Theory]
    [InlineData(ArtefactType.Unpacked, false)]
    [InlineData(ArtefactType.Unpacked, true)]
    [InlineData(ArtefactType.Packed, false)]
    [InlineData(ArtefactType.Packed, true)]
    public void Build_MappingKindCannotConflictWithBundledModuleBeforeCleanup(ArtefactType type, bool directory)
    {
        using var fixture = new Fixture();
        string source = Directory.CreateDirectory(Path.Combine(fixture.Root, "GPO")).FullName;
        string policy = Path.Combine(source, "policy.xml");
        File.WriteAllText(policy, "policy");
        var segment = fixture.Segment(type);
        segment.Configuration.FilesOutput =
        [new() { Source = directory ? source : policy, Destination = directory ? "Demo/Demo.psm1" : "Demo" }];

        var error = Assert.Throws<InvalidOperationException>(() => fixture.Build(segment));

        Assert.Contains("file copy destination", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("directory copy destination", error.Message, StringComparison.OrdinalIgnoreCase);
        fixture.AssertPreserved();
    }

    [Theory]
    [InlineData(ArtefactType.Unpacked)]
    [InlineData(ArtefactType.Packed)]
    public void Build_DirectoryEntryCanShareTheModuleDirectory(ArtefactType type)
    {
        using var fixture = new Fixture();
        string source = Directory.CreateDirectory(Path.Combine(fixture.Root, "GPO")).FullName;
        var segment = fixture.Segment(type);
        segment.Configuration.FilesOutput = [new() { Source = source, Destination = "Demo" }];

        string output = fixture.BuildAndInspect(segment);

        Assert.Equal("function Get-Demo { 'module' }", File.ReadAllText(Path.Combine(output, "Demo", "Demo.psm1")));
    }

    [Theory]
    [InlineData(ArtefactType.Unpacked)]
    [InlineData(ArtefactType.Packed)]
    public void Build_RecursiveDirectoryMappingCannotEraseTheBundledModule(ArtefactType type)
    {
        using var fixture = new Fixture();
        string source = Directory.CreateDirectory(Path.Combine(fixture.Root, "GPO")).FullName;
        var segment = fixture.Segment(type);
        segment.Configuration.DirectoryOutput = [new() { Source = source, Destination = "Demo" }];

        var error = Assert.Throws<InvalidOperationException>(() => fixture.Build(segment));

        Assert.Contains("bundled module directory", error.Message, StringComparison.OrdinalIgnoreCase);
        fixture.AssertPreserved();
    }

    [Fact]
    public void Build_UnpackedDirectoryEntryDoesNotClaimRetainedFilesForFinalization()
    {
        using var fixture = new Fixture();
        string source = Directory.CreateDirectory(Path.Combine(fixture.Root, "GPO")).FullName;
        string selected = Path.Combine(source, "selected.ps1");
        File.WriteAllText(selected, "selected");
        string backup = Directory.CreateDirectory(Path.Combine(fixture.Output, "backup")).FullName;
        string retained = Path.Combine(backup, "retained.ps1");
        File.WriteAllText(retained, "caller owned");
        var segment = fixture.Segment(ArtefactType.Unpacked);
        segment.Configuration.DoNotClear = true;
        segment.Configuration.FilesOutput =
        [
            new() { Source = source, Destination = "backup" },
            new() { Source = selected, Destination = "backup/selected.ps1" }
        ];
        bool finalized = false;

        var result = new ArtefactBuilder(new NullLogger()).BuildWithFinalizer(segment,
            fixture.Root, fixture.Staging, "Demo", "1.0.0", null, [], context =>
            {
                finalized = true;
                Assert.Contains(Path.Combine(backup, "selected.ps1"), context.FinalizationFilePaths!);
                Assert.DoesNotContain(retained, context.FinalizationFilePaths!);
                foreach (string file in context.FinalizationFilePaths!)
                    File.AppendAllText(file, " finalized");
                return null;
            });

        Assert.True(finalized);
        Assert.Contains(result.CopiedItems, entry => entry.IsDirectory && entry.Destination == backup);
        Assert.Equal("selected finalized", File.ReadAllText(Path.Combine(backup, "selected.ps1")));
        Assert.Equal("caller owned", File.ReadAllText(retained));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Build_RetainedMappingSourceCannotBeReplacedByBundledModule(bool dependency, bool directory)
    {
        using var fixture = new Fixture();
        string module = dependency ? "Dependency" : "Demo";
        string source = directory
            ? Directory.CreateDirectory(Path.Combine(fixture.Output, module, "custom")).FullName
            : Path.Combine(Directory.CreateDirectory(Path.Combine(fixture.Output, module)).FullName, "custom.txt");
        string content = directory ? Path.Combine(source, "content.txt") : source;
        File.WriteAllText(content, "preserve-source");
        var segment = fixture.Segment(ArtefactType.Unpacked);
        segment.Configuration.DoNotClear = true;
        segment.Configuration.RequiredModules.Enabled = dependency;
        segment.Configuration.FilesOutput = [new() { Source = source, Destination = "backup" }];
        RequiredModuleReference[] dependencies = dependency ? [new("Dependency", "1.0.0")] : [];

        var error = Assert.Throws<InvalidOperationException>(() => new ArtefactBuilder(new NullLogger()).Build(
            segment, fixture.Root, fixture.Staging, "Demo", "1.0.0", null, dependencies));

        Assert.Contains("copy source", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("overlaps", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("preserve-source", File.ReadAllText(content));
        fixture.AssertPreserved();
    }

    [Theory]
    [InlineData("Demo.psm1")]
    [InlineData("Demo.psd1")]
    [InlineData("Demo.ps1")]
    [InlineData("Demo.powerforge-compilation.json")]
    public void Build_RetainedMappingSourceCannotBeOverwrittenByScriptPreparation(string name)
    {
        using var fixture = new Fixture();
        string source = Path.Combine(fixture.Output, name);
        File.WriteAllText(source, "preserve-source");
        var segment = fixture.Segment(ArtefactType.Script);
        segment.Configuration.DoNotClear = true;
        segment.Configuration.FilesOutput = [new() { Source = source, Destination = "backup/source.txt" }];

        var error = Assert.Throws<InvalidOperationException>(() => fixture.Build(segment));

        Assert.Contains("copy source", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("overlaps", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("preserve-source", File.ReadAllText(source));
        fixture.AssertPreserved();
    }

    [Theory]
    [InlineData(ArtefactType.Unpacked, false)]
    [InlineData(ArtefactType.Unpacked, true)]
    [InlineData(ArtefactType.Script, false)]
    [InlineData(ArtefactType.Script, true)]
    public void Build_ExistingFileInDestinationParentIsRejectedBeforeMutation(ArtefactType type, bool external)
    {
        using var fixture = new Fixture();
        string project = Directory.CreateDirectory(Path.Combine(fixture.Root, "project")).FullName;
        string destinationRoot = external
            ? Directory.CreateDirectory(Path.Combine(fixture.Root, "external")).FullName
            : fixture.Output;
        string blocker = Path.Combine(destinationRoot, "blocked");
        File.WriteAllText(blocker, "preserve-blocker");
        string source = Path.Combine(fixture.Root, "source.txt");
        File.WriteAllText(source, "source");
        string previousModuleFile = type == ArtefactType.Script
            ? Path.Combine(fixture.Output, "Demo.ps1")
            : Path.Combine(Directory.CreateDirectory(Path.Combine(fixture.Output, "Demo")).FullName, "previous.txt");
        File.WriteAllText(previousModuleFile, "preserve-module");
        var segment = fixture.Segment(type);
        segment.Configuration.DoNotClear = !external;
        segment.Configuration.FilesOutput = [new() { Source = source, Destination = Path.Combine(blocker, "child.txt") }];
        segment.Configuration.DestinationFilesRelative = false;

        var error = Assert.Throws<InvalidOperationException>(() => new ArtefactBuilder(new NullLogger()).Build(
            segment, project, fixture.Staging, "Demo", "1.0.0", null, []));

        Assert.Contains("required directory", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(blocker, error.Message, StringComparison.Ordinal);
        Assert.Equal("preserve-blocker", File.ReadAllText(blocker));
        Assert.Equal("preserve-module", File.ReadAllText(previousModuleFile));
        fixture.AssertPreserved();
    }

    [Fact]
    public void Build_ParentFileRemovedByEarlierDirectoryReplacementDoesNotBlockMapping()
    {
        using var fixture = new Fixture();
        string source = Directory.CreateDirectory(Path.Combine(fixture.Root, "GPO")).FullName;
        Directory.CreateDirectory(Path.Combine(source, "child"));
        string policy = Path.Combine(source, "policy.xml");
        File.WriteAllText(policy, "policy");
        string backup = Directory.CreateDirectory(Path.Combine(fixture.Output, "backup")).FullName;
        File.WriteAllText(Path.Combine(backup, "child"), "old file removed by the tree copy");
        var segment = fixture.Segment(ArtefactType.Unpacked);
        segment.Configuration.DoNotClear = true;
        segment.Configuration.DirectoryOutput = [new() { Source = source, Destination = "backup" }];
        segment.Configuration.FilesOutput = [new() { Source = policy, Destination = "backup/child/policy.xml" }];

        string output = fixture.BuildAndInspect(segment);

        Assert.Equal("policy", File.ReadAllText(Path.Combine(output, "backup", "child", "policy.xml")));
        fixture.AssertPreserved();
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"))).FullName;
        internal string Staging { get; }
        internal string Output { get; }
        internal string Marker { get; }

        internal Fixture()
        {
            Staging = Directory.CreateDirectory(Path.Combine(Root, "staging")).FullName;
            File.WriteAllText(Path.Combine(Staging, "Demo.psd1"), "@{ RootModule = 'Demo.psm1'; ModuleVersion = '1.0.0' }");
            File.WriteAllText(Path.Combine(Staging, "Demo.psm1"), "function Get-Demo { 'module' }");
            Output = Directory.CreateDirectory(Path.Combine(Root, "output")).FullName;
            Marker = Path.Combine(Output, "previous.txt");
            File.WriteAllText(Marker, "preserve");
        }

        internal ConfigurationArtefactSegment Segment(ArtefactType type) => new()
        {
            ArtefactType = type,
            Configuration = new()
            {
                Enabled = true,
                Path = Output,
                ArtefactName = "bundle.zip",
                DestinationFilesRelative = true,
                DestinationDirectoriesRelative = true
            }
        };

        internal ArtefactBuildResult Build(ConfigurationArtefactSegment segment) =>
            new ArtefactBuilder(new NullLogger()).Build(segment, Root, Staging, "Demo", "1.0.0", null, []);

        internal string BuildAndInspect(ConfigurationArtefactSegment segment)
        {
            var result = Build(segment);
            if (segment.ArtefactType is not (ArtefactType.Packed or ArtefactType.ScriptPacked))
                return result.OutputPath;

            string extracted = Path.Combine(Root, "extracted");
            ZipFile.ExtractToDirectory(result.OutputPath, extracted);
            return extracted;
        }

        internal void AssertPreserved() => Assert.Equal("preserve", File.ReadAllText(Marker));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
