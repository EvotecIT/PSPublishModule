using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineHostedOperationsTests
{
    [Fact]
    public void DefaultRunnerServices_ReuseProvidedPowerShellRunner()
    {
        var powerShellRunner = new RecordingPowerShellRunner(_ => new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh"));

        var services = ModulePipelineRunnerDefaults.Create(new NullLogger(), powerShellRunner, null, null, null, null, null);

        Assert.Same(powerShellRunner, services.PowerShellRunner);
        Assert.IsType<PowerShellModuleDependencyMetadataProvider>(services.ModuleDependencyMetadataProvider);
        Assert.IsType<PowerShellModulePipelineHostedOperations>(services.HostedOperations);
        Assert.IsType<PowerShellMissingFunctionAnalysisService>(services.MissingFunctionAnalysisService);
        Assert.IsType<PowerShellScriptFunctionExportDetector>(services.ScriptFunctionExportDetector);
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_UsesInjectedHostedOperations()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildSegment
                    {
                        BuildModule = new BuildModuleConfiguration
                        {
                            InstallMissingModules = true
                        }
                    },
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.RequiredModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "Pester",
                            RequiredVersion = "5.6.1"
                        }
                    }
                }
            };

            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);

            var plan = runner.Plan(spec);
            var result = InvokeEnsureBuildDependenciesInstalledIfNeeded(runner, plan);

            Assert.Equal(2, result.Length);
            Assert.Equal(2, hostedOperations.DependencyInstallCalls);
            Assert.Equal("Microsoft.PowerShell.PSResourceGet", Assert.Single(hostedOperations.DependencyCalls[0]).Name);
            Assert.Equal("Pester", hostedOperations.LastDependencies.Single().Name);
            Assert.Null(hostedOperations.LastRepository);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_HonorsModuleSkipForDeclaredDependencies()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildSegment
                    {
                        BuildModule = new BuildModuleConfiguration
                        {
                            InstallMissingModules = true
                        }
                    },
                    new ConfigurationModuleSkipSegment
                    {
                        Configuration = new ModuleSkipConfiguration
                        {
                            IgnoreModuleName = new[] { "Pester" }
                        }
                    },
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.RequiredModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "Pester",
                            RequiredVersion = "5.6.1"
                        }
                    }
                }
            };

            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);

            var plan = runner.Plan(spec);
            var result = InvokeEnsureBuildDependenciesInstalledIfNeeded(runner, plan);

            Assert.Single(result);
            Assert.Equal(1, hostedOperations.DependencyInstallCalls);
            Assert.Equal("Pester", Assert.Single(hostedOperations.LastDependencies).Name);
            Assert.Contains("Pester", hostedOperations.LastSkipModules?.IgnoreModuleName ?? Array.Empty<string>());
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_InstallsPesterForConfiguredTests()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var testsPath = Path.Combine(root.FullName, "Tests");
            Directory.CreateDirectory(testsPath);

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationModuleSkipSegment
                    {
                        Configuration = new ModuleSkipConfiguration
                        {
                            IgnoreModuleName = new[] { "Pester" }
                        }
                    },
                    new ConfigurationTestSegment
                    {
                        Configuration = new TestConfiguration
                        {
                            TestsPath = testsPath,
                            When = TestExecutionWhen.AfterMerge
                        }
                    }
                }
            };

            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);

            var plan = runner.Plan(spec);
            var result = InvokeEnsureBuildDependenciesInstalledIfNeeded(runner, plan);

            Assert.Single(result);
            Assert.Equal(1, hostedOperations.DependencyInstallCalls);
            var dependency = Assert.Single(hostedOperations.LastDependencies);
            Assert.Equal("Pester", dependency.Name);
            Assert.Equal("5.7.1", dependency.MinimumVersion);
            Assert.Null(hostedOperations.LastSkipModules);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_SkipsRepositoryToolPreflightForSatisfiedDeclaredDependencies()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildSegment
                    {
                        BuildModule = new BuildModuleConfiguration
                        {
                            InstallMissingModules = true
                        }
                    },
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.RequiredModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "Pester",
                            RequiredVersion = "1.0.0"
                        }
                    }
                }
            };

            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider("Pester"),
                hostedOperations);

            var plan = runner.Plan(spec);
            var result = InvokeEnsureBuildDependenciesInstalledIfNeeded(runner, plan);

            Assert.Single(result);
            Assert.Equal(1, hostedOperations.DependencyInstallCalls);
            Assert.Equal("Pester", Assert.Single(hostedOperations.LastDependencies).Name);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_InstallsFeatureToolsFromDefaultSource()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var testsPath = Path.Combine(root.FullName, "Tests");
            Directory.CreateDirectory(testsPath);

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildSegment
                    {
                        BuildModule = new BuildModuleConfiguration
                        {
                            InstallMissingModulesRepository = "CompanyDependencies"
                        }
                    },
                    new ConfigurationTestSegment
                    {
                        Configuration = new TestConfiguration
                        {
                            TestsPath = testsPath,
                            When = TestExecutionWhen.AfterMerge
                        }
                    }
                }
            };

            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);

            var plan = runner.Plan(spec);
            var result = InvokeEnsureBuildDependenciesInstalledIfNeeded(runner, plan);

            Assert.Single(result);
            Assert.Equal(1, hostedOperations.DependencyInstallCalls);
            Assert.Equal("Pester", Assert.Single(hostedOperations.LastDependencies).Name);
            Assert.Null(hostedOperations.LastRepository);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData("Microsoft.PowerShell.PSResourceGet")]
    [InlineData("PowerShellGet")]
    public void EnsureBuildDependenciesInstalledIfNeeded_SkipsAutoRepositoryPublishPreflightWhenPublishToolExists(string installedPublishTool)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = CreatePublishToolSpec(root.FullName, moduleName, PublishTool.Auto);
            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(installedPublishTool),
                hostedOperations);

            var plan = runner.Plan(spec);
            var result = InvokeEnsureBuildDependenciesInstalledIfNeeded(runner, plan);

            Assert.Empty(result);
            Assert.Equal(0, hostedOperations.DependencyInstallCalls);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_InstallsPSResourceGetForAutoRepositoryPublishWhenNoPublishToolExists()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = CreatePublishToolSpec(root.FullName, moduleName, PublishTool.Auto);
            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);

            var plan = runner.Plan(spec);
            var result = InvokeEnsureBuildDependenciesInstalledIfNeeded(runner, plan);

            Assert.Single(result);
            Assert.Equal(1, hostedOperations.DependencyInstallCalls);
            Assert.Equal("Microsoft.PowerShell.PSResourceGet", Assert.Single(hostedOperations.LastDependencies).Name);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_InstallsPSResourceGetForAutoRepositoryPublishWhenPowerShellGetIsTooOld()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = CreatePublishToolSpec(root.FullName, moduleName, PublishTool.Auto);
            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PowerShellGet"] = "1.0.0.1"
                }),
                hostedOperations);

            var plan = runner.Plan(spec);
            var result = InvokeEnsureBuildDependenciesInstalledIfNeeded(runner, plan);

            Assert.Single(result);
            Assert.Equal(1, hostedOperations.DependencyInstallCalls);
            Assert.Equal("Microsoft.PowerShell.PSResourceGet", Assert.Single(hostedOperations.LastDependencies).Name);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_InstallsPSResourceGetForRequiredModuleDownloadArtefact()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = CreateRequiredModuleArtefactSpec(root.FullName, moduleName, ModuleSaveTool.Auto, RequiredModulesSource.Download);
            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);

            var plan = runner.Plan(spec);
            var result = InvokeEnsureBuildDependenciesInstalledIfNeeded(runner, plan);

            Assert.Single(result);
            Assert.Equal(1, hostedOperations.DependencyInstallCalls);
            Assert.Equal("Microsoft.PowerShell.PSResourceGet", Assert.Single(hostedOperations.LastDependencies).Name);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_PreservesLocalFirstAutoRequiredModuleArtefact()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = CreateRequiredModuleArtefactSpec(root.FullName, moduleName, ModuleSaveTool.Auto, RequiredModulesSource.Auto);
            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider("Dependency.Tools"),
                hostedOperations);

            var plan = runner.Plan(spec);
            var result = InvokeEnsureBuildDependenciesInstalledIfNeeded(runner, plan);

            Assert.Empty(result);
            Assert.Equal(0, hostedOperations.DependencyInstallCalls);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_InstallsPSResourceGetForAutoRequiredModuleArtefactWhenLocalModuleIsMissing()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = CreateRequiredModuleArtefactSpec(root.FullName, moduleName, ModuleSaveTool.Auto, RequiredModulesSource.Auto);
            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);

            var plan = runner.Plan(spec);
            var result = InvokeEnsureBuildDependenciesInstalledIfNeeded(runner, plan);

            Assert.Single(result);
            Assert.Equal(1, hostedOperations.DependencyInstallCalls);
            Assert.Equal("Microsoft.PowerShell.PSResourceGet", Assert.Single(hostedOperations.LastDependencies).Name);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_PreservesExactLocalAutoRequiredModuleArtefactWhenLatestVersionDiffers()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = CreateRequiredModuleArtefactSpec(root.FullName, moduleName, ModuleSaveTool.Auto, RequiredModulesSource.Auto);
            var requiredModule = spec.Segments
                .OfType<ConfigurationModuleSegment>()
                .Single()
                .Configuration;
            requiredModule.ModuleVersion = null;
            requiredModule.RequiredVersion = "1.0.0";

            var moduleLocatorRequests = new List<PowerShellRunRequest>();
            var powerShellRunner = new RecordingPowerShellRunner(request =>
            {
                moduleLocatorRequests.Add(request);
                return new PowerShellRunResult(0, "PFMODLOC::FOUND::version::path", string.Empty, "pwsh");
            });
            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner,
                new FakeMetadataProvider(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Dependency.Tools"] = "2.0.0"
                }),
                hostedOperations);

            var plan = runner.Plan(spec);
            var result = InvokeEnsureBuildDependenciesInstalledIfNeeded(runner, plan);

            Assert.Empty(result);
            Assert.Equal(0, hostedOperations.DependencyInstallCalls);
            Assert.Single(moduleLocatorRequests);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_DoesNotInstallPSResourceGetBeforeOnlineRequiredModuleResolution()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = CreateOnlineRequiredModuleSpec(root.FullName, moduleName);
            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);

            runner.Plan(spec);

            Assert.Equal(0, hostedOperations.DependencyInstallCalls);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void RunPreflight_SkipsPublishVersionSourceWhenRequiredModulesAreConcrete()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildSegment
                    {
                        BuildModule = new BuildModuleConfiguration
                        {
                            ResolveMissingModulesOnline = false
                        }
                    },
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.RequiredModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "Dependency.Tools",
                            ModuleVersion = "1.0.0",
                            Guid = "11111111-1111-1111-1111-111111111111"
                        }
                    },
                    new ConfigurationPublishSegment
                    {
                        Configuration = new PublishConfiguration
                        {
                            Enabled = true,
                            Destination = PublishDestination.PowerShellGallery,
                            Tool = PublishTool.Auto,
                            ApiKey = "test-api-key",
                            UseAsDependencyVersionSource = true
                        }
                    }
                }
            };
            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);

            InvokeEnsureRequiredModuleOnlineResolutionToolInstalledIfNeededForRun(runner, spec);

            Assert.Equal(0, hostedOperations.DependencyInstallCalls);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void RunPreflight_UsesConfigurationOverrideBeforeManifestRequiredModules()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            AddRequiredModuleToManifest(root.FullName, moduleName, "Dependency.Tools", "Latest");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildSegment
                    {
                        BuildModule = new BuildModuleConfiguration
                        {
                            ResolveMissingModulesOnline = false
                        }
                    },
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.RequiredModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "Dependency.Tools",
                            ModuleVersion = "1.0.0",
                            Guid = "11111111-1111-1111-1111-111111111111"
                        }
                    }
                }
            };
            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);

            InvokeEnsureRequiredModuleOnlineResolutionToolInstalledIfNeededForRun(runner, spec);

            Assert.Equal(0, hostedOperations.DependencyInstallCalls);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void RunPreflight_RefreshesRepositorySourcedPrecomputedPlans()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = CreateOnlineRequiredModuleSpec(
                root.FullName,
                moduleName,
                resolveMissingModulesOnline: false,
                versionSource: ModuleDependencyVersionSource.PSGallery);
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Online.Tools"] = "1.0.0"
                }),
                new FakeHostedOperations());

            var plan = runner.Plan(spec);

            Assert.True(InvokeShouldRefreshPrecomputedPlanAfterOnlineRequiredModulePreflight(runner, spec, plan));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void RunPreflight_DoesNotRefreshExactRequiredVersionPrecomputedPlans()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.RequiredModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "Dependency.Tools",
                            RequiredVersion = "1.0.0"
                        }
                    }
                }
            };
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Dependency.Tools"] = "1.0.0"
                }),
                new FakeHostedOperations());

            var plan = runner.Plan(spec);

            Assert.False(InvokeShouldRefreshPrecomputedPlanAfterOnlineRequiredModulePreflight(runner, spec, plan));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_InstallsPSResourceGetBeforeOnlineRequiredModuleResolution()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = CreateOnlineRequiredModuleSpec(root.FullName, moduleName);
            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                FakeMetadataProvider.ThrowingOnlineResolver(),
                hostedOperations);

            Assert.Throws<InvalidOperationException>(() => runner.Run(spec));

            Assert.Equal(1, hostedOperations.DependencyInstallCalls);
            Assert.Equal("Microsoft.PowerShell.PSResourceGet", Assert.Single(hostedOperations.LastDependencies).Name);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_WithPrecomputedPlanInstallsPSResourceGetBeforeOnlineRequiredModuleResolution()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = CreateOnlineRequiredModuleSpec(root.FullName, moduleName);
            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);

            var plan = runner.Plan(spec);
            runner.Run(spec, plan);

            Assert.Equal(1, hostedOperations.DependencyInstallCalls);
            Assert.Equal("Microsoft.PowerShell.PSResourceGet", Assert.Single(hostedOperations.LastDependencies).Name);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_InstallsPSResourceGetForRepositoryVersionSourceEvenWhenOnlineResolutionIsDisabled()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = CreateOnlineRequiredModuleSpec(
                root.FullName,
                moduleName,
                resolveMissingModulesOnline: false,
                versionSource: ModuleDependencyVersionSource.PSGallery);
            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                FakeMetadataProvider.ThrowingOnlineResolver(),
                hostedOperations);

            Assert.Throws<InvalidOperationException>(() => runner.Run(spec));

            Assert.Equal(1, hostedOperations.DependencyInstallCalls);
            Assert.Equal("Microsoft.PowerShell.PSResourceGet", Assert.Single(hostedOperations.LastDependencies).Name);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_InstallsPSResourceGetBeforeManifestRequiredModuleOnlineResolution()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            AddRequiredModuleToManifest(root.FullName, moduleName, "Manifest.Tools", "Latest");

            var spec = CreateOnlineRequiredModuleSpec(root.FullName, moduleName, includeSegmentRequiredModule: false);
            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                FakeMetadataProvider.ThrowingOnlineResolver(),
                hostedOperations);

            Assert.Throws<InvalidOperationException>(() => runner.Run(spec));

            Assert.Equal(1, hostedOperations.DependencyInstallCalls);
            Assert.Equal("Microsoft.PowerShell.PSResourceGet", Assert.Single(hostedOperations.LastDependencies).Name);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_SkipsOnlineRequiredModuleResolutionToolPreflightForRefreshOnly()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = CreateOnlineRequiredModuleSpec(root.FullName, moduleName, refreshPsd1Only: true);
            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);

            runner.Run(spec);

            Assert.Equal(0, hostedOperations.DependencyInstallCalls);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_UsesFilteredPackagingModulesForRequiredModuleArtefact()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = CreateRequiredModuleArtefactSpec(root.FullName, moduleName, ModuleSaveTool.Auto, RequiredModulesSource.Download);
            spec.Segments = new IConfigurationSegment[]
            {
                new ConfigurationBuildSegment
                {
                    BuildModule = new BuildModuleConfiguration
                    {
                        MergeMissing = true
                    }
                }
            }
            .Concat(spec.Segments)
            .Concat(new IConfigurationSegment[]
            {
                new ConfigurationModuleSegment
                {
                    Kind = ModuleDependencyKind.ApprovedModule,
                    Configuration = new ModuleDependencyConfiguration
                    {
                        ModuleName = "Dependency.Tools"
                    }
                }
            })
            .ToArray();

            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);

            var plan = runner.Plan(spec);
            var result = InvokeEnsureBuildDependenciesInstalledIfNeeded(runner, plan);

            Assert.Empty(result);
            Assert.Equal(0, hostedOperations.DependencyInstallCalls);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_InstallsPowerShellGetForExplicitPowerShellGetPublish()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = CreatePublishToolSpec(root.FullName, moduleName, PublishTool.PowerShellGet);
            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);

            var plan = runner.Plan(spec);
            var result = InvokeEnsureBuildDependenciesInstalledIfNeeded(runner, plan);

            Assert.Single(result);
            Assert.Equal(1, hostedOperations.DependencyInstallCalls);
            var dependency = Assert.Single(hostedOperations.LastDependencies);
            Assert.Equal("PowerShellGet", dependency.Name);
            Assert.Equal("2.2.5", dependency.MinimumVersion);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_InstallsPSResourceGetForExplicitPSResourceGetPublish()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = CreatePublishToolSpec(root.FullName, moduleName, PublishTool.PSResourceGet);
            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);

            var plan = runner.Plan(spec);
            var result = InvokeEnsureBuildDependenciesInstalledIfNeeded(runner, plan);

            Assert.Single(result);
            Assert.Equal(1, hostedOperations.DependencyInstallCalls);
            Assert.Equal("Microsoft.PowerShell.PSResourceGet", Assert.Single(hostedOperations.LastDependencies).Name);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ValidateModuleImports_UsesInjectedPowerShellRunner()
    {
        var requests = new List<PowerShellRunRequest>();
        var runner = new RecordingPowerShellRunner(request =>
        {
            requests.Add(request);
            return new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh");
        });

        var operations = new PowerShellModulePipelineHostedOperations(runner, new NullLogger());
        operations.ValidateModuleImports(
            manifestPath: @"C:\Temp\TestModule\TestModule.psd1",
            modules: Array.Empty<ImportModuleEntry>(),
            importRequired: true,
            importSelf: false,
            verbose: true,
            targets: new[]
            {
                new ModuleImportValidationTarget("pwsh", "Core", preferPwsh: true)
            });

        var request = Assert.Single(requests);
        Assert.Equal(PowerShellInvocationMode.File, request.InvocationMode);
        Assert.True(request.PreferPwsh);
        Assert.EndsWith(".ps1", request.ScriptPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SignModuleOutput_UsesInjectedPowerShellRunner()
    {
        var requests = new List<PowerShellRunRequest>();
        var summary = new ModuleSigningResult
        {
            TotalMatched = 1,
            TotalAfterExclude = 1,
            Attempted = 1,
            SignedNew = 1,
            Resigned = 0,
            Failed = 0
        };
        var stdout = "PFSIGN::SUMMARY::" + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(summary)));
        var runner = new RecordingPowerShellRunner(request =>
        {
            requests.Add(request);
            return new PowerShellRunResult(0, stdout, string.Empty, "pwsh");
        });

        var operations = new PowerShellModulePipelineHostedOperations(runner, new NullLogger());
        var result = operations.SignModuleOutput(
            moduleName: "TestModule",
            rootPath: @"C:\Temp\TestModule",
            includePatterns: new[] { "*.psm1" },
            excludeSubstrings: Array.Empty<string>(),
            signing: new SigningOptionsConfiguration());

        var request = Assert.Single(requests);
        Assert.Equal(PowerShellInvocationMode.File, request.InvocationMode);
        Assert.True(request.PreferPwsh);
        Assert.Equal(1, result.SignedNew);
    }

    [Fact]
    public void RunTestsAfterMerge_IncludesFailedTestNamesAndMessages()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var testsPath = Directory.CreateDirectory(Path.Combine(root.FullName, "Tests")).FullName;

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false }
            };

            var hostedOperations = new FakeHostedOperations
            {
                NextTestSuiteResult = new ModuleTestSuiteResult(
                    projectPath: root.FullName,
                    testPath: testsPath,
                    moduleName: moduleName,
                    moduleVersion: "1.0.0",
                    manifestPath: Path.Combine(root.FullName, $"{moduleName}.psd1"),
                    requiredModules: Array.Empty<RequiredModuleReference>(),
                    dependencyResults: Array.Empty<ModuleDependencyInstallResult>(),
                    moduleImported: true,
                    exportedFunctionCount: null,
                    exportedCmdletCount: null,
                    exportedAliasCount: null,
                    pesterVersion: "5.7.1",
                    totalCount: 3,
                    passedCount: 2,
                    failedCount: 1,
                    skippedCount: 0,
                    duration: null,
                    coveragePercent: null,
                    failureAnalysis: new ModuleTestFailureAnalysis
                    {
                        Source = "PesterResults",
                        Timestamp = DateTime.Now,
                        TotalCount = 3,
                        PassedCount = 2,
                        FailedCount = 1,
                        FailedTests = new[]
                        {
                            new ModuleTestFailureInfo
                            {
                                Name = "Broken.Test",
                                ErrorMessage = "boom"
                            }
                        }
                    },
                    exitCode: 1,
                    stdOut: string.Empty,
                    stdErr: string.Empty,
                    resultsXmlPath: null)
            };

            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);

            var plan = runner.Plan(spec);
            var buildResult = new ModuleBuildResult(
                stagingPath: root.FullName,
                manifestPath: Path.Combine(root.FullName, $"{moduleName}.psd1"),
                exports: new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));

            var ex = Assert.Throws<TargetInvocationException>(() =>
                InvokeRunTestsAfterMerge(runner, plan, buildResult, new TestConfiguration { TestsPath = testsPath }));

            var actual = Assert.IsType<InvalidOperationException>(ex.InnerException);
            Assert.Contains("TestsAfterMerge failed (1 failed).", actual.Message, StringComparison.Ordinal);
            Assert.Contains("Broken.Test", actual.Message, StringComparison.Ordinal);
            Assert.Contains("boom", actual.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void RunTestsAfterMerge_FallsBackToCapturedErrorOutput()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var testsPath = Directory.CreateDirectory(Path.Combine(root.FullName, "Tests")).FullName;

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false }
            };

            var hostedOperations = new FakeHostedOperations
            {
                NextTestSuiteResult = new ModuleTestSuiteResult(
                    projectPath: root.FullName,
                    testPath: testsPath,
                    moduleName: moduleName,
                    moduleVersion: "1.0.0",
                    manifestPath: Path.Combine(root.FullName, $"{moduleName}.psd1"),
                    requiredModules: Array.Empty<RequiredModuleReference>(),
                    dependencyResults: Array.Empty<ModuleDependencyInstallResult>(),
                    moduleImported: true,
                    exportedFunctionCount: null,
                    exportedCmdletCount: null,
                    exportedAliasCount: null,
                    pesterVersion: "5.7.1",
                    totalCount: 3,
                    passedCount: 2,
                    failedCount: 1,
                    skippedCount: 0,
                    duration: null,
                    coveragePercent: null,
                    failureAnalysis: null,
                    exitCode: 1,
                    stdOut: "ignored output",
                    stdErr: "\r\nfirst error line\r\nsecond error line",
                    resultsXmlPath: null)
            };

            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);

            var plan = runner.Plan(spec);
            var buildResult = new ModuleBuildResult(
                stagingPath: root.FullName,
                manifestPath: Path.Combine(root.FullName, $"{moduleName}.psd1"),
                exports: new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));

            var ex = Assert.Throws<TargetInvocationException>(() =>
                InvokeRunTestsAfterMerge(runner, plan, buildResult, new TestConfiguration { TestsPath = testsPath }));

            var actual = Assert.IsType<InvalidOperationException>(ex.InnerException);
            Assert.Contains("stderr: first error line", actual.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void RunTestsAfterMerge_OmittedCount_IgnoresBlankFailuresFilteredOutOfOutput()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var testsPath = Directory.CreateDirectory(Path.Combine(root.FullName, "Tests")).FullName;

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false }
            };

            var hostedOperations = new FakeHostedOperations
            {
                NextTestSuiteResult = new ModuleTestSuiteResult(
                    projectPath: root.FullName,
                    testPath: testsPath,
                    moduleName: moduleName,
                    moduleVersion: "1.0.0",
                    manifestPath: Path.Combine(root.FullName, $"{moduleName}.psd1"),
                    requiredModules: Array.Empty<RequiredModuleReference>(),
                    dependencyResults: Array.Empty<ModuleDependencyInstallResult>(),
                    moduleImported: true,
                    exportedFunctionCount: null,
                    exportedCmdletCount: null,
                    exportedAliasCount: null,
                    pesterVersion: "5.7.1",
                    totalCount: 7,
                    passedCount: 0,
                    failedCount: 7,
                    skippedCount: 0,
                    duration: null,
                    coveragePercent: null,
                    failureAnalysis: new ModuleTestFailureAnalysis
                    {
                        Source = "PesterResults",
                        Timestamp = DateTime.Now,
                        TotalCount = 7,
                        PassedCount = 0,
                        FailedCount = 7,
                        FailedTests = new[]
                        {
                            new ModuleTestFailureInfo { Name = "Broken.Test1", ErrorMessage = "boom1" },
                            new ModuleTestFailureInfo { Name = "Broken.Test2", ErrorMessage = "boom2" },
                            new ModuleTestFailureInfo { Name = "Broken.Test3", ErrorMessage = "boom3" },
                            new ModuleTestFailureInfo { Name = "Broken.Test4", ErrorMessage = "boom4" },
                            new ModuleTestFailureInfo { Name = "Broken.Test5", ErrorMessage = "boom5" },
                            new ModuleTestFailureInfo(),
                            new ModuleTestFailureInfo()
                        }
                    },
                    exitCode: 1,
                    stdOut: string.Empty,
                    stdErr: string.Empty,
                    resultsXmlPath: null)
            };

            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);

            var plan = runner.Plan(spec);
            var buildResult = new ModuleBuildResult(
                stagingPath: root.FullName,
                manifestPath: Path.Combine(root.FullName, $"{moduleName}.psd1"),
                exports: new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));

            var ex = Assert.Throws<TargetInvocationException>(() =>
                InvokeRunTestsAfterMerge(runner, plan, buildResult, new TestConfiguration { TestsPath = testsPath }));

            var actual = Assert.IsType<InvalidOperationException>(ex.InnerException);
            Assert.DoesNotContain("Additional failed tests omitted", actual.Message, StringComparison.Ordinal);
            Assert.Contains("Broken.Test5", actual.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static ModuleDependencyInstallResult[] InvokeEnsureBuildDependenciesInstalledIfNeeded(ModulePipelineRunner runner, ModulePipelinePlan plan)
    {
        var method = typeof(ModulePipelineRunner).GetMethod("EnsureBuildDependenciesInstalledIfNeeded", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(method is not null, "EnsureBuildDependenciesInstalledIfNeeded method signature may have changed.");
        return (ModuleDependencyInstallResult[])method!.Invoke(runner, new object?[] { plan })!;
    }

    private static void InvokeEnsureRequiredModuleOnlineResolutionToolInstalledIfNeededForRun(ModulePipelineRunner runner, ModulePipelineSpec spec)
    {
        var method = typeof(ModulePipelineRunner).GetMethod("EnsureRequiredModuleOnlineResolutionToolInstalledIfNeededForRun", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(method is not null, "EnsureRequiredModuleOnlineResolutionToolInstalledIfNeededForRun method signature may have changed.");
        method!.Invoke(runner, new object?[] { spec });
    }

    private static bool InvokeShouldRefreshPrecomputedPlanAfterOnlineRequiredModulePreflight(ModulePipelineRunner runner, ModulePipelineSpec spec, ModulePipelinePlan plan)
    {
        var method = typeof(ModulePipelineRunner).GetMethod("ShouldRefreshPrecomputedPlanAfterOnlineRequiredModulePreflight", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(method is not null, "ShouldRefreshPrecomputedPlanAfterOnlineRequiredModulePreflight method signature may have changed.");
        return (bool)method!.Invoke(runner, new object?[] { spec, plan })!;
    }

    private static void InvokeRunTestsAfterMerge(ModulePipelineRunner runner, ModulePipelinePlan plan, ModuleBuildResult buildResult, TestConfiguration configuration)
    {
        var method = typeof(ModulePipelineRunner).GetMethod("RunTestsAfterMerge", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(method is not null, "RunTestsAfterMerge method signature may have changed.");
        method!.Invoke(runner, new object?[] { plan, buildResult, configuration });
    }

    private static ModulePipelineSpec CreatePublishToolSpec(string root, string moduleName, PublishTool tool)
    {
        return new ModulePipelineSpec
        {
            Build = new ModuleBuildSpec
            {
                Name = moduleName,
                SourcePath = root,
                Version = "1.0.0"
            },
            Install = new ModulePipelineInstallOptions { Enabled = false },
            Segments = new IConfigurationSegment[]
            {
                new ConfigurationPublishSegment
                {
                    Configuration = new PublishConfiguration
                    {
                        Enabled = true,
                        Destination = PublishDestination.PowerShellGallery,
                        Tool = tool,
                        ApiKey = "test-api-key"
                    }
                }
            }
        };
    }

    private static ModulePipelineSpec CreateRequiredModuleArtefactSpec(
        string root,
        string moduleName,
        ModuleSaveTool tool,
        RequiredModulesSource source)
    {
        return new ModulePipelineSpec
        {
            Build = new ModuleBuildSpec
            {
                Name = moduleName,
                SourcePath = root,
                Version = "1.0.0"
            },
            Install = new ModulePipelineInstallOptions { Enabled = false },
            Segments = new IConfigurationSegment[]
            {
                new ConfigurationModuleSegment
                {
                    Kind = ModuleDependencyKind.RequiredModule,
                    Configuration = new ModuleDependencyConfiguration
                    {
                        ModuleName = "Dependency.Tools",
                        ModuleVersion = "1.0.0"
                    }
                },
                new ConfigurationArtefactSegment
                {
                    ArtefactType = ArtefactType.Unpacked,
                    Configuration = new ArtefactConfiguration
                    {
                        Enabled = true,
                        Path = Path.Combine(root, "Artefacts", "Unpacked"),
                        RequiredModules = new ArtefactRequiredModulesConfiguration
                        {
                            Enabled = true,
                            Source = source,
                            Tool = tool
                        }
                    }
                }
            }
        };
    }

    private static ModulePipelineSpec CreateOnlineRequiredModuleSpec(
        string root,
        string moduleName,
        bool refreshPsd1Only = false,
        bool includeSegmentRequiredModule = true,
        bool? resolveMissingModulesOnline = true,
        ModuleDependencyVersionSource versionSource = ModuleDependencyVersionSource.Auto)
    {
        var segments = new List<IConfigurationSegment>
        {
            new ConfigurationBuildSegment
            {
                BuildModule = new BuildModuleConfiguration
                {
                    ResolveMissingModulesOnline = resolveMissingModulesOnline,
                    RefreshPSD1Only = refreshPsd1Only
                }
            }
        };
        if (includeSegmentRequiredModule)
        {
            segments.Add(new ConfigurationModuleSegment
            {
                Kind = ModuleDependencyKind.RequiredModule,
                Configuration = new ModuleDependencyConfiguration
                {
                    ModuleName = "Online.Tools",
                    ModuleVersion = "Latest",
                    VersionSource = versionSource
                }
            });
        }

        return new ModulePipelineSpec
        {
            Build = new ModuleBuildSpec
            {
                Name = moduleName,
                SourcePath = root,
                Version = "1.0.0"
            },
            Install = new ModulePipelineInstallOptions { Enabled = false },
            Segments = segments.ToArray()
        };
    }

    private static void WriteMinimalModule(string moduleRoot, string moduleName, string version)
    {
        Directory.CreateDirectory(moduleRoot);
        File.WriteAllText(Path.Combine(moduleRoot, $"{moduleName}.psm1"), string.Empty);

        var psd1 = string.Join(Environment.NewLine, new[]
        {
            "@{",
            $"    RootModule = '{moduleName}.psm1'",
            $"    ModuleVersion = '{version}'",
            "    FunctionsToExport = @()",
            "    CmdletsToExport = @()",
            "    AliasesToExport = @()",
            "}"
        }) + Environment.NewLine;

        File.WriteAllText(Path.Combine(moduleRoot, $"{moduleName}.psd1"), psd1);
    }

    private static void AddRequiredModuleToManifest(
        string moduleRoot,
        string moduleName,
        string requiredModuleName,
        string requiredModuleVersion)
    {
        var manifestPath = Path.Combine(moduleRoot, $"{moduleName}.psd1");
        var psd1 = string.Join(Environment.NewLine, new[]
        {
            "@{",
            $"    RootModule = '{moduleName}.psm1'",
            "    ModuleVersion = '1.0.0'",
            "    RequiredModules = @(",
            "        @{",
            $"            ModuleName = '{requiredModuleName}'",
            $"            ModuleVersion = '{requiredModuleVersion}'",
            "        }",
            "    )",
            "    FunctionsToExport = @()",
            "    CmdletsToExport = @()",
            "    AliasesToExport = @()",
            "}"
        }) + Environment.NewLine;

        File.WriteAllText(manifestPath, psd1);
    }

    private sealed class FakeMetadataProvider : IModuleDependencyMetadataProvider
    {
        private readonly bool _filterInstalledModules;
        private readonly HashSet<string> _installedModuleNames;
        private readonly IReadOnlyDictionary<string, string> _installedModuleVersions;
        private readonly bool _throwOnResolveLatestOnlineVersions;

        public FakeMetadataProvider()
        {
            _installedModuleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _installedModuleVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public FakeMetadataProvider(params string[] installedModuleNames)
        {
            _filterInstalledModules = true;
            _installedModuleNames = new HashSet<string>(
                installedModuleNames ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            _installedModuleVersions = _installedModuleNames.ToDictionary(
                static name => name,
                static name => name.Equals("PowerShellGet", StringComparison.OrdinalIgnoreCase) ? "2.2.5" : "1.0.0",
                StringComparer.OrdinalIgnoreCase);
        }

        public FakeMetadataProvider(IReadOnlyDictionary<string, string> installedModuleVersions)
        {
            _filterInstalledModules = true;
            _installedModuleVersions = installedModuleVersions ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _installedModuleNames = new HashSet<string>(_installedModuleVersions.Keys, StringComparer.OrdinalIgnoreCase);
        }

        private FakeMetadataProvider(bool throwOnResolveLatestOnlineVersions)
            : this()
        {
            _throwOnResolveLatestOnlineVersions = throwOnResolveLatestOnlineVersions;
        }

        public static FakeMetadataProvider ThrowingOnlineResolver()
        {
            return new FakeMetadataProvider(throwOnResolveLatestOnlineVersions: true);
        }

        public IReadOnlyDictionary<string, InstalledModuleMetadata> GetLatestInstalledModules(IReadOnlyList<string> names)
            => (names ?? Array.Empty<string>())
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Where(name => !_filterInstalledModules || _installedModuleNames.Contains(name))
                .ToDictionary(
                    static name => name,
                    name => _installedModuleNames.Contains(name)
                        ? new InstalledModuleMetadata(name, _installedModuleVersions[name], null, Path.Combine(Path.GetTempPath(), name))
                        : new InstalledModuleMetadata(name, null, null, null),
                    StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<RequiredModuleReference> GetRequiredModulesForInstalledModule(string moduleName)
            => Array.Empty<RequiredModuleReference>();

        public IReadOnlyDictionary<string, (string? Version, string? Guid)> ResolveLatestOnlineVersions(
            IReadOnlyCollection<string> names,
            string? repository,
            RepositoryCredential? credential,
            bool prerelease)
        {
            if (_throwOnResolveLatestOnlineVersions)
                throw new InvalidOperationException("Online metadata resolution was requested.");

            return new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed class FakeHostedOperations : IModulePipelineHostedOperations
    {
        public int DependencyInstallCalls { get; private set; }
        public List<IReadOnlyList<ModuleDependency>> DependencyCalls { get; } = new();
        public IReadOnlyList<ModuleDependency> LastDependencies { get; private set; } = Array.Empty<ModuleDependency>();
        public string? LastRepository { get; private set; }
        public ModuleSkipConfiguration? LastSkipModules { get; private set; }
        public ModuleTestSuiteResult? NextTestSuiteResult { get; set; }

        public IReadOnlyList<ModuleDependencyInstallResult> EnsureDependenciesInstalled(
            ModuleDependency[] dependencies,
            ModuleSkipConfiguration? skipModules,
            bool force,
            string? repository,
            RepositoryCredential? credential,
            bool prerelease)
        {
            DependencyInstallCalls++;
            LastDependencies = dependencies ?? Array.Empty<ModuleDependency>();
            DependencyCalls.Add(LastDependencies);
            LastRepository = repository;
            LastSkipModules = skipModules;
            return LastDependencies
                .Select(dependency => new ModuleDependencyInstallResult(
                    name: dependency.Name,
                    installedVersion: dependency.RequiredVersion ?? dependency.MinimumVersion,
                    resolvedVersion: dependency.RequiredVersion ?? dependency.MinimumVersion,
                    requestedVersion: dependency.RequiredVersion ?? dependency.MinimumVersion,
                    status: ModuleDependencyInstallStatus.Satisfied,
                    installer: "fake",
                    message: "ok"))
                .ToArray();
        }

        public DocumentationBuildResult BuildDocumentation(
            string moduleName,
            string stagingPath,
            string moduleManifestPath,
            DocumentationConfiguration documentation,
            BuildDocumentationConfiguration buildDocumentation,
            IModulePipelineProgressReporter progress,
            ModulePipelineStep? extractStep,
            ModulePipelineStep? writeStep,
            ModulePipelineStep? externalHelpStep)
            => throw new InvalidOperationException("Not used in this test.");

        public ModuleValidationReport ValidateModule(ModuleValidationSpec spec)
            => throw new InvalidOperationException("Not used in this test.");

        public void EnsureBinaryDependenciesValid(string moduleRoot, string powerShellEdition, string? modulePath, string? validationTarget)
            => throw new InvalidOperationException("Not used in this test.");

        public ModuleTestSuiteResult RunModuleTestSuite(ModuleTestSuiteSpec spec)
            => NextTestSuiteResult ?? throw new InvalidOperationException("Not used in this test.");

        public ModulePublishResult PublishModule(
            PublishConfiguration publish,
            ModulePipelinePlan plan,
            ModuleBuildResult buildResult,
            IReadOnlyList<ArtefactBuildResult> artefactResults,
            bool includeScriptFolders)
            => throw new InvalidOperationException("Not used in this test.");

        public void ValidateModuleImports(
            string manifestPath,
            ImportModuleEntry[] modules,
            bool importRequired,
            bool importSelf,
            bool verbose,
            ModuleImportValidationTarget[] targets)
            => throw new InvalidOperationException("Not used in this test.");

        public ModuleSigningResult SignModuleOutput(
            string moduleName,
            string rootPath,
            string[] includePatterns,
            string[] excludeSubstrings,
            SigningOptionsConfiguration signing)
            => throw new InvalidOperationException("Not used in this test.");
    }

    private sealed class ThrowingPowerShellRunner : IPowerShellRunner
    {
        public PowerShellRunResult Run(PowerShellRunRequest request)
            => throw new InvalidOperationException("PowerShell runner should not be used in this test.");
    }

    private sealed class RecordingPowerShellRunner : IPowerShellRunner
    {
        private readonly Func<PowerShellRunRequest, PowerShellRunResult> _run;

        public RecordingPowerShellRunner(Func<PowerShellRunRequest, PowerShellRunResult> run)
        {
            _run = run;
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
            => _run(request);
    }
}
