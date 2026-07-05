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
    public void Plan_MapsDevelopmentBinaryOptions_FromNetAliasBuildLibraries()
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
                            NETDevelopmentBinaries = true,
                            NETDevelopmentBinariesMode = ModuleDevelopmentBinaryMode.Auto,
                            NETDevelopmentBinariesPath = "Sources/Demo/bin",
                            NETDevelopmentBinariesEnvironmentVariable = "DEMO_DEV",
                            NETDevelopmentConfigurationEnvironmentVariable = "DEMO_CONFIGURATION",
                            NETDevelopmentSourceBootstrapperMode = ModuleDevelopmentSourceBootstrapperMode.ReplaceSingleFile
                        }
                    }
                },
                Install = new ModulePipelineInstallOptions { Enabled = false }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);

            Assert.Equal(ModuleDevelopmentBinaryMode.Auto, plan.BuildSpec.DevelopmentBinariesMode);
            Assert.Equal("Sources/Demo/bin", plan.BuildSpec.DevelopmentBinariesPath);
            Assert.Equal("DEMO_DEV", plan.BuildSpec.DevelopmentBinariesEnvironmentVariable);
            Assert.Equal("DEMO_CONFIGURATION", plan.BuildSpec.DevelopmentConfigurationEnvironmentVariable);
            Assert.Equal(ModuleDevelopmentSourceBootstrapperMode.ReplaceSingleFile, plan.BuildSpec.DevelopmentSourceBootstrapperMode);
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
            InvokeSourceBootstrapperRefresh(runner, spec, projectRoot.FullName, moduleName);

            Assert.Equal(handAuthored, File.ReadAllText(psm1Path));
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void SourceRefresh_WithDevelopmentBinaries_DoesNotOverwriteCustomIncludeSourceModule()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "DemoModule";
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "repo"));
            ModulePipelineMissingAnalysisServiceTests.WriteMinimalModule(projectRoot.FullName, moduleName, "1.0.0");
            var commands = Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "Commands"));
            File.WriteAllText(Path.Combine(commands.FullName, "Get-Demo.ps1"), "function Get-Demo { 'demo' }");
            var psm1Path = Path.Combine(projectRoot.FullName, moduleName + ".psm1");
            const string handAuthored = "# local custom source module\r\n. $PSScriptRoot\\Commands\\Get-Demo.ps1\r\n";
            File.WriteAllText(psm1Path, handAuthored);
            WriteDemoCsproj(projectRoot.FullName, "<TargetFramework>net8.0</TargetFramework>");

            var spec = CreateDevelopmentBinarySpec(projectRoot.FullName, moduleName, ModuleDevelopmentBinaryMode.Auto);
            spec.Segments = spec.Segments!
                .Concat(new IConfigurationSegment[]
                {
                    new ConfigurationInformationSegment
                    {
                        Configuration = new InformationConfiguration
                        {
                            IncludePS1 = new[] { "Commands" }
                        }
                    }
                })
                .ToArray();

            var runner = new ModulePipelineRunner(new NullLogger());
            InvokeSourceBootstrapperRefresh(runner, spec, projectRoot.FullName, moduleName);

            Assert.Equal(handAuthored, File.ReadAllText(psm1Path));
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void SourceRefresh_WithDevelopmentBinaries_DoesNotOverwriteMixedCustomIncludeSourceModule()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "DemoModule";
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "repo"));
            ModulePipelineMissingAnalysisServiceTests.WriteMinimalModule(projectRoot.FullName, moduleName, "1.0.0");
            var publicFolder = Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "Public"));
            File.WriteAllText(Path.Combine(publicFolder.FullName, "Get-StandardDemo.ps1"), "function Get-StandardDemo { 'standard' }");
            var commands = Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "Commands"));
            File.WriteAllText(Path.Combine(commands.FullName, "Get-CustomDemo.ps1"), "function Get-CustomDemo { 'custom' }");
            var psm1Path = Path.Combine(projectRoot.FullName, moduleName + ".psm1");
            const string handAuthored = "# local mixed custom source module\r\n. $PSScriptRoot\\Public\\Get-StandardDemo.ps1\r\n. $PSScriptRoot\\Commands\\Get-CustomDemo.ps1\r\n";
            File.WriteAllText(psm1Path, handAuthored);
            WriteDemoCsproj(projectRoot.FullName, "<TargetFramework>net8.0</TargetFramework>");

            var spec = CreateDevelopmentBinarySpec(projectRoot.FullName, moduleName, ModuleDevelopmentBinaryMode.Auto);
            spec.Segments = spec.Segments!
                .Concat(new IConfigurationSegment[]
                {
                    new ConfigurationInformationSegment
                    {
                        Configuration = new InformationConfiguration
                        {
                            IncludePS1 = new[] { "Commands" }
                        }
                    }
                })
                .ToArray();

            var runner = new ModulePipelineRunner(new NullLogger());
            InvokeSourceBootstrapperRefresh(runner, spec, projectRoot.FullName, moduleName);

            Assert.Equal(handAuthored, File.ReadAllText(psm1Path));
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void SourceRefresh_WithDevelopmentBinaries_DoesNotOverwriteIncludeToArrayCustomIncludeSourceModule()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "DemoModule";
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "repo"));
            ModulePipelineMissingAnalysisServiceTests.WriteMinimalModule(projectRoot.FullName, moduleName, "1.0.0");
            var commands = Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "Commands"));
            File.WriteAllText(Path.Combine(commands.FullName, "Get-Demo.ps1"), "function Get-Demo { 'demo' }");
            var psm1Path = Path.Combine(projectRoot.FullName, moduleName + ".psm1");
            const string handAuthored = "# local IncludeToArray source module\r\n. $PSScriptRoot\\Commands\\Get-Demo.ps1\r\n";
            File.WriteAllText(psm1Path, handAuthored);
            WriteDemoCsproj(projectRoot.FullName, "<TargetFramework>net8.0</TargetFramework>");

            var spec = CreateDevelopmentBinarySpec(projectRoot.FullName, moduleName, ModuleDevelopmentBinaryMode.Auto);
            spec.Segments = spec.Segments!
                .Concat(new IConfigurationSegment[]
                {
                    new ConfigurationInformationSegment
                    {
                        Configuration = new InformationConfiguration
                        {
                            IncludeToArray = new[]
                            {
                                new IncludeToArrayEntry
                                {
                                    Key = "IncludePS1",
                                    Values = new[] { "Commands" }
                                }
                            }
                        }
                    }
                })
                .ToArray();

            var runner = new ModulePipelineRunner(new NullLogger());
            InvokeSourceBootstrapperRefresh(runner, spec, projectRoot.FullName, moduleName);

            Assert.Equal(handAuthored, File.ReadAllText(psm1Path));
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void SourceRefresh_WithDevelopmentBinaries_GeneratesLibOnlySourceModule()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "DemoModule";
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "repo"));
            ModulePipelineMissingAnalysisServiceTests.WriteMinimalModule(projectRoot.FullName, moduleName, "1.0.0");
            var libCore = Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "Lib", "Core"));
            File.WriteAllText(Path.Combine(libCore.FullName, moduleName + ".dll"), string.Empty);
            var psm1Path = Path.Combine(projectRoot.FullName, moduleName + ".psm1");
            File.WriteAllText(psm1Path, "# stale source module\r\n");
            WriteDemoCsproj(projectRoot.FullName, "<TargetFramework>net8.0</TargetFramework>");

            var spec = CreateDevelopmentBinarySpec(projectRoot.FullName, moduleName, ModuleDevelopmentBinaryMode.Auto);

            var runner = new ModulePipelineRunner(new NullLogger());
            InvokeSourceBootstrapperRefresh(runner, spec, projectRoot.FullName, moduleName);

            var bootstrapper = File.ReadAllText(psm1Path);
            Assert.Contains("# Auto-generated by PowerForge. Do not edit.", bootstrapper);
            Assert.Contains("# Source development binary loader", bootstrapper);
            Assert.Contains("$PowerForgeDevelopmentBinaryMode = 'Auto'", bootstrapper);
            Assert.Contains("$LibraryName = 'DemoModule'", bootstrapper);
            Assert.True(File.Exists(Path.Combine(projectRoot.FullName, moduleName + ".Libraries.ps1")));
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void SourceRefresh_WithDevelopmentBinariesOff_RegeneratesGeneratedLibOnlySourceBootstrapper()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "DemoModule";
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "repo"));
            ModulePipelineMissingAnalysisServiceTests.WriteMinimalModule(projectRoot.FullName, moduleName, "1.0.0");
            var libCore = Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "Lib", "Core"));
            File.WriteAllText(Path.Combine(libCore.FullName, moduleName + ".dll"), string.Empty);
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
            InvokeSourceBootstrapperRefresh(runner, spec, projectRoot.FullName, moduleName);

            var bootstrapper = File.ReadAllText(psm1Path);
            Assert.Contains("# Auto-generated by PowerForge. Do not edit.", bootstrapper);
            Assert.DoesNotContain("# Source development binary loader", bootstrapper);
            Assert.DoesNotContain("$PowerForgeDevelopmentBinaryMode", bootstrapper);
            Assert.Contains("$LibraryName = 'DemoModule'", bootstrapper);
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void SourceRefresh_WithDevelopmentBinaries_RefreshesGeneratedSingleFileSourceBootstrapper()
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
                "$PowerForgeDevelopmentBinaryMode = 'Environment'",
                "$PowerForgeDevelopmentBinaryEnvironmentVariable = 'OLD_DEV'"
            }));
            WriteDemoCsproj(projectRoot.FullName, "<TargetFramework>net8.0</TargetFramework>");

            var spec = CreateDevelopmentBinarySpec(projectRoot.FullName, moduleName, ModuleDevelopmentBinaryMode.Auto);

            var runner = new ModulePipelineRunner(new NullLogger());
            InvokeSourceBootstrapperRefresh(runner, spec, projectRoot.FullName, moduleName);

            var bootstrapper = File.ReadAllText(psm1Path);
            Assert.Contains("# Source development binary loader", bootstrapper);
            Assert.Contains("$PowerForgeDevelopmentBinaryMode = 'Auto'", bootstrapper);
            Assert.Contains("$PowerForgeDevelopmentBinaryEnvironmentVariable = 'DEMO_DEV'", bootstrapper);
            Assert.DoesNotContain("OLD_DEV", bootstrapper);
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
                sourceBootstrapperMode: ModuleDevelopmentSourceBootstrapperMode.ReplaceSingleFile);

            var runner = new ModulePipelineRunner(new NullLogger());
            InvokeSourceBootstrapperRefresh(runner, spec, projectRoot.FullName, moduleName);

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
    public void SourceRefresh_WithDevelopmentBinariesAndReplaceSingleFileSource_CleansStaleSourceLibPayload()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "DemoModule";
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "repo"));
            ModulePipelineMissingAnalysisServiceTests.WriteMinimalModule(projectRoot.FullName, moduleName, "1.0.0");
            var libCore = Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "Lib", "Core"));
            File.WriteAllText(Path.Combine(libCore.FullName, "Humanizer.dll"), string.Empty);
            File.WriteAllText(Path.Combine(projectRoot.FullName, moduleName + ".Libraries.ps1"), "Lib\\Core\\Humanizer.dll");
            var psm1Path = Path.Combine(projectRoot.FullName, moduleName + ".psm1");
            File.WriteAllText(psm1Path, "# local binary-only source module\r\n");
            WriteDemoCsproj(projectRoot.FullName, "<TargetFrameworks>net8.0;net472</TargetFrameworks>");

            var spec = CreateDevelopmentBinarySpec(
                projectRoot.FullName,
                moduleName,
                ModuleDevelopmentBinaryMode.Environment,
                sourceBootstrapperMode: ModuleDevelopmentSourceBootstrapperMode.ReplaceSingleFile);

            var runner = new ModulePipelineRunner(new NullLogger());
            InvokeSourceBootstrapperRefresh(runner, spec, projectRoot.FullName, moduleName);

            var bootstrapper = File.ReadAllText(psm1Path);
            Assert.Contains("# Source development binary loader", bootstrapper);
            Assert.False(Directory.Exists(Path.Combine(projectRoot.FullName, "Lib")));
            Assert.False(File.Exists(Path.Combine(projectRoot.FullName, moduleName + ".Libraries.ps1")));
            Assert.DoesNotContain("$LibraryName = 'DemoModule'", bootstrapper);
            Assert.DoesNotContain("Humanizer.dll", bootstrapper);
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void SourceRefresh_WithDevelopmentBinariesAndReplaceSingleFileSource_DoesNotOverwriteCustomIncludeSourceModule()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "DemoModule";
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "repo"));
            ModulePipelineMissingAnalysisServiceTests.WriteMinimalModule(projectRoot.FullName, moduleName, "1.0.0");
            var commands = Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "Commands"));
            File.WriteAllText(Path.Combine(commands.FullName, "Get-Demo.ps1"), "function Get-Demo { 'demo' }");
            var psm1Path = Path.Combine(projectRoot.FullName, moduleName + ".psm1");
            const string handAuthored = "# local custom source module\r\n. $PSScriptRoot\\Commands\\Get-Demo.ps1\r\n";
            File.WriteAllText(psm1Path, handAuthored);
            WriteDemoCsproj(projectRoot.FullName, "<TargetFramework>net8.0</TargetFramework>");

            var spec = CreateDevelopmentBinarySpec(
                projectRoot.FullName,
                moduleName,
                ModuleDevelopmentBinaryMode.Environment,
                sourceBootstrapperMode: ModuleDevelopmentSourceBootstrapperMode.ReplaceSingleFile);
            spec.Segments = spec.Segments!
                .Concat(new IConfigurationSegment[]
                {
                    new ConfigurationInformationSegment
                    {
                        Configuration = new InformationConfiguration
                        {
                            IncludePS1 = new[] { "Commands" }
                        }
                    }
                })
                .ToArray();

            var runner = new ModulePipelineRunner(new NullLogger());
            InvokeSourceBootstrapperRefresh(runner, spec, projectRoot.FullName, moduleName);

            Assert.Equal(handAuthored, File.ReadAllText(psm1Path));
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void SourceRefresh_WithDevelopmentBinariesOff_RegeneratesGeneratedSingleFileSourceBootstrapper()
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
            InvokeSourceBootstrapperRefresh(runner, spec, projectRoot.FullName, moduleName);

            var bootstrapper = File.ReadAllText(psm1Path);
            Assert.Contains("# Auto-generated by PowerForge. Do not edit.", bootstrapper);
            Assert.DoesNotContain("# Source development binary loader", bootstrapper);
            Assert.DoesNotContain("$PowerForgeDevelopmentBinaryMode", bootstrapper);
            Assert.Contains("# DemoModule bootstrapper", bootstrapper);
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void SourceRefresh_WithDevelopmentBinariesOff_DoesNotRewriteHandAuthoredFolderSourceModule()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "DemoModule";
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "repo"));
            ModulePipelineMissingAnalysisServiceTests.WriteMinimalModule(projectRoot.FullName, moduleName, "1.0.0");
            Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "Public"));
            File.WriteAllText(Path.Combine(projectRoot.FullName, "Public", "Get-Demo.ps1"), "function Get-Demo { 'demo' }");
            var psm1Path = Path.Combine(projectRoot.FullName, moduleName + ".psm1");
            const string handAuthored = "# local source module\r\n. $PSScriptRoot\\Public\\Get-Demo.ps1\r\n";
            File.WriteAllText(psm1Path, handAuthored);
            WriteDemoCsproj(projectRoot.FullName, "<TargetFramework>net8.0</TargetFramework>");

            var spec = CreateDevelopmentBinarySpec(projectRoot.FullName, moduleName, ModuleDevelopmentBinaryMode.Off);

            var runner = new ModulePipelineRunner(new NullLogger());
            InvokeSourceBootstrapperRefresh(runner, spec, projectRoot.FullName, moduleName);

            Assert.Equal(handAuthored, File.ReadAllText(psm1Path));
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
            InvokeSourceBootstrapperRefresh(runner, spec, projectRoot.FullName, moduleName);

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
        ModuleDevelopmentSourceBootstrapperMode sourceBootstrapperMode = ModuleDevelopmentSourceBootstrapperMode.PreserveSingleFile)
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
                        DevelopmentSourceBootstrapperMode = sourceBootstrapperMode
                    }
                }
            },
            Install = new ModulePipelineInstallOptions { Enabled = false }
        };

    private static void InvokeSourceBootstrapperRefresh(
        ModulePipelineRunner runner,
        ModulePipelineSpec spec,
        string stagingPath,
        string moduleName)
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
        method!.Invoke(runner, new object[] { buildResult, plan });
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
