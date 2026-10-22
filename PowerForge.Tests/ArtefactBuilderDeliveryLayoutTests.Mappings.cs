using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ArtefactBuilderDeliveryLayoutTests
{
    [Theory]
    [InlineData(ArtefactType.Packed, ".")]
    [InlineData(ArtefactType.Unpacked, ".")]
    [InlineData(ArtefactType.Packed, "Application")]
    [InlineData(ArtefactType.Unpacked, "Application")]
    public void Build_DeliveryMappingsSelectRenameAndRelocatePackagedContent(ArtefactType type, string deliveryPath)
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(Path.Combine(fixture.Staging, "Files", "Scripts", "nested"));
        File.WriteAllText(Path.Combine(fixture.Staging, "Files", "Scripts", "nested", "Job.ps1"), "job bytes");
        var segment = fixture.Segment(type);
        segment.Configuration.DeliveryPath = deliveryPath;
        segment.Configuration.DeliveryMappings = new[]
        {
            new ArtefactCopyMapping { Source = "Run.ps1", Destination = "bin/Start.ps1" },
            new ArtefactCopyMapping { Source = "Scripts", Destination = "jobs" }
        };
        string output = fixture.Build(segment).OutputPath;
        string prefix = deliveryPath == "." ? "" : deliveryPath + "/";
        Assert.Equal("launcher", ReadDeliveryEntry(type, output, prefix + "bin/Start.ps1"));
        Assert.Equal("job bytes", ReadDeliveryEntry(type, output, prefix + "jobs/nested/Job.ps1"));
        Assert.Null(ReadDeliveryEntry(type, output, prefix + "config.json"));
        Assert.Null(ReadDeliveryEntry(type, output, prefix + "Run.ps1"));
        Assert.Equal("{}", ReadDeliveryEntry(type, output, "Modules/DemoModule/Files/config.json"));
        Assert.Equal("launcher", ReadDeliveryEntry(type, output, "Modules/DemoModule/Files/Run.ps1"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_EmptyMappingsAndExplicitRootPreserveWholeTreeDefault(bool explicitRoot)
    {
        using var fixture = new Fixture();
        var segment = fixture.Segment(ArtefactType.Unpacked);
        segment.Configuration.DeliveryMappings = explicitRoot
            ? new[] { new ArtefactCopyMapping { Source = ".", Destination = "." } }
            : Array.Empty<ArtefactCopyMapping>();
        string output = fixture.Build(segment).OutputPath;
        Assert.Equal("launcher", File.ReadAllText(Path.Combine(output, "Run.ps1")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(output, "config.json")));
    }

    [Fact]
    public void Build_MappingsAllowDistinctCopiesAndNormalizeEitherSlashStyle()
    {
        using var fixture = new Fixture();
        var segment = fixture.Segment(ArtefactType.Unpacked);
        segment.Configuration.DeliveryMappings = new[]
        {
            new ArtefactCopyMapping { Source = " \"Run.ps1\" ", Destination = "bin\\Start.ps1" },
            new ArtefactCopyMapping { Source = "Run.ps1", Destination = "maintenance/Run.ps1" }
        };
        string output = fixture.Build(segment).OutputPath;
        Assert.Equal("launcher", File.ReadAllText(Path.Combine(output, "bin/Start.ps1")));
        Assert.Equal("launcher", File.ReadAllText(Path.Combine(output, "maintenance/Run.ps1")));
    }

    [Theory]
    [InlineData("../DemoModule.psm1", "out.ps1")]
    [InlineData("/outside", "out.ps1")]
    [InlineData("C:\\outside", "out.ps1")]
    [InlineData("*.ps1", "out.ps1")]
    [InlineData("missing.ps1", "out.ps1")]
    [InlineData("Run.ps1", "../outside.ps1")]
    [InlineData("Run.ps1", "/outside.ps1")]
    [InlineData("Run.ps1", "C:\\outside.ps1")]
    [InlineData("Run.ps1", "*.ps1")]
    [InlineData("Run.ps1", ".")]
    [InlineData("Run.ps1", "bin/")]
    [InlineData("Run.ps1", "Modules/DemoModule/DemoModule.psm1")]
    [InlineData("Run.ps1", "Modules")]
    [InlineData("", "out.ps1")]
    [InlineData("Run.ps1", "")]
    public void Build_InvalidDeliveryMappingLeavesExistingOutputUntouched(string source, string destination)
    {
        using var fixture = new Fixture();
        string marker = Path.Combine(fixture.Output, "keep.txt");
        File.WriteAllText(marker, "existing");
        var segment = fixture.Segment(ArtefactType.Unpacked);
        segment.Configuration.DeliveryMappings = new[] { new ArtefactCopyMapping { Source = source, Destination = destination } };
        Assert.Throws<InvalidOperationException>(() => fixture.Build(segment));
        Assert.Equal("existing", File.ReadAllText(marker));
        Assert.False(Directory.Exists(Path.Combine(fixture.Output, "Modules")));
    }

    [Theory]
    [InlineData("same", "same")]
    [InlineData("same", "same/child")]
    [InlineData("same/child", "same")]
    public void Build_ConflictingDeliveryMappingsFailBeforeOutputChanges(string first, string second)
    {
        using var fixture = new Fixture();
        var segment = fixture.Segment(ArtefactType.Packed);
        segment.Configuration.DeliveryMappings = new[]
        {
            new ArtefactCopyMapping { Source = "Run.ps1", Destination = first },
            new ArtefactCopyMapping { Source = "config.json", Destination = second }
        };
        string marker = Path.Combine(fixture.Output, "keep.txt");
        File.WriteAllText(marker, "existing");
        Assert.Throws<InvalidOperationException>(() => fixture.Build(segment));
        Assert.Equal("existing", File.ReadAllText(marker));
    }

    [Fact]
    public void Build_MappingsCannotPromoteFilesOmittedFromFinalizedPayload()
    {
        using var fixture = new Fixture();
        var segment = fixture.Segment(ArtefactType.Unpacked);
        segment.Configuration.DeliveryMappings = new[] { new ArtefactCopyMapping { Source = "config.json", Destination = "etc/config.json" } };
        var files = Directory.GetFiles(fixture.Staging, "*", SearchOption.AllDirectories)
            .Where(file => !file.EndsWith("config.json", StringComparison.Ordinal)).ToArray();
        Assert.Throws<InvalidOperationException>(() => new ArtefactBuilder(new NullLogger()).BuildWithFinalizer(
            segment, fixture.Root, fixture.Staging, "DemoModule", "1.2.3", null, Array.Empty<RequiredModuleReference>(),
            finalizePackedArtefact: null, information: null, delivery: fixture.Delivery, includeScriptFolders: true, finalizedPayloadFiles: files));
    }

    [Fact]
    public void Build_MappingsRequireDeliveryLayout()
    {
        using var fixture = new Fixture();
        var segment = fixture.Segment(ArtefactType.Unpacked);
        segment.Configuration.Layout = ArtefactLayout.Module;
        segment.Configuration.DeliveryMappings = new[] { new ArtefactCopyMapping { Source = ".", Destination = "." } };
        Assert.Contains("DeliveryMappings requires", Assert.Throws<InvalidOperationException>(() => fixture.Build(segment)).Message);
    }

    [Theory]
    [InlineData("./")]
    [InlineData(".\\")]
    public void Build_RejectsAlternateStagingRootAsDeliveryInternals(string path)
    {
        using var fixture = new Fixture();
        fixture.Delivery.InternalsPath = path;
        Assert.Contains("subdirectory", Assert.Throws<InvalidOperationException>(() => fixture.Build(fixture.Segment(ArtefactType.Unpacked))).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_DisabledDeliveryReportsConfigurationErrorBeforeCopying(bool missing)
    {
        using var fixture = new Fixture();
        var spec = new ModulePipelineSpec
        {
            Build = new ModuleBuildSpec { Name = "DemoModule", SourcePath = fixture.Staging, Version = "1.2.3", KeepStaging = true },
            Install = new ModulePipelineInstallOptions { Enabled = false },
            Segments = new IConfigurationSegment[]
            {
                new ConfigurationOptionsSegment { Options = new ConfigurationOptions { Delivery = missing ? null : new DeliveryOptionsConfiguration { Enable = false } } },
                fixture.Segment(ArtefactType.Unpacked)
            }
        };
        var runner = new ModulePipelineRunner(new NullLogger());
        Assert.Contains("enabled delivery configuration", Assert.Throws<InvalidOperationException>(() => runner.Run(spec, runner.Plan(spec))).Message);
    }

    [Fact]
    public void Build_MappingDestinationCollisionUsesOutputFilesystemCaseRules()
    {
        using var fixture = new Fixture();
        var segment = fixture.Segment(ArtefactType.Unpacked);
        segment.Configuration.DeliveryMappings = new[]
        {
            new ArtefactCopyMapping { Source = "Run.ps1", Destination = "Task.ps1" },
            new ArtefactCopyMapping { Source = "config.json", Destination = "task.ps1" }
        };
        if (OperatingSystem.IsWindows())
            Assert.Throws<InvalidOperationException>(() => fixture.Build(segment));
        else
        {
            string output = fixture.Build(segment).OutputPath;
            Assert.Equal("launcher", File.ReadAllText(Path.Combine(output, "Task.ps1")));
            Assert.Equal("{}", File.ReadAllText(Path.Combine(output, "task.ps1")));
        }
    }

    [Fact]
    public void Run_MappedDestinationsParticipateInCrossArtefactPreflight()
    {
        using var fixture = new Fixture();
        var loose = fixture.Segment(ArtefactType.Unpacked);
        loose.Configuration.DoNotClear = true;
        loose.Configuration.DeliveryMappings = new[] { new ArtefactCopyMapping { Source = "config.json", Destination = "archives/Bundle.zip" } };
        var packed = fixture.Segment(ArtefactType.Packed);
        packed.Configuration.Path = Path.Combine(fixture.Output, "archives");
        packed.Configuration.ArtefactName = "Bundle.zip";
        var spec = new ModulePipelineSpec
        {
            Build = new ModuleBuildSpec { Name = "DemoModule", SourcePath = fixture.Staging, Version = "1.2.3", KeepStaging = true },
            Install = new ModulePipelineInstallOptions { Enabled = false },
            Segments = new IConfigurationSegment[]
            {
                new ConfigurationOptionsSegment { Options = new ConfigurationOptions { Delivery = fixture.Delivery } },
                loose, packed
            }
        };
        string marker = Path.Combine(fixture.Output, "keep.txt");
        File.WriteAllText(marker, "keep");
        var runner = new ModulePipelineRunner(new NullLogger());
        Assert.Contains("unsafe", Assert.Throws<InvalidOperationException>(() => runner.Run(spec, runner.Plan(spec))).Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("keep", File.ReadAllText(marker));
        Assert.False(Directory.Exists(Path.Combine(fixture.Output, "Modules")));
    }

    private static string? ReadDeliveryEntry(ArtefactType type, string output, string relativePath)
    {
        if (type == ArtefactType.Unpacked)
        {
            string path = Path.Combine(output, relativePath);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        using var zip = ZipFile.OpenRead(output);
        var entry = zip.GetEntry(relativePath);
        if (entry is null) return null;
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }
}
