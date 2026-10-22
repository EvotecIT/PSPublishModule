using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace PowerForge.Tests;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ArtefactBuilderDeliveryLayoutTests
{
    [Theory]
    [InlineData(ArtefactType.Packed)]
    [InlineData(ArtefactType.Unpacked)]
    public void Build_DeliveryLayoutBundlesRemainingInstalledDependenciesUnderModules(ArtefactType type)
    {
        using var fixture = new Fixture();
        string installed = Path.Combine(fixture.Root, "installed");
        string dependency = Path.Combine(installed, "RuntimeDependency", "1.0.0");
        Directory.CreateDirectory(dependency);
        File.WriteAllText(Path.Combine(dependency, "RuntimeDependency.psd1"), "@{RootModule='RuntimeDependency.psm1';ModuleVersion='1.0.0'}");
        File.WriteAllText(Path.Combine(dependency, "RuntimeDependency.psm1"), "function Get-RuntimeThing { 'runtime' }");
        string? previous = Environment.GetEnvironmentVariable("PSModulePath");
        try
        {
            Environment.SetEnvironmentVariable("PSModulePath", installed + Path.PathSeparator + previous);
            var segment = fixture.Segment(type);
            segment.Configuration.RequiredModules.Enabled = true;
            segment.Configuration.RequiredModules.Source = RequiredModulesSource.Installed;
            var result = new ArtefactBuilder(new NullLogger()).Build(segment, fixture.Root, fixture.Staging,
                "DemoModule", "1.2.3", null, new[] { new RequiredModuleReference("RuntimeDependency", requiredVersion: "1.0.0") },
                delivery: fixture.Delivery);
            const string relative = "Modules/RuntimeDependency/RuntimeDependency.psm1";
            if (type == ArtefactType.Packed)
            {
                using var zip = ZipFile.OpenRead(result.OutputPath);
                Assert.NotNull(zip.GetEntry(relative));
            }
            else
                Assert.True(File.Exists(Path.Combine(result.OutputPath, relative)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PSModulePath", previous);
        }
    }

    [Theory]
    [InlineData(ArtefactType.Packed)]
    [InlineData(ArtefactType.Unpacked)]
    public void Build_DeliveryLayoutIncludesRootLauncherAndCompleteModule(ArtefactType type)
    {
        using var fixture = new Fixture();
        var segment = fixture.Segment(type);
        bool finalized = false;
        var result = new ArtefactBuilder(new NullLogger()).BuildWithFinalizer(segment,
            fixture.Root, fixture.Staging, "DemoModule", "1.2.3", null,
            Array.Empty<RequiredModuleReference>(), finalizePackedArtefact: context =>
            {
                finalized = true;
                Assert.Equal("launcher", File.ReadAllText(Path.Combine(context.RootPath, "Run.ps1")));
                Assert.True(File.Exists(context.ManifestPath));
                Assert.Equal(Path.Combine(context.RootPath, "Modules", "DemoModule"), context.MainModulePath);
                return null;
            }, delivery: fixture.Delivery);

        string[] expected =
        {
            "Modules/DemoModule/DemoModule.psd1", "Modules/DemoModule/DemoModule.psm1",
            "Modules/DemoModule/Files/Run.ps1", "Modules/DemoModule/Files/config.json",
            "Run.ps1", "config.json"
        };
        if (type == ArtefactType.Packed)
        {
            Assert.True(finalized);
            using var zip = ZipFile.OpenRead(result.OutputPath);
            Assert.Equal(expected.OrderBy(path => path), zip.Entries.Select(entry => entry.FullName).OrderBy(path => path));
            using var reader = new StreamReader(zip.GetEntry("config.json")!.Open());
            Assert.Equal("{}", reader.ReadToEnd());
        }
        else
        {
            Assert.Equal(expected.OrderBy(path => path), Directory.GetFiles(result.OutputPath, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(result.OutputPath, path).Replace('\\', '/')).OrderBy(path => path));
            Assert.Equal("launcher", File.ReadAllText(Path.Combine(result.OutputPath, "Run.ps1")));
        }
    }

    [Theory]
    [InlineData(ArtefactType.Packed)]
    [InlineData(ArtefactType.Unpacked)]
    public void Build_DeliveryLayoutRequiresDeliveryBeforeChangingExistingOutput(ArtefactType type)
    {
        using var fixture = new Fixture();
        string marker = Path.Combine(fixture.Output, "existing.txt");
        File.WriteAllText(marker, "keep");
        var error = Assert.Throws<InvalidOperationException>(() => new ArtefactBuilder(new NullLogger()).Build(
            fixture.Segment(type), fixture.Root, fixture.Staging, "DemoModule", "1.2.3", null,
            Array.Empty<RequiredModuleReference>()));

        Assert.Contains("enabled delivery", error.Message);
        Assert.Equal("keep", File.ReadAllText(marker));
    }

    [Fact]
    public void Build_DeliveryFilesCannotOverwriteModuleNamespace()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(Path.Combine(fixture.Staging, "Files", "Modules", "DemoModule"));
        File.WriteAllText(Path.Combine(fixture.Staging, "Files", "Modules", "DemoModule", "DemoModule.psm1"), "wrong");
        string marker = Path.Combine(fixture.Output, "existing.txt");
        File.WriteAllText(marker, "keep");

        Assert.Throws<InvalidOperationException>(() => fixture.Build(fixture.Segment(ArtefactType.Unpacked)));
        Assert.Equal("keep", File.ReadAllText(marker));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData(".")]
    public void Build_DeliveryModuleDirectoryMustStayBelowBundleRoot(string modulePath)
    {
        using var fixture = new Fixture();
        var segment = fixture.Segment(ArtefactType.Unpacked);
        segment.Configuration.RequiredModules.ModulesPath = modulePath;
        Assert.Throws<InvalidOperationException>(() => fixture.Build(segment));
    }

    [Fact]
    public void Build_DeliveryLayoutDoesNotConvertModulesToScripts()
    {
        using var fixture = new Fixture();
        var error = Assert.Throws<InvalidOperationException>(() => fixture.Build(fixture.Segment(ArtefactType.ScriptPacked)));
        Assert.Contains("Packed and Unpacked", error.Message);
    }

    [Fact]
    public void Factory_PreservesDeliveryLayoutInConfiguration()
    {
        var segment = new ArtefactConfigurationFactory(new NullLogger()).Create(
            new ArtefactConfigurationRequest { Type = ArtefactType.Packed, Layout = ArtefactLayout.Delivery });
        Assert.Equal(ArtefactLayout.Delivery, segment.Configuration.Layout);
    }

    [Fact]
    public void Configuration_DeliveryLayoutRoundTripsThroughJson()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());
        var configuration = new ArtefactConfiguration { Layout = ArtefactLayout.Delivery };
        string json = JsonSerializer.Serialize(configuration, options);
        Assert.Contains("\"Layout\":\"Delivery\"", json);
        Assert.Equal(ArtefactLayout.Delivery, JsonSerializer.Deserialize<ArtefactConfiguration>(json, options)!.Layout);
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        internal string Staging => Path.Combine(Root, "staging");
        internal string Output => Path.Combine(Root, "output");
        internal DeliveryOptionsConfiguration Delivery { get; } = new() { Enable = true, InternalsPath = "Files" };

        internal Fixture()
        {
            Directory.CreateDirectory(Path.Combine(Staging, "Files"));
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Staging, "DemoModule.psm1"), "function Get-Demo { 'demo' }");
            File.WriteAllText(Path.Combine(Staging, "DemoModule.psd1"), "@{RootModule='DemoModule.psm1';ModuleVersion='1.2.3'}");
            File.WriteAllText(Path.Combine(Staging, "Files", "Run.ps1"), "launcher");
            File.WriteAllText(Path.Combine(Staging, "Files", "config.json"), "{}");
        }

        internal ConfigurationArtefactSegment Segment(ArtefactType type) => new()
        {
            ArtefactType = type,
            Configuration = new ArtefactConfiguration { Enabled = true, Path = Output, Layout = ArtefactLayout.Delivery }
        };

        internal ArtefactBuildResult Build(ConfigurationArtefactSegment segment) => new ArtefactBuilder(new NullLogger()).Build(
            segment, Root, Staging, "DemoModule", "1.2.3", null, Array.Empty<RequiredModuleReference>(), delivery: Delivery);

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
