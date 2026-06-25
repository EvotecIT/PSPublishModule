using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineDevelopmentBootstrapperTests
{
    [Fact]
    public void Plan_MapsDevelopmentBinaryOptions_FromBuildLibraries()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "repo"));
            var csprojDir = Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "Sources", "Demo"));
            File.WriteAllText(Path.Combine(csprojDir.FullName, "Demo.csproj"), "<Project />");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = "DemoModule",
                    SourcePath = projectRoot.FullName,
                    Version = "1.0.0",
                    ExportAssemblies = Array.Empty<string>()
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildLibrariesSegment
                    {
                        BuildLibraries = new BuildLibrariesConfiguration
                        {
                            NETProjectPath = "Sources/Demo/Demo.csproj",
                            DevelopmentBinaries = true,
                            DevelopmentBinariesMode = ModuleDevelopmentBinaryMode.Environment,
                            DevelopmentBinariesPath = "Sources/Demo/bin",
                            DevelopmentBinariesEnvironmentVariable = "DEMO_DEV",
                            DevelopmentConfigurationEnvironmentVariable = "DEMO_CONFIGURATION"
                        }
                    }
                },
                Install = new ModulePipelineInstallOptions { Enabled = false }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);

            Assert.Equal(ModuleDevelopmentBinaryMode.Environment, plan.BuildSpec.DevelopmentBinariesMode);
            Assert.Equal("Sources/Demo/bin", plan.BuildSpec.DevelopmentBinariesPath);
            Assert.Equal("DEMO_DEV", plan.BuildSpec.DevelopmentBinariesEnvironmentVariable);
            Assert.Equal("DEMO_CONFIGURATION", plan.BuildSpec.DevelopmentConfigurationEnvironmentVariable);
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
