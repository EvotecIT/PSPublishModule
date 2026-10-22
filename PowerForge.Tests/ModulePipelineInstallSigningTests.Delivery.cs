using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineInstallSigningTests
{
    [Theory]
    [InlineData(ArtefactType.Packed, false, false, null)]
    [InlineData(ArtefactType.Packed, true, false, null)]
    [InlineData(ArtefactType.Packed, false, true, null)]
    [InlineData(ArtefactType.Unpacked, false, false, null)]
    [InlineData(ArtefactType.Unpacked, true, false, null)]
    [InlineData(ArtefactType.Unpacked, false, true, null)]
    [InlineData(ArtefactType.Packed, false, false, "Launchers")]
    [InlineData(ArtefactType.Packed, true, false, "Launchers")]
    [InlineData(ArtefactType.Unpacked, false, false, "Launchers")]
    [InlineData(ArtefactType.Unpacked, true, false, "Launchers")]
    [InlineData(ArtefactType.Packed, false, false, "Mapped")]
    [InlineData(ArtefactType.Packed, true, false, "Mapped")]
    [InlineData(ArtefactType.Unpacked, false, false, "Mapped")]
    [InlineData(ArtefactType.Unpacked, true, false, "Mapped")]
    public void SignedDeliveryArtefacts_PreserveDeliverySigningPreference(ArtefactType type, bool deliverySign, bool includeInternals, string? deliveryPath)
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "source");
            Directory.CreateDirectory(Path.Combine(source, "Files"));
            WriteMinimalModule(source, "DemoModule", "1.0.0");
            File.WriteAllText(Path.Combine(source, "Files", "Run.ps1"), "'launcher'");
            var spec = CreateSpec(source, "DemoModule", Path.Combine(root, "install"));
            spec.Install.Enabled = false;
            var options = spec.Segments.OfType<ConfigurationOptionsSegment>().Single().Options;
            options.Signing!.IncludeInternals = includeInternals;
            options.Delivery = new DeliveryOptionsConfiguration { Enable = true, Sign = deliverySign, InternalsPath = "Files" };
            bool mapped = deliveryPath == "Mapped";
            string launcherName = mapped ? "bin/Start.ps1" : "Run.ps1";
            spec.Segments = spec.Segments.Concat(new IConfigurationSegment[]
            {
                new ConfigurationArtefactSegment
                {
                    ArtefactType = type,
                    Configuration = new ArtefactConfiguration
                    {
                        Enabled = true, Layout = ArtefactLayout.Delivery, DeliveryPath = deliveryPath, Path = Path.Combine(root, "packed"),
                        DeliveryMappings = mapped ? new[] { new ArtefactCopyMapping { Source = "Run.ps1", Destination = launcherName } } : null
                    }
                }
            }).ToArray();
            var hosted = new RecordingHostedOperations { HonorSigningPatterns = true, MarkSignedFiles = true };
            var runner = new ModulePipelineRunner(new NullLogger(), powerShellRunner: null, moduleDependencyMetadataProvider: null, hostedOperations: hosted);
            var result = runner.Run(spec, runner.Plan(spec));
            string output = Assert.Single(result.ArtefactResults).OutputPath;
            string launcher;
            if (type == ArtefactType.Packed)
            {
                using var zip = ZipFile.OpenRead(output);
                using var reader = new StreamReader(Assert.IsType<ZipArchiveEntry>(zip.GetEntry((deliveryPath is null ? "" : deliveryPath + "/") + launcherName)).Open());
                launcher = reader.ReadToEnd();
            }
            else
                launcher = File.ReadAllText(Path.Combine(output, deliveryPath ?? ".", launcherName));
            Assert.Equal(deliverySign || includeInternals, launcher.Contains("# synthetic delivery signature", StringComparison.Ordinal));
            if (!deliverySign && !includeInternals)
                Assert.Equal("'launcher'", launcher);
            Assert.Contains(hosted.SignedFilePaths, path => path.EndsWith("DemoModule.psm1", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
