using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    public void Run_ExecutesLifecycleActionWithStableContext()
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
                    new ConfigurationActionSegment
                    {
                        Configuration = new ModulePipelineActionConfiguration
                        {
                            Name = "Inspect staged module",
                            At = ModulePipelineActionStage.AfterStaging,
                            InlineScript = "$ctx = Get-Content $env:POWERFORGE_CONTEXT | ConvertFrom-Json"
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

            var result = runner.Run(spec);

            var actionResult = Assert.Single(result.ActionResults);
            var context = Assert.Single(hostedOperations.ActionContexts);
            Assert.Single(hostedOperations.ActionContextPaths);
            Assert.Equal("Inspect staged module", actionResult.Name);
            Assert.True(actionResult.Succeeded);
            Assert.Equal(ModulePipelineActionStage.AfterStaging, context.Stage);
            Assert.Equal("Inspect staged module", context.ActionName);
            Assert.Equal(moduleName, context.ModuleName);
            Assert.Equal(root.FullName, context.ProjectRoot);
            Assert.Equal("1.0.0", context.ExpectedVersion);
            Assert.Equal("1.0.0", context.ResolvedVersion);
            Assert.False(string.IsNullOrWhiteSpace(context.StagingPath));
            Assert.Equal(context.StagingPath, context.ModuleRoot);
            Assert.Equal(context.ContextPath, hostedOperations.ActionContextPaths.Single());
            Assert.True(File.Exists(context.ContextPath));

            var jsonOptions = new JsonSerializerOptions();
            jsonOptions.Converters.Add(new JsonStringEnumConverter());
            var contextJson = File.ReadAllText(context.ContextPath);
            Assert.Contains("\"Stage\": \"AfterStaging\"", contextJson, StringComparison.Ordinal);

            var persisted = JsonSerializer.Deserialize<ModulePipelineActionContext>(contextJson, jsonOptions);
            Assert.NotNull(persisted);
            Assert.Equal(ModulePipelineActionStage.AfterStaging, persisted!.Stage);
            Assert.Equal(moduleName, persisted.ModuleName);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_ContinuesWhenLifecycleActionFailureAllowsContinue()
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
                    new ConfigurationActionSegment
                    {
                        Configuration = new ModulePipelineActionConfiguration
                        {
                            Name = "Advisory check",
                            At = ModulePipelineActionStage.AfterStaging,
                            InlineScript = "exit 7",
                            ContinueOnError = true
                        }
                    }
                }
            };

            var hostedOperations = new FakeHostedOperations
            {
                NextActionSucceeded = false,
                NextActionExitCode = 7,
                NextActionStdErr = "advisory failure"
            };
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);

            var result = runner.Run(spec);

            var actionResult = Assert.Single(result.ActionResults);
            Assert.False(actionResult.Succeeded);
            Assert.Equal(7, actionResult.ExitCode);
            Assert.True(actionResult.ContinuedOnError);
            Assert.Equal("advisory failure", actionResult.StdErr);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void RunAction_ForwardsContextEnvironmentAndWorkingDirectory()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var scripts = Directory.CreateDirectory(Path.Combine(root.FullName, "Build"));
            var contextPath = Path.Combine(root.FullName, "action.context.json");
            var requests = new List<PowerShellRunRequest>();
            var runner = new RecordingPowerShellRunner(request =>
            {
                requests.Add(request);
                return new PowerShellRunResult(0, "ok", string.Empty, "pwsh.exe");
            });
            var operations = new PowerShellModulePipelineHostedOperations(runner, new NullLogger());
            var action = new ModulePipelineActionConfiguration
            {
                Name = "Release guard",
                At = ModulePipelineActionStage.BeforePublish,
                InlineScript = "Write-Output $env:POWERFORGE_CONTEXT",
                WorkingDirectory = ".\\Build",
                Environment = new Dictionary<string, string?> { ["CUSTOM_FLAG"] = "enabled" },
                TimeoutSeconds = 42,
                PreferWindowsPowerShell = true
            };
            var context = new ModulePipelineActionContext
            {
                Stage = ModulePipelineActionStage.BeforePublish,
                ActionName = "Release guard",
                ModuleName = "TestModule",
                ProjectRoot = root.FullName,
                StagingPath = Path.Combine(root.FullName, "staging"),
                ManifestPath = Path.Combine(root.FullName, "staging", "TestModule.psd1"),
                ResolvedVersion = "1.2.3"
            };

            var result = operations.RunAction(action, context, contextPath, root.FullName);

            var request = Assert.Single(requests);
            Assert.Equal(PowerShellInvocationMode.Command, request.InvocationMode);
            Assert.Equal(action.InlineScript, request.CommandText);
            Assert.Equal(TimeSpan.FromSeconds(42), request.Timeout);
            Assert.False(request.PreferPwsh);
            Assert.Equal(scripts.FullName, request.WorkingDirectory);
            Assert.NotNull(request.EnvironmentVariables);
            Assert.Equal("enabled", request.EnvironmentVariables!["CUSTOM_FLAG"]);
            Assert.Equal(contextPath, request.EnvironmentVariables["POWERFORGE_CONTEXT"]);
            Assert.Equal("BeforePublish", request.EnvironmentVariables["POWERFORGE_ACTION_STAGE"]);
            Assert.Equal("Release guard", request.EnvironmentVariables["POWERFORGE_ACTION_NAME"]);
            Assert.Equal("TestModule", request.EnvironmentVariables["POWERFORGE_MODULE_NAME"]);
            Assert.Equal(root.FullName, request.EnvironmentVariables["POWERFORGE_PROJECT_ROOT"]);
            Assert.Equal(context.StagingPath, request.EnvironmentVariables["POWERFORGE_STAGING_PATH"]);
            Assert.Equal(context.ManifestPath, request.EnvironmentVariables["POWERFORGE_MANIFEST_PATH"]);
            Assert.Equal("1.2.3", request.EnvironmentVariables["POWERFORGE_RESOLVED_VERSION"]);
            Assert.True(result.Succeeded);
            Assert.True(result.Inline);
            Assert.Equal("pwsh.exe", result.Executable);
            Assert.Equal(contextPath, result.ContextPath);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
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
    public void EnsureBuildDependenciesInstalledIfNeeded_IgnoresSourceManifestRequiredModules_WhenNoRequiredModuleIsConfigured()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            AddRequiredModuleToManifest(root.FullName, moduleName, "Manifest.Tools", "1.0.0");

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

            Assert.Empty(result);
            Assert.Equal(0, hostedOperations.DependencyInstallCalls);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_InstallsExternalModules_WhenNoRequiredModulesAreConfigured()
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
                        Kind = ModuleDependencyKind.ExternalModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "Az.Accounts"
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
            Assert.Equal("Az.Accounts", Assert.Single(hostedOperations.LastDependencies).Name);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_InstallsEmbeddedModules()
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
                        Kind = ModuleDependencyKind.EmbeddedModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "Microsoft.Graph.Authentication",
                            RequiredVersion = "2.25.0"
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
            var dependency = Assert.Single(hostedOperations.LastDependencies);
            Assert.Equal("Microsoft.Graph.Authentication", dependency.Name);
            Assert.Equal("2.25.0", dependency.RequiredVersion);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_PreservesEmbeddedVersionConstraintWhenRequiredModuleUsesDifferentVersion()
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
                            ModuleName = "Tools.Dependency",
                            RequiredVersion = "1.0.0"
                        }
                    },
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.EmbeddedModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "Tools.Dependency",
                            RequiredVersion = "2.0.0"
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
            InvokeEnsureBuildDependenciesInstalledIfNeeded(runner, plan);

            var requestedVersions = hostedOperations.LastDependencies
                .Where(static dependency => string.Equals(dependency.Name, "Tools.Dependency", StringComparison.OrdinalIgnoreCase))
                .Select(static dependency => dependency.RequiredVersion)
                .ToArray();
            Assert.Equal(new[] { "1.0.0", "2.0.0" }, requestedVersions);
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

            Assert.Equal(2, result.Length);
            Assert.Equal(2, hostedOperations.DependencyInstallCalls);
            Assert.Equal("Microsoft.PowerShell.PSResourceGet", Assert.Single(hostedOperations.DependencyCalls[0]).Name);
            var pester = Assert.Single(hostedOperations.LastDependencies, dependency => dependency.Name == "Pester");
            Assert.Equal("5.7.1", pester.MinimumVersion);
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

            Assert.Equal(2, result.Length);
            Assert.Equal(2, hostedOperations.DependencyInstallCalls);
            Assert.Equal("Microsoft.PowerShell.PSResourceGet", Assert.Single(hostedOperations.DependencyCalls[0]).Name);
            Assert.Equal("Pester", Assert.Single(hostedOperations.LastDependencies).Name);
            Assert.Null(hostedOperations.LastRepository);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_InstallsValidationTestSuiteTools()
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
                    new ConfigurationValidationSegment
                    {
                        Settings = new ModuleValidationSettings
                        {
                            Enable = true,
                            Tests = new TestSuiteValidationSettings
                            {
                                Enable = true,
                                TestPath = testsPath,
                                AdditionalModules = new[] { "Az.Accounts", "Pester", "PSWriteColor" }
                            }
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

            Assert.Equal(4, result.Length);
            Assert.Equal(2, hostedOperations.DependencyInstallCalls);
            Assert.Equal("Microsoft.PowerShell.PSResourceGet", Assert.Single(hostedOperations.DependencyCalls[0]).Name);
            var pester = Assert.Single(hostedOperations.LastDependencies, dependency => dependency.Name == "Pester");
            Assert.Equal("5.7.1", pester.MinimumVersion);
            Assert.Contains(hostedOperations.LastDependencies, dependency => dependency.Name == "Az.Accounts");
            Assert.Contains(hostedOperations.LastDependencies, dependency => dependency.Name == "PSWriteColor");
            Assert.Null(hostedOperations.LastRepository);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_SkipsRepositoryToolForSatisfiedTestsAfterMergeTool()
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
                new FakeMetadataProvider(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Pester"] = "5.7.1"
                }),
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
    public void EnsureBuildDependenciesInstalledIfNeeded_InstallsScriptAnalyzerBootstrapBeforeAnalyzerTool()
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
                    new ConfigurationValidationSegment
                    {
                        Settings = new ModuleValidationSettings
                        {
                            Enable = true,
                            ScriptAnalyzer = new ScriptAnalyzerValidationSettings
                            {
                                Enable = true,
                                InstallIfUnavailable = true,
                                Severity = ValidationSeverity.Warning
                            },
                            Tests = new TestSuiteValidationSettings { Severity = ValidationSeverity.Off }
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
            Assert.Equal("PSScriptAnalyzer", Assert.Single(hostedOperations.LastDependencies).Name);
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
    public void EnsureBuildDependenciesInstalledIfNeeded_SkipsRepositoryToolForManagedAutoPublishWhenNoPublishToolExists()
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

            Assert.Empty(result);
            Assert.Equal(0, hostedOperations.DependencyInstallCalls);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_SkipsRepositoryToolForManagedAutoPublishWhenPowerShellGetIsTooOld()
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

            Assert.Empty(result);
            Assert.Equal(0, hostedOperations.DependencyInstallCalls);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_InstallsRepositoryToolForAutoPublishWithRuntimeCredentialProvider()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var repository = new PublishRepositoryConfiguration
            {
                Name = "JFrog",
                Uri = "https://example.jfrog.io/artifactory/api/nuget/v3/feed",
                CredentialProvider = new RepositoryCredentialProviderConfiguration
                {
                    Kind = RepositoryCredentialProviderKind.JFrogOidc
                }
            };
            var spec = CreatePublishToolSpec(root.FullName, moduleName, PublishTool.Auto, repository: repository);
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
    public void RunPreflight_InstallsPSResourceGetForRepositorySourcedTransitiveMetadata()
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
                            ModuleName = "Parent.Tools",
                            ModuleVersion = "1.0.0",
                            Guid = "11111111-1111-1111-1111-111111111111",
                            VersionSource = ModuleDependencyVersionSource.PSGallery
                        }
                    }
                }
            };
            var hostedOperations = new FakeHostedOperations();
            var provider = new FakeMetadataProvider(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlyList<RequiredModuleReference>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Parent.Tools"] = new[]
                    {
                        new RequiredModuleReference("Child.Tools")
                    }
                });
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                provider,
                hostedOperations);

            InvokeEnsureRequiredModuleOnlineResolutionToolInstalledIfNeededForRun(runner, spec);

            Assert.Equal(1, hostedOperations.DependencyInstallCalls);
            Assert.Equal("Microsoft.PowerShell.PSResourceGet", Assert.Single(hostedOperations.LastDependencies).Name);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void RunPreflight_InstallsPSResourceGetForRepositorySourcedEmbeddedTransitiveMetadata()
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
                        Kind = ModuleDependencyKind.EmbeddedModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "Parent.Tools",
                            ModuleVersion = "1.0.0",
                            Guid = "11111111-1111-1111-1111-111111111111",
                            VersionSource = ModuleDependencyVersionSource.PSGallery
                        }
                    }
                }
            };
            var hostedOperations = new FakeHostedOperations();
            var provider = new FakeMetadataProvider(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlyList<RequiredModuleReference>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Parent.Tools"] = new[]
                    {
                        new RequiredModuleReference("Child.Tools")
                    }
                });
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                provider,
                hostedOperations);

            InvokeEnsureRequiredModuleOnlineResolutionToolInstalledIfNeededForRun(runner, spec);

            Assert.Equal(1, hostedOperations.DependencyInstallCalls);
            Assert.Equal("Microsoft.PowerShell.PSResourceGet", Assert.Single(hostedOperations.LastDependencies).Name);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void RunPreflight_RefreshesRepositorySourcedEmbeddedPrecomputedPlans()
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
                        Kind = ModuleDependencyKind.EmbeddedModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "Parent.Tools",
                            ModuleVersion = "1.0.0",
                            VersionSource = ModuleDependencyVersionSource.PSGallery
                        }
                    }
                }
            };
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, IReadOnlyList<RequiredModuleReference>>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Parent.Tools"] = new[]
                        {
                            new RequiredModuleReference("Child.Tools")
                        }
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
    public void RunPreflight_DocumentationGateDisablesRefreshOnlyBeforeOnlineToolCheck()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = CreateOnlineRequiredModuleSpec(
                root.FullName,
                moduleName,
                refreshPsd1Only: true,
                resolveMissingModulesOnline: true);
            spec.Segments = new IConfigurationSegment[]
            {
                new ConfigurationGateSegment
                {
                    Configuration = new GateConfiguration
                    {
                        Mode = ConfigurationGateMode.Documentation
                    }
                }
            }.Concat(spec.Segments).ToArray();

            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);

            InvokeEnsureRequiredModuleOnlineResolutionToolInstalledIfNeededForRun(runner, spec);

            Assert.Equal(1, hostedOperations.DependencyInstallCalls);
            Assert.Equal("Microsoft.PowerShell.PSResourceGet", Assert.Single(hostedOperations.LastDependencies).Name);
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
    public void Run_IgnoresManifestRequiredModuleOnlineResolution_WhenNoModuleDependencySegmentIsConfigured()
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

            runner.Run(spec);

            Assert.Equal(0, hostedOperations.DependencyInstallCalls);
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

            Assert.Equal(2, result.Length);
            Assert.Equal(2, hostedOperations.DependencyInstallCalls);
            Assert.Equal("Microsoft.PowerShell.PSResourceGet", Assert.Single(hostedOperations.DependencyCalls[0]).Name);
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
    public void EnsureBuildDependenciesInstalledIfNeeded_SkipsRepositoryToolForManagedModulePublish()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = CreatePublishToolSpec(root.FullName, moduleName, PublishTool.ManagedModule);
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
    public void EnsureBuildDependenciesInstalledIfNeeded_GatePublishInstallsToolForDisabledPublish()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = CreatePublishToolSpec(root.FullName, moduleName, PublishTool.PSResourceGet, ConfigurationGateMode.Publish, publishEnabled: false);
            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);

            var plan = runner.Plan(spec);
            var result = InvokeEnsureBuildDependenciesInstalledIfNeeded(runner, plan);

            Assert.Equal(ConfigurationGateMode.Publish, plan.GateMode);
            var publish = Assert.Single(plan.Publishes);
            Assert.True(publish.Configuration.Enabled);
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
    public void SigningScript_FailsClosedForInvalidCertificatesAndUnknownSignatures()
    {
        var script = EmbeddedScripts.Load("Scripts/Signing/Sign-Module.ps1");

        Assert.Contains("outside its validity period", script, StringComparison.Ordinal);
        Assert.Contains("if ($status -eq 'Valid')", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$status -eq 'Valid' -or $status -eq 'UnknownError'", script, StringComparison.Ordinal);
    }

    [Fact]
    public void SigningScript_AllowsTheFirstFailedFileToBeRecorded()
    {
        var script = EmbeddedScripts.Load("Scripts/Signing/Sign-Module.ps1");

        Assert.Contains("[AllowEmptyCollection()]", script, StringComparison.Ordinal);
        Assert.Contains("Add-FailedFile -List $failedFiles", script, StringComparison.Ordinal);
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

    private static ModulePipelineSpec CreatePublishToolSpec(
        string root,
        string moduleName,
        PublishTool tool,
        ConfigurationGateMode? gateMode = null,
        bool publishEnabled = true,
        PublishRepositoryConfiguration? repository = null)
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
            Segments = new IConfigurationSegment?[]
            {
                gateMode is null
                    ? null
                    : new ConfigurationGateSegment
                    {
                        Configuration = new GateConfiguration
                        {
                            Mode = gateMode.Value
                        }
                    },
                new ConfigurationPublishSegment
                {
                    Configuration = new PublishConfiguration
                    {
                        Enabled = publishEnabled,
                        Destination = PublishDestination.PowerShellGallery,
                        Tool = tool,
                        ApiKey = "test-api-key",
                        RepositoryName = repository?.Name,
                        Repository = repository
                    }
                }
            }.Where(static segment => segment is not null).Cast<IConfigurationSegment>().ToArray()
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
        private readonly IReadOnlyDictionary<string, IReadOnlyList<RequiredModuleReference>> _installedRequiredModules;
        private readonly bool _throwOnResolveLatestOnlineVersions;

        public FakeMetadataProvider()
        {
            _installedModuleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _installedModuleVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _installedRequiredModules = new Dictionary<string, IReadOnlyList<RequiredModuleReference>>(StringComparer.OrdinalIgnoreCase);
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
            _installedRequiredModules = new Dictionary<string, IReadOnlyList<RequiredModuleReference>>(StringComparer.OrdinalIgnoreCase);
        }

        public FakeMetadataProvider(IReadOnlyDictionary<string, string> installedModuleVersions)
        {
            _filterInstalledModules = true;
            _installedModuleVersions = installedModuleVersions ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _installedModuleNames = new HashSet<string>(_installedModuleVersions.Keys, StringComparer.OrdinalIgnoreCase);
            _installedRequiredModules = new Dictionary<string, IReadOnlyList<RequiredModuleReference>>(StringComparer.OrdinalIgnoreCase);
        }

        public FakeMetadataProvider(
            IReadOnlyDictionary<string, string> installedModuleVersions,
            IReadOnlyDictionary<string, IReadOnlyList<RequiredModuleReference>> installedRequiredModules)
            : this(installedModuleVersions)
        {
            _installedRequiredModules = installedRequiredModules ??
                new Dictionary<string, IReadOnlyList<RequiredModuleReference>>(StringComparer.OrdinalIgnoreCase);
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
            => _installedRequiredModules.TryGetValue(moduleName, out var modules)
                ? modules
                : Array.Empty<RequiredModuleReference>();

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
        public List<ModulePipelineActionContext> ActionContexts { get; } = new();
        public List<string> ActionContextPaths { get; } = new();
        public bool NextActionSucceeded { get; set; } = true;
        public int NextActionExitCode { get; set; }
        public string NextActionStdOut { get; set; } = string.Empty;
        public string NextActionStdErr { get; set; } = string.Empty;

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
            bool includeScriptFolders,
            Action? remotePublishAttempted,
            Action? remoteSideEffectObserved)
            => throw new InvalidOperationException("Not used in this test.");

        public void ValidateModuleImports(
            string manifestPath,
            ImportModuleEntry[] modules,
            bool importRequired,
            bool importSelf,
            bool verbose,
            ModuleImportValidationTarget[] targets)
            => throw new InvalidOperationException("Not used in this test.");

        public ModulePipelineActionResult RunAction(
            ModulePipelineActionConfiguration action,
            ModulePipelineActionContext context,
            string contextPath,
            string projectRoot)
        {
            ActionContexts.Add(context);
            ActionContextPaths.Add(contextPath);

            return new ModulePipelineActionResult
            {
                Name = string.IsNullOrWhiteSpace(action.Name) ? context.Stage.ToString() : action.Name!,
                Stage = context.Stage,
                Succeeded = NextActionSucceeded,
                ExitCode = NextActionExitCode,
                Executable = "fake-pwsh",
                FilePath = action.FilePath,
                Inline = !string.IsNullOrWhiteSpace(action.InlineScript),
                WorkingDirectory = projectRoot,
                ContextPath = contextPath,
                StdOut = NextActionStdOut,
                StdErr = NextActionStdErr
            };
        }

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
