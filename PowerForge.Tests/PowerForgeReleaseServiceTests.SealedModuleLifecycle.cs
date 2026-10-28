using System.Text;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void Execute_preserves_source_loader_through_deferred_publication_and_registry_recovery(
        bool guardedSource, bool publishModule)
    {
        var root = CreateSandbox();
        try
        {
            const string name = "SampleModule";
            var source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
            Directory.CreateDirectory(Path.Combine(source, "Public"));
            File.WriteAllText(Path.Combine(source, "Public", "Get-Sample.ps1"), "function Get-Sample { 'sample' }");
            File.WriteAllText(Path.Combine(source, name + ".psd1"),
                "@{ RootModule = 'SampleModule.psm1'; ModuleVersion = '1.0.0'; FunctionsToExport = @('Get-Sample'); CmdletsToExport = @(); AliasesToExport = @() }");
            var binaries = Directory.CreateDirectory(Path.Combine(root, "binaries")).FullName;
            ModuleBootstrapperGenerator.Generate(source, name,
                new ExportSet([], [], []), ["SampleModule.Binary.dll"],
                handleRuntimes: false, useAssemblyLoadContext: true,
                developmentBinaries: new ModuleDevelopmentBinaryBootstrapperOptions(
                    ModuleDevelopmentBinaryMode.Auto, binaries, "SAMPLE_DEV", "SAMPLE_CONFIGURATION",
                    ["net8.0"], ["net472"]));
            var loader = Path.Combine(source, name + ".psm1");
            File.AppendAllText(loader, "\n# Retained source loader revision.\n", new UTF8Encoding(false));
            var original = File.ReadAllBytes(loader);
            // Commit native normalized metadata, as the release preparation lane does.
            new ModulePipelineRunner(new NullLogger()).Run(new ModulePipelineSpec
            {
                ReleaseCheckpoint = true,
                Build = new ModuleBuildSpec
                {
                    Name = name, SourcePath = source, Version = "1.0.0",
                    StagingPath = Path.Combine(root, "prepared"), KeepStaging = true,
                    DevelopmentBinariesMode = ModuleDevelopmentBinaryMode.Auto,
                    DevelopmentBinariesPath = binaries,
                    DevelopmentSourceBootstrapperMode = ModuleDevelopmentSourceBootstrapperMode.ReplaceSingleFile
                },
                Install = new ModulePipelineInstallOptions { Enabled = false }
            });
            RunGitForSourceGuard(source, "init --quiet");
            RunGitForSourceGuard(source, "config core.autocrlf false");
            RunGitForSourceGuard(source, "config user.name \"PowerForge Tests\"");
            RunGitForSourceGuard(source, "config user.email \"powerforge-tests@example.invalid\"");
            RunGitForSourceGuard(source, "add .");
            RunGitForSourceGuard(source, "commit --quiet -m fixture");
            var revision = RunGitForSourceGuard(source, "rev-parse HEAD").Trim();
            var configPath = Path.Combine(root, "module.json");
            File.WriteAllText(configPath, """
                { "Build": { "Name": "SampleModule", "SourcePath": "source", "Version": "1.0.0" }, "Segments": [] }
                """);
            var releasePath = Path.Combine(root, "release.json");
            File.WriteAllText(releasePath, "{}");
            var calls = new List<ModuleExecutionSnapshot>();
            var stagedLoaders = new List<string>();
            var service = CreateReleaseService(root, calls,
                new PowerForgeToolReleaseResult { Success = true },
                onModuleExecution: request =>
                {
                    // Exercise the real module engine for BOTH host invocations.
                    // Only the outer tool/network boundary is replaced by the fixture.
                    var runner = new ModulePipelineRunner(new NullLogger());
                    var built = runner.Run(new ModulePipelineSpec
                    {
                        ReleaseCheckpoint = request.ReleaseCheckpoint,
                        Build = new ModuleBuildSpec
                        {
                            Name = name, SourcePath = source, Version = "1.0.0",
                            StagingPath = request.StagingPath ?? Path.Combine(root, "staging"),
                            ReuseStaging = request.ReuseStaging, KeepStaging = true,
                            SkipDotNetBuild = request.NoDotnetBuild,
                            DevelopmentBinariesMode = ModuleDevelopmentBinaryMode.Auto,
                            DevelopmentBinariesPath = binaries,
                            DevelopmentSourceBootstrapperMode = ModuleDevelopmentSourceBootstrapperMode.ReplaceSingleFile
                        },
                        Install = new ModulePipelineInstallOptions { Enabled = false },
                        Segments = [new ConfigurationGateSegment
                        {
                            Configuration = new GateConfiguration { Mode = request.RunMode ?? ConfigurationGateMode.Build }
                        }]
                    });
                    stagedLoaders.Add(File.ReadAllText(Path.Combine(built.BuildResult!.StagingPath, name + ".psm1")));
                });
            var result = service.Execute(new PowerForgeReleaseSpec
            {
                Module = new PowerForgeModuleReleaseOptions { RepositoryRoot = root, ConfigPath = configPath },
                Tools = new PowerForgeToolReleaseSpec
                {
                    ProjectRoot = root,
                    Targets = [new PowerForgeToolReleaseTarget
                    {
                        Name = "SampleTool", ProjectPath = "SampleTool.csproj", OutputName = "SampleTool"
                    }]
                }
            }, new PowerForgeReleaseRequest
            {
                ConfigPath = releasePath, ModuleRunMode = ConfigurationGateMode.Publish,
                ModuleIncludePublishing = publishModule,
                SourceRepositoryRoot = guardedSource ? source : null,
                ExpectedSourceRevision = guardedSource ? revision : null
            });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(publishModule ? 2 : 1, calls.Count);
            Assert.Equal(original, File.ReadAllBytes(loader));
            Assert.All(stagedLoaders, staged =>
            {
                Assert.Contains("Get-Sample", staged, StringComparison.Ordinal);
                Assert.DoesNotContain("Retained source loader revision", staged, StringComparison.Ordinal);
            });
            if (publishModule)
            {
                Assert.Equal(ConfigurationGateMode.Build, calls[0].RunMode);
                Assert.Equal(ConfigurationGateMode.Publish, calls[1].RunMode);
                Assert.True(calls[1].ReuseStaging);
                Assert.True(calls[1].IncludeModulePublishing);
            }
            else
            {
                Assert.Equal(ConfigurationGateMode.Publish, calls[0].RunMode);
                Assert.False(calls[0].IncludeModulePublishing);
            }
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            TryDelete(root);
        }
    }
}
