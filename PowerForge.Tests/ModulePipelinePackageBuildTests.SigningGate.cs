using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class ModulePipelinePackageBuildTests
{
    [Theory]
    [InlineData("json", true, true)]
    [InlineData("reference", true, true)]
    [InlineData("inline", true, true)]
    [InlineData("reference", true, false)]
    [InlineData("reference", false, true)]
    public void Run_GateBuild_PreservesExplicitPackageSigningWithoutPublishing(
        string signingSource, bool signAssemblies, bool signPackages)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var stagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            WriteMinimalModule(root.FullName, "TestModule", "1.0.0");
            var configPath = Path.Combine(root.FullName, "Build", "project.build.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(configPath, JsonSerializer.Serialize(new ProjectBuildConfiguration
            {
                RootPath = "Sources",
                Build = true,
                PublishNuget = true,
                PublishGitHub = true,
                CertificateThumbprint = "ABC123",
                SignAssemblies = signingSource == "json" ? signAssemblies : null,
                SignPackages = signingSource == "json" ? signPackages : null
            }));

            var calls = new List<PackageBuildCall>();
            var runner = new ModulePipelineRunner(new NullLogger(),
                powerShellRunner: null, moduleDependencyMetadataProvider: null,
                hostedOperations: null, manifestMutator: null,
                missingFunctionAnalysisService: null, scriptFunctionExportDetector: null,
                packageBuildExecutor: (request, configuration, path) =>
                {
                    calls.Add(new PackageBuildCall(request, configuration, path));
                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true, ConfigPath = path ?? request.ConfigPath,
                        RootPath = root.FullName,
                        Result = new ProjectBuildResult { Success = true }
                    };
                });

            IConfigurationSegment packageSegment = signingSource == "inline"
                ? new ConfigurationPackageBuildSegment
                {
                    Configuration = new PackageBuildConfiguration
                    {
                        RootPath = "Sources", Name = "Packages", BuildBeforeModule = true,
                        Build = true, PublishNuget = true, PublishGitHub = true,
                        CertificateThumbprint = "ABC123",
                        SignAssemblies = signAssemblies, SignPackages = signPackages
                    }
                }
                : new ConfigurationProjectBuildSegment
                {
                    Configuration = new ProjectBuildConfigurationReference
                    {
                        ConfigPath = configPath, Name = "Packages", BuildBeforeModule = true,
                        SignAssemblies = signingSource == "reference" ? signAssemblies : null,
                        SignPackages = signingSource == "reference" ? signPackages : null
                    }
                };
            var result = runner.Run(new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = "TestModule", SourcePath = root.FullName,
                    Version = "1.0.0", StagingPath = stagingPath
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationGateSegment
                    {
                        Configuration = new GateConfiguration { Mode = ConfigurationGateMode.Build }
                    },
                    packageSegment
                }
            });

            var call = Assert.Single(calls);
            Assert.Equal(ConfigurationGateMode.Build, result.Plan.GateMode);
            Assert.True(call.Request.Build);
            Assert.False(call.Request.PublishNuget);
            Assert.False(call.Request.PublishGitHub);
            Assert.Equal("ABC123", call.Configuration?.CertificateThumbprint);
            Assert.Equal(signAssemblies, call.Configuration?.SignAssemblies);
            Assert.Equal(signPackages, call.Configuration?.SignPackages);
        }
        finally
        {
            root.Delete(recursive: true);
            if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, recursive: true);
        }
    }
}
