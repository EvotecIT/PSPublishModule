using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace PowerForge.Tests;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed partial class ArtefactBuilderDeliveryLayoutTests
{
    [Theory]
    [InlineData(ArtefactType.Packed, null, "")]
    [InlineData(ArtefactType.Unpacked, null, "")]
    [InlineData(ArtefactType.Packed, ".", "")]
    [InlineData(ArtefactType.Unpacked, ".", "")]
    [InlineData(ArtefactType.Packed, "Launchers/Operations", "Launchers/Operations/")]
    [InlineData(ArtefactType.Unpacked, "Launchers/Operations", "Launchers/Operations/")]
    [InlineData(ArtefactType.Packed, " \"Launchers\\Tasks\" ", "Launchers/Tasks/")]
    [InlineData(ArtefactType.Unpacked, " \"Launchers\\Tasks\" ", "Launchers/Tasks/")]
    public void Build_DeliveryPathPlacesFilesWithoutMovingModulePayload(ArtefactType type, string? path, string prefix)
    {
        using var fixture = new Fixture();
        var segment = fixture.Segment(type);
        segment.Configuration.DeliveryPath = path;
        var result = fixture.Build(segment);
        if (type == ArtefactType.Packed)
        {
            using var zip = ZipFile.OpenRead(result.OutputPath);
            Assert.NotNull(zip.GetEntry(prefix + "Run.ps1"));
            Assert.NotNull(zip.GetEntry(prefix + "config.json"));
            Assert.NotNull(zip.GetEntry("Modules/DemoModule/DemoModule.psm1"));
            Assert.NotNull(zip.GetEntry("Modules/DemoModule/Files/Run.ps1"));
            if (prefix.Length > 0) Assert.Null(zip.GetEntry("Run.ps1"));
        }
        else
        {
            Assert.Equal("launcher", File.ReadAllText(Path.Combine(result.OutputPath, prefix, "Run.ps1")));
            Assert.Equal("{}", File.ReadAllText(Path.Combine(result.OutputPath, prefix, "config.json")));
            Assert.True(File.Exists(Path.Combine(result.OutputPath, "Modules/DemoModule/Files/Run.ps1")));
            if (prefix.Length > 0) Assert.False(File.Exists(Path.Combine(result.OutputPath, "Run.ps1")));
        }
    }

    [Theory]
    [InlineData(ArtefactType.Packed, "../outside")]
    [InlineData(ArtefactType.Unpacked, "../outside")]
    [InlineData(ArtefactType.Packed, "/outside")]
    [InlineData(ArtefactType.Unpacked, "/outside")]
    [InlineData(ArtefactType.Packed, "C:\\outside")]
    [InlineData(ArtefactType.Unpacked, "C:\\outside")]
    [InlineData(ArtefactType.Packed, "Modules")]
    [InlineData(ArtefactType.Unpacked, "Modules")]
    public void Build_RejectsUnsafeDeliveryPathBeforeChangingOutput(ArtefactType type, string path)
    {
        using var fixture = new Fixture();
        string marker = Path.Combine(fixture.Output, "existing.txt");
        File.WriteAllText(marker, "keep");
        var segment = fixture.Segment(type);
        segment.Configuration.DeliveryPath = path;
        Assert.Throws<InvalidOperationException>(() => fixture.Build(segment));
        Assert.Equal("keep", File.ReadAllText(marker));
    }

    [Fact]
    public void Build_RejectsDeliveryPathWithoutDeliveryLayout()
    {
        using var fixture = new Fixture();
        var segment = fixture.Segment(ArtefactType.Unpacked);
        segment.Configuration.Layout = ArtefactLayout.Module;
        segment.Configuration.DeliveryPath = "Launchers";
        Assert.Contains("requires the Delivery layout", Assert.Throws<InvalidOperationException>(() => fixture.Build(segment)).Message);
    }

    [Theory]
    [InlineData(ArtefactType.Packed, " Files ")]
    [InlineData(ArtefactType.Unpacked, " Files ")]
    [InlineData(ArtefactType.Packed, "\"Files\"")]
    [InlineData(ArtefactType.Unpacked, "\"Files\"")]
    public void Build_DeliveryLayoutUsesNormalizedInternalsPath(ArtefactType type, string internalsPath)
    {
        using var fixture = new Fixture();
        fixture.Delivery.InternalsPath = internalsPath;
        var result = fixture.Build(fixture.Segment(type));
        if (type == ArtefactType.Packed)
        {
            using var archive = ZipFile.OpenRead(result.OutputPath);
            Assert.NotNull(archive.GetEntry("Run.ps1"));
        }
        else
            Assert.Equal("launcher", File.ReadAllText(Path.Combine(result.OutputPath, "Run.ps1")));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void Run_RejectsPromotedDeliveryFileConflictingWithAnotherArtefactBeforeChangingOutput(
        bool packedFirst, bool nestedDestination, bool customDeliveryPath)
    {
        using var fixture = new Fixture();
        string subdirectory = nestedDestination ? "archives" : string.Empty;
        Directory.CreateDirectory(Path.Combine(fixture.Staging, "Files", subdirectory));
        File.WriteAllText(Path.Combine(fixture.Staging, "Files", subdirectory, "Bundle.zip"), "delivery file");
        string marker = Path.Combine(fixture.Output, "keep.txt");
        File.WriteAllText(marker, "existing output");
        var loose = fixture.Segment(ArtefactType.Unpacked);
        loose.Configuration.DeliveryPath = customDeliveryPath ? "Launchers" : null;
        loose.Configuration.DoNotClear = true;
        var packed = fixture.Segment(ArtefactType.Packed);
        packed.Configuration.Path = Path.Combine(fixture.Output, customDeliveryPath ? "Launchers" : string.Empty, subdirectory);
        packed.Configuration.ArtefactName = "Bundle.zip";
        packed.Configuration.DoNotClear = true;
        var spec = new ModulePipelineSpec
        {
            Build = new ModuleBuildSpec { Name = "DemoModule", SourcePath = fixture.Staging, Version = "1.2.3", KeepStaging = true },
            Install = new ModulePipelineInstallOptions { Enabled = false },
            Segments = new IConfigurationSegment[]
            {
                new ConfigurationOptionsSegment { Options = new ConfigurationOptions { Delivery = fixture.Delivery } },
                packedFirst ? packed : loose,
                packedFirst ? loose : packed
            }
        };
        var runner = new ModulePipelineRunner(new NullLogger());
        var error = Assert.Throws<InvalidOperationException>(() => runner.Run(spec, runner.Plan(spec)));
        Assert.Contains("unsafe", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("existing output", File.ReadAllText(marker));
        Assert.False(File.Exists(Path.Combine(packed.Configuration.Path, "Bundle.zip")));
        Assert.False(Directory.Exists(Path.Combine(fixture.Output, "Modules")));
    }

    [Theory]
    [InlineData(ArtefactType.Packed, false)]
    [InlineData(ArtefactType.Unpacked, false)]
    [InlineData(ArtefactType.Packed, true)]
    [InlineData(ArtefactType.Unpacked, true)]
    public void Build_DeliveryLayoutBundlesRemainingInstalledDependenciesAtConfiguredLocations(ArtefactType type, bool customPaths)
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
            segment.Configuration.RequiredModules.ModulesPath = customPaths ? "Application/Modules" : null;
            segment.Configuration.RequiredModules.Path = customPaths ? "Shared/Modules" : null;
            segment.Configuration.RequiredModules.Enabled = true;
            segment.Configuration.RequiredModules.Source = RequiredModulesSource.Installed;
            var result = new ArtefactBuilder(new NullLogger()).Build(segment, fixture.Root, fixture.Staging,
                "DemoModule", "1.2.3", null, new[] { new RequiredModuleReference("RuntimeDependency", requiredVersion: "1.0.0") },
                delivery: fixture.Delivery);
            string relative = (customPaths ? "Shared/Modules" : "Modules") + "/RuntimeDependency/RuntimeDependency.psm1";
            string main = (customPaths ? "Application/Modules" : "Modules") + "/DemoModule/DemoModule.psd1";
            if (type == ArtefactType.Packed)
            {
                using var zip = ZipFile.OpenRead(result.OutputPath);
                Assert.NotNull(zip.GetEntry(relative));
                Assert.NotNull(zip.GetEntry(main));
            }
            else
            {
                Assert.True(File.Exists(Path.Combine(result.OutputPath, relative)));
                Assert.True(File.Exists(Path.Combine(result.OutputPath, main)));
            }
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
            Assert.Contains(result.CopiedItems, item => item.Destination == Path.Combine(result.OutputPath, "Run.ps1") && !item.IsDirectory);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_RejectsRootDeliveryFileMutationByAfterModuleProducer(bool removeFile)
    {
        using var fixture = new Fixture();
        var runner = new ModulePipelineRunner(new NullLogger(), powerShellRunner: null,
            moduleDependencyMetadataProvider: null, hostedOperations: null, manifestMutator: null,
            missingFunctionAnalysisService: null, scriptFunctionExportDetector: null,
            packageBuildExecutor: (request, configuration, configPath) =>
        {
            string launcher = Path.Combine(fixture.Output, "Run.ps1");
            Assert.True(File.Exists(launcher));
            if (removeFile)
                File.Delete(launcher);
            else
                File.WriteAllText(launcher, "replacement");
            return new ProjectBuildHostExecutionResult
            {
                Success = true, RootPath = fixture.Staging, ConfigPath = configPath ?? string.Empty,
                Result = new ProjectBuildResult { Success = true, Release = new DotNetRepositoryReleaseResult { Success = true } }
            };
        });
        var spec = new ModulePipelineSpec
        {
            Build = new ModuleBuildSpec { Name = "DemoModule", SourcePath = fixture.Staging, Version = "1.2.3", KeepStaging = true },
            Install = new ModulePipelineInstallOptions { Enabled = false },
            Segments = new IConfigurationSegment[]
            {
                new ConfigurationOptionsSegment { Options = new ConfigurationOptions { Delivery = fixture.Delivery } },
                fixture.Segment(ArtefactType.Unpacked),
                new ConfigurationPackageBuildSegment
                {
                    Configuration = new PackageBuildConfiguration { Name = "AfterModule", RootPath = ".", BuildBeforeModule = false }
                }
            }
        };
        var error = Assert.Throws<InvalidOperationException>(() => runner.Run(spec, runner.Plan(spec)));
        Assert.Contains("finalized artefact", error.Message, StringComparison.OrdinalIgnoreCase);
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
            new ArtefactConfigurationRequest
            {
                Type = ArtefactType.Packed, Layout = ArtefactLayout.Delivery, DeliveryPath = "Launchers",
                DeliveryMappings = new[] { new ArtefactCopyMapping { Source = "Run.ps1", Destination = "bin/Start.ps1" } }
            });
        Assert.Equal(ArtefactLayout.Delivery, segment.Configuration.Layout);
        Assert.Equal("Launchers", segment.Configuration.DeliveryPath);
        var mapping = Assert.Single(segment.Configuration.DeliveryMappings!);
        Assert.Equal("Run.ps1", mapping.Source);
        Assert.Equal("bin/Start.ps1", mapping.Destination);
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
