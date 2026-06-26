using System.Reflection;
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

    [Fact]
    public void SourceRefresh_WithDevelopmentBinaries_DoesNotOverwriteSingleFileSourceModule()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "DemoModule";
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "repo"));
            ModulePipelineMissingAnalysisServiceTests.WriteMinimalModule(projectRoot.FullName, moduleName, "1.0.0");
            var psm1Path = Path.Combine(projectRoot.FullName, moduleName + ".psm1");
            const string handAuthored = "# local source module\r\nfunction Get-Demo { 'demo' }\r\n";
            File.WriteAllText(psm1Path, handAuthored);
            WriteDemoCsproj(projectRoot.FullName, "<TargetFramework>net8.0</TargetFramework>");

            var spec = CreateDevelopmentBinarySpec(projectRoot.FullName, moduleName, ModuleDevelopmentBinaryMode.Auto);

            var runner = new ModulePipelineRunner(new NullLogger());
            InvokeSourceBootstrapperRefresh(runner, spec, projectRoot.FullName, moduleName, sourceIsSingleFileModule: true);

            Assert.Equal(handAuthored, File.ReadAllText(psm1Path));
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void SourceRefresh_WithDevelopmentBinariesAndReplaceSingleFileSource_OverwritesSourceModule()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "DemoModule";
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "repo"));
            ModulePipelineMissingAnalysisServiceTests.WriteMinimalModule(projectRoot.FullName, moduleName, "1.0.0");
            var psm1Path = Path.Combine(projectRoot.FullName, moduleName + ".psm1");
            File.WriteAllText(psm1Path, "# local binary-only source module\r\n");
            WriteDemoCsproj(projectRoot.FullName, "<TargetFrameworks>net8.0;net472</TargetFrameworks>");

            var spec = CreateDevelopmentBinarySpec(
                projectRoot.FullName,
                moduleName,
                ModuleDevelopmentBinaryMode.Environment,
                replaceSingleFileSource: true);

            var runner = new ModulePipelineRunner(new NullLogger());
            InvokeSourceBootstrapperRefresh(runner, spec, projectRoot.FullName, moduleName, sourceIsSingleFileModule: true);

            var bootstrapper = File.ReadAllText(psm1Path);
            Assert.Contains("# Auto-generated by PowerForge. Do not edit.", bootstrapper);
            Assert.Contains("# Source development binary loader", bootstrapper);
            Assert.Contains("$PowerForgeDevelopmentBinaryMode = 'Environment'", bootstrapper);
            Assert.Contains("$PowerForgeDevelopmentCoreFrameworks = @('net8.0', 'net472')", bootstrapper);
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void SourceRefresh_WithDevelopmentBinariesOff_RemovesGeneratedSingleFileSourceBootstrapper()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "DemoModule";
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "repo"));
            ModulePipelineMissingAnalysisServiceTests.WriteMinimalModule(projectRoot.FullName, moduleName, "1.0.0");
            var psm1Path = Path.Combine(projectRoot.FullName, moduleName + ".psm1");
            File.WriteAllText(psm1Path, string.Join(Environment.NewLine, new[]
            {
                "# Auto-generated by PowerForge. Do not edit.",
                "# DemoModule bootstrapper",
                "# Source development binary loader",
                "$PowerForgeDevelopmentBinaryMode = 'Auto'"
            }));
            WriteDemoCsproj(projectRoot.FullName, "<TargetFramework>net8.0</TargetFramework>");

            var spec = CreateDevelopmentBinarySpec(projectRoot.FullName, moduleName, ModuleDevelopmentBinaryMode.Off);

            var runner = new ModulePipelineRunner(new NullLogger());
            InvokeSourceBootstrapperRefresh(runner, spec, projectRoot.FullName, moduleName, sourceIsSingleFileModule: true);

            Assert.False(File.Exists(psm1Path));
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void SourceRefresh_WithDevelopmentBinariesAndNoDeclaredFrameworks_UsesCsprojFrameworkCandidates()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "DemoModule";
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "repo"));
            ModulePipelineMissingAnalysisServiceTests.WriteMinimalModule(projectRoot.FullName, moduleName, "1.0.0");
            Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "Public"));
            File.WriteAllText(Path.Combine(projectRoot.FullName, "Public", "Get-Demo.ps1"), "function Get-Demo { 'demo' }");

            WriteDemoCsproj(projectRoot.FullName, "<TargetFrameworks>net9.0;net472</TargetFrameworks>");

            var spec = CreateDevelopmentBinarySpec(projectRoot.FullName, moduleName, ModuleDevelopmentBinaryMode.Auto);

            var runner = new ModulePipelineRunner(new NullLogger());
            InvokeSourceBootstrapperRefresh(runner, spec, projectRoot.FullName, moduleName, sourceIsSingleFileModule: false);

            var bootstrapper = File.ReadAllText(Path.Combine(projectRoot.FullName, moduleName + ".psm1"));
            Assert.Contains("$PowerForgeDevelopmentCoreFrameworks = @('net9.0', 'net472')", bootstrapper);
            Assert.Contains("$PowerForgeDevelopmentDesktopFrameworks = @('net472', 'net9.0')", bootstrapper);
            Assert.DoesNotContain("$PowerForgeDevelopmentCoreFrameworks = @('net8.0', 'netstandard2.0', 'net472')", bootstrapper);
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static ModulePipelineSpec CreateDevelopmentBinarySpec(
        string sourcePath,
        string moduleName,
        ModuleDevelopmentBinaryMode mode,
        bool replaceSingleFileSource = false)
        => new()
        {
            Build = new ModuleBuildSpec
            {
                Name = moduleName,
                SourcePath = sourcePath,
                Version = "1.0.0",
                KeepStaging = true,
                ExportAssemblies = Array.Empty<string>()
            },
            Segments = new IConfigurationSegment[]
            {
                new ConfigurationBuildLibrariesSegment
                {
                    BuildLibraries = new BuildLibrariesConfiguration
                    {
                        NETProjectPath = "Sources/Demo/Demo.csproj",
                        DevelopmentBinaries = mode != ModuleDevelopmentBinaryMode.Off,
                        DevelopmentBinariesMode = mode,
                        DevelopmentBinariesPath = "Sources/Demo/bin",
                        DevelopmentBinariesEnvironmentVariable = "DEMO_DEV",
                        DevelopmentConfigurationEnvironmentVariable = "DEMO_CONFIGURATION",
                        DevelopmentBinariesReplaceSingleFileSource = replaceSingleFileSource
                    }
                }
            },
            Install = new ModulePipelineInstallOptions { Enabled = false }
        };

    private static void InvokeSourceBootstrapperRefresh(
        ModulePipelineRunner runner,
        ModulePipelineSpec spec,
        string stagingPath,
        string moduleName,
        bool sourceIsSingleFileModule)
    {
        var plan = runner.Plan(spec);
        var manifestPath = Path.Combine(stagingPath, moduleName + ".psd1");
        var buildResult = new ModuleBuildResult(
            stagingPath,
            manifestPath,
            new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));
        var method = typeof(ModulePipelineRunner).GetMethod(
            "TryRegenerateSourceDevelopmentBootstrapperFromManifest",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        method!.Invoke(runner, new object[] { buildResult, plan, sourceIsSingleFileModule });
    }

    private static void WriteDemoCsproj(string projectRoot, string frameworkProperty)
    {
        var csprojDir = Directory.CreateDirectory(Path.Combine(projectRoot, "Sources", "Demo"));
        File.WriteAllText(Path.Combine(csprojDir.FullName, "Demo.csproj"), $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    {{frameworkProperty}}
  </PropertyGroup>
</Project>
""");
    }
}
