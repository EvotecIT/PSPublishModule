using System.IO.Compression;

namespace PowerForge.Tests;

public sealed partial class ArtefactBuilderDeliveryLayoutTests
{
    [Theory]
    [InlineData(ArtefactType.Packed, ArtefactLayout.Module)]
    [InlineData(ArtefactType.Unpacked, ArtefactLayout.Module)]
    [InlineData(ArtefactType.Packed, ArtefactLayout.Delivery)]
    [InlineData(ArtefactType.Unpacked, ArtefactLayout.Delivery)]
    public void Build_FinalizesCompleteLayoutAfterExtraCopies(ArtefactType type, ArtefactLayout layout)
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "Support"));
        File.WriteAllText(Path.Combine(fixture.Root, "Support", "Bootstrap.ps1"), "runtime");
        var segment = fixture.Segment(type);
        segment.Configuration.Layout = layout;
        segment.Configuration.DestinationFilesRelative = true;
        segment.Configuration.FilesOutput =
        [
            new ArtefactCopyMapping
            {
                Source = Path.Combine(fixture.Root, "Support", "Bootstrap.ps1"),
                Destination = "Diagnostics/Bootstrap.ps1"
            }
        ];
        int finalized = 0;

        var result = new ArtefactBuilder(new NullLogger()).BuildWithFinalizer(segment,
            fixture.Root, fixture.Staging, "DemoModule", "1.2.3", null,
            Array.Empty<RequiredModuleReference>(), finalizePackedArtefact: context =>
            {
                finalized++;
                Assert.Equal(type, context.ArtefactType);
                Assert.True(File.Exists(context.ManifestPath));
                string helper = Path.Combine(context.RootPath, "Diagnostics", "Bootstrap.ps1");
                Assert.Equal("runtime", File.ReadAllText(helper));
                File.WriteAllText(helper, "finalized runtime");
                return null;
            }, delivery: fixture.Delivery);

        Assert.Equal(1, finalized);
        if (type == ArtefactType.Packed)
        {
            using var zip = ZipFile.OpenRead(result.OutputPath);
            using var reader = new StreamReader(zip.GetEntry("Diagnostics/Bootstrap.ps1")!.Open());
            Assert.Equal("finalized runtime", reader.ReadToEnd());
        }
        else
        {
            Assert.Equal("finalized runtime", File.ReadAllText(Path.Combine(result.OutputPath, "Diagnostics", "Bootstrap.ps1")));
        }
    }

    [Fact]
    public void Build_UnpackedFinalizationScopesCopiedFilesInRetainedDirectory()
    {
        using var fixture = new Fixture();
        string source = Path.Combine(fixture.Root, "Support");
        string destination = Path.Combine(fixture.Output, "Diagnostics");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(source, "Bootstrap.ps1"), "runtime");
        File.WriteAllText(Path.Combine(destination, "Bootstrap.ps1"), "stale");
        string retained = Path.Combine(fixture.Output, "Caller.ps1");
        File.WriteAllText(retained, "caller owned");
        var segment = fixture.Segment(ArtefactType.Unpacked);
        segment.Configuration.DoNotClear = true;
        segment.Configuration.DestinationDirectoriesRelative = true;
        segment.Configuration.DirectoryOutput =
        [
            new ArtefactCopyMapping { Source = source, Destination = "Diagnostics" }
        ];

        _ = new ArtefactBuilder(new NullLogger()).BuildWithFinalizer(segment,
            fixture.Root, fixture.Staging, "DemoModule", "1.2.3", null,
            Array.Empty<RequiredModuleReference>(), finalizePackedArtefact: context =>
            {
                string copied = Path.Combine(destination, "Bootstrap.ps1");
                Assert.Contains(copied, context.FinalizationFilePaths!);
                Assert.DoesNotContain(retained, context.FinalizationFilePaths!);
                foreach (string file in context.FinalizationFilePaths!.Where(path => path.EndsWith(".ps1", StringComparison.Ordinal)))
                    File.AppendAllText(file, " finalized");
                return null;
            }, delivery: fixture.Delivery);

        Assert.Equal("runtime finalized", File.ReadAllText(Path.Combine(destination, "Bootstrap.ps1")));
        Assert.Equal("caller owned", File.ReadAllText(retained));
    }
}
