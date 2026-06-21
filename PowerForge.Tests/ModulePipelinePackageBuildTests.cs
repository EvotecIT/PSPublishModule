using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelinePackageBuildTests
{
    [Fact]
    public void Run_ExecutesPackageBuildsBeforeModuleStaging()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            WriteProjectBuildConfig(root.FullName, Path.Combine("Build", "project.build.json"));

            var calls = new List<PackageBuildCall>();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: null,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: (request, configuration, configPath) =>
                {
                    calls.Add(new PackageBuildCall(request, configuration, configPath));
                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        ConfigPath = configPath ?? request.ConfigPath,
                        RootPath = root.FullName,
                        Result = new ProjectBuildResult { Success = true }
                    };
                });

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
                    new ConfigurationProjectBuildSegment
                    {
                        Configuration = new ProjectBuildConfigurationReference
                        {
                            Name = "JsonPackages",
                            ConfigPath = Path.Combine("Build", "project.build.json"),
                            BuildBeforeModule = true
                        }
                    },
                    new ConfigurationPackageBuildSegment
                    {
                        Configuration = new PackageBuildConfiguration
                        {
                            Name = "InlinePackages",
                            RootPath = "Sources",
                            ExpectedVersionMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["HtmlTinkerX"] = "2.0.X"
                            },
                            BuildBeforeModule = true,
                            Build = true,
                            PublishNuget = false,
                            PublishGitHub = false,
                            UseGitHubPackages = true,
                            GitHubPackagesOwner = "EvotecIT",
                            GitHubIncludeProjectNameInTag = false
                        }
                    }
                }
            };

            var plan = runner.Plan(spec);
            var reporter = new RecordingProgressReporter();
            var result = runner.Run(spec, plan, reporter);

            Assert.Equal(2, calls.Count);
            Assert.Equal(2, result.ProjectBuildResults.Length);
            Assert.Null(calls[0].Configuration);
            Assert.EndsWith(Path.Combine("Build", "project.build.json"), calls[0].Request.ConfigPath, StringComparison.OrdinalIgnoreCase);
            Assert.True(calls[0].Request.Build);
            Assert.False(calls[0].Request.PublishNuget);
            Assert.False(calls[0].Request.PublishGitHub);
            Assert.NotNull(calls[1].Configuration);
            var inlineConfiguration = calls[1].Configuration!;
            Assert.Equal(Path.Combine(root.FullName, "Sources"), inlineConfiguration.RootPath);
            Assert.Equal("2.0.X", inlineConfiguration.ExpectedVersionMap?["HtmlTinkerX"]);
            Assert.True(calls[1].Request.Build);
            Assert.False(calls[1].Request.PublishNuget);
            Assert.False(calls[1].Request.PublishGitHub);
            Assert.True(inlineConfiguration.UseGitHubPackages);
            Assert.Equal("EvotecIT", inlineConfiguration.GitHubPackagesOwner);
            Assert.False(inlineConfiguration.GitHubIncludeProjectNameInTag);

            var projectPackageIndex = reporter.StartedKeys.IndexOf("package:project:01");
            var inlinePackageIndex = reporter.StartedKeys.IndexOf("package:inline:01");
            var stageIndex = reporter.StartedKeys.IndexOf("build:stage");
            Assert.True(projectPackageIndex >= 0);
            Assert.True(inlinePackageIndex >= 0);
            Assert.True(stageIndex >= 0);
            Assert.True(projectPackageIndex < stageIndex);
            Assert.True(inlinePackageIndex < stageIndex);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_AppliesDslOverridesToReferencedProjectBuildConfig()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var configPath = Path.Combine(root.FullName, "Build", "project.build.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(
                configPath,
                string.Join(Environment.NewLine, new[]
                {
                    "{",
                    "  \"RootPath\": \"Sources\",",
                    "  \"Build\": false,",
                    "  \"PublishNuget\": true,",
                    "  \"PublishGitHub\": true,",
                    "  \"CreateReleaseZip\": true",
                    "}"
                }));

            var calls = new List<PackageBuildCall>();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: null,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: (request, configuration, configPath) =>
                {
                    calls.Add(new PackageBuildCall(request, configuration, configPath));
                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        ConfigPath = configPath ?? request.ConfigPath,
                        RootPath = root.FullName,
                        Result = new ProjectBuildResult { Success = true }
                    };
                });

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
                    new ConfigurationProjectBuildSegment
                    {
                        Configuration = new ProjectBuildConfigurationReference
                        {
                            Name = "JsonPackages",
                            ConfigPath = Path.Combine("Build", "project.build.json"),
                            BuildBeforeModule = true,
                            Build = true,
                            PublishNuget = false,
                            PublishGitHub = false,
                            CreateReleaseZip = false,
                            Options = new Dictionary<string, object?>
                            {
                                ["StagingPath"] = Path.Combine("Artifacts", "MergedProjectBuild")
                            }
                        }
                    }
                }
            };

            var plan = runner.Plan(spec);
            _ = runner.Run(spec, plan, new RecordingProgressReporter());

            var call = Assert.Single(calls);
            Assert.True(call.Request.Build);
            Assert.False(call.Request.PublishNuget);
            Assert.False(call.Request.PublishGitHub);
            Assert.NotNull(call.Configuration);
            Assert.False(call.Configuration!.CreateReleaseZip);
            Assert.Equal(Path.Combine("Artifacts", "MergedProjectBuild"), call.Configuration.StagingPath);
            Assert.EndsWith(Path.Combine("Build", "project.build.json"), call.ConfigPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_PreservesDefaultProjectBuildActionsWhenOneDslActionIsOverridden()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var configPath = Path.Combine(root.FullName, "Build", "project.build.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(
                configPath,
                string.Join(Environment.NewLine, new[]
                {
                    "{",
                    "  \"RootPath\": \"Sources\"",
                    "}"
                }));

            var calls = new List<PackageBuildCall>();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: null,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: (request, configuration, configPath) =>
                {
                    calls.Add(new PackageBuildCall(request, configuration, configPath));
                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        ConfigPath = configPath ?? request.ConfigPath,
                        RootPath = root.FullName,
                        Result = new ProjectBuildResult { Success = true }
                    };
                });

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
                    new ConfigurationProjectBuildSegment
                    {
                        Configuration = new ProjectBuildConfigurationReference
                        {
                            Name = "JsonPackages",
                            ConfigPath = Path.Combine("Build", "project.build.json"),
                            BuildBeforeModule = true,
                            PublishNuget = false
                        }
                    }
                }
            };

            runner.Run(spec);

            var call = Assert.Single(calls);
            Assert.True(call.Request.UpdateVersions);
            Assert.True(call.Request.Build);
            Assert.False(call.Request.PublishNuget);
            Assert.False(call.Request.PublishGitHub);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_ConvertsProjectBuildOptionsHashtableMaps()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            WriteProjectBuildConfig(root.FullName, Path.Combine("Build", "project.build.json"));

            var calls = new List<PackageBuildCall>();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: null,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: (request, configuration, configPath) =>
                {
                    calls.Add(new PackageBuildCall(request, configuration, configPath));
                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        ConfigPath = configPath ?? request.ConfigPath,
                        RootPath = root.FullName,
                        Result = new ProjectBuildResult { Success = true }
                    };
                });

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
                    new ConfigurationProjectBuildSegment
                    {
                        Configuration = new ProjectBuildConfigurationReference
                        {
                            Name = "JsonPackages",
                            ConfigPath = Path.Combine("Build", "project.build.json"),
                            BuildBeforeModule = true,
                            Options = new Dictionary<string, object?>
                            {
                                ["ExpectedVersionMap"] = new System.Collections.Hashtable
                                {
                                    ["HtmlTinkerX"] = "2.0.X"
                                },
                                ["VersionTracks"] = new System.Collections.Hashtable
                                {
                                    ["Core"] = new System.Collections.Hashtable
                                    {
                                        ["ExpectedVersion"] = "2.0.X",
                                        ["Projects"] = new[] { "HtmlTinkerX" },
                                        ["IncludePrerelease"] = true
                                    }
                                }
                            }
                        }
                    }
                }
            };

            runner.Run(spec);

            var call = Assert.Single(calls);
            Assert.NotNull(call.Configuration);
            Assert.Equal("2.0.X", call.Configuration!.ExpectedVersionMap?["HtmlTinkerX"]);
            Assert.NotNull(call.Configuration.VersionTracks);
            var track = call.Configuration.VersionTracks!["Core"];
            Assert.Equal("2.0.X", track.ExpectedVersion);
            Assert.Equal(new[] { "HtmlTinkerX" }, track.Projects);
            Assert.True(track.IncludePrerelease);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_PacksPublishOnlyDependencyPackageLaneForLocalFeed()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var packageOutput = Path.Combine(root.FullName, "Artifacts", "NuGet");
            var packagePath = Path.Combine(packageOutput, "HtmlTinkerX.2.0.1.nupkg");
            var calls = new List<PackageBuildCall>();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: null,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: (request, configuration, configPath) =>
                {
                    calls.Add(new PackageBuildCall(request, configuration, configPath));

                    if (request.Build == true)
                    {
                        Directory.CreateDirectory(packageOutput);
                        File.WriteAllText(packagePath, "package");
                    }

                    var release = new DotNetRepositoryReleaseResult { Success = true };
                    var project = new DotNetRepositoryProjectResult
                    {
                        ProjectName = "HtmlTinkerX",
                        PackageId = "HtmlTinkerX",
                        IsPackable = true,
                        NewVersion = "2.0.1"
                    };
                    if (File.Exists(packagePath))
                        project.Packages.Add(packagePath);
                    release.Projects.Add(project);

                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        ConfigPath = configPath ?? request.ConfigPath,
                        RootPath = root.FullName,
                        OutputPath = packageOutput,
                        Result = new ProjectBuildResult
                        {
                            Success = true,
                            Release = release
                        }
                    };
                });

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
                    new ConfigurationPackageBuildSegment
                    {
                        Configuration = new PackageBuildConfiguration
                        {
                            Name = "InlinePackages",
                            RootPath = "Sources",
                            BuildBeforeModule = true,
                            ProvideLocalNuGetFeed = true,
                            PublishNuget = true
                        }
                    }
                }
            };

            var plan = runner.Plan(spec);
            var result = runner.Run(spec, plan, new RecordingProgressReporter());

            Assert.Equal(2, calls.Count);
            Assert.True(calls[0].Request.UpdateVersions);
            Assert.True(calls[0].Request.Build);
            Assert.False(calls[0].Request.PublishNuget);
            Assert.False(calls[0].Request.PublishGitHub);
            Assert.False(calls[1].Request.Build);
            Assert.True(calls[1].Request.PublishNuget);
            Assert.Equal(new[] { Path.GetFullPath(packageOutput) }, plan.BuildSpec.NuGetRestoreSources);
            Assert.Equal(plan.BuildSpec.NuGetRestoreSources, result.Plan.BuildSpec.NuGetRestoreSources);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_ResolvesInlinePackageBuildPathsFromModuleRoot()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var calls = new List<PackageBuildCall>();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: null,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: (request, configuration, configPath) =>
                {
                    calls.Add(new PackageBuildCall(request, configuration, configPath));
                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        ConfigPath = configPath ?? request.ConfigPath,
                        RootPath = root.FullName,
                        Result = new ProjectBuildResult { Success = true }
                    };
                });

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
                    new ConfigurationPackageBuildSegment
                    {
                        Configuration = new PackageBuildConfiguration
                        {
                            Name = "InlinePackages",
                            RootPath = "Sources",
                            StagingPath = Path.Combine("Artifacts", "ProjectBuild"),
                            OutputPath = Path.Combine("Artifacts", "NuGet"),
                            ReleaseZipOutputPath = Path.Combine("Artifacts", "Releases"),
                            PlanOutputPath = Path.Combine("Artifacts", "plan.json"),
                            BuildBeforeModule = true,
                            Build = true,
                            PublishNuget = false,
                            PublishGitHub = false
                        }
                    }
                }
            };

            runner.Run(spec);

            var call = Assert.Single(calls);
            Assert.NotNull(call.Configuration);
            var configuration = call.Configuration!;
            Assert.Equal(Path.Combine(root.FullName, "Sources"), configuration.RootPath);
            Assert.Equal(Path.Combine(root.FullName, "Artifacts", "ProjectBuild"), configuration.StagingPath);
            Assert.Equal(Path.Combine(root.FullName, "Artifacts", "NuGet"), configuration.OutputPath);
            Assert.Equal(Path.Combine(root.FullName, "Artifacts", "Releases"), configuration.ReleaseZipOutputPath);
            Assert.Equal(Path.Combine(root.FullName, "Artifacts", "plan.json"), configuration.PlanOutputPath);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_DefersPackagePublishActionsUntilPublishPhase()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var stagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var calls = new List<PackageBuildCall>();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: null,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: (request, configuration, configPath) =>
                {
                    calls.Add(new PackageBuildCall(request, configuration, configPath));
                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        ConfigPath = configPath ?? request.ConfigPath,
                        RootPath = root.FullName,
                        Result = new ProjectBuildResult { Success = true }
                    };
                });

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0",
                    StagingPath = stagingPath
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationPackageBuildSegment
                    {
                        Configuration = new PackageBuildConfiguration
                        {
                            Name = "InlinePackages",
                            RootPath = "Sources",
                            BuildBeforeModule = true,
                            Build = true,
                            PublishNuget = true,
                            PublishGitHub = true
                        }
                    }
                }
            };

            var result = runner.Run(spec);

            Assert.Equal(3, calls.Count);
            Assert.Single(result.ProjectBuildResults);

            Assert.True(calls[0].Request.Build);
            Assert.False(calls[0].Request.PublishNuget);
            Assert.False(calls[0].Request.PublishGitHub);

            Assert.False(calls[1].Request.UpdateVersions);
            Assert.False(calls[1].Request.Build);
            Assert.True(calls[1].Request.PublishNuget);
            Assert.False(calls[1].Request.PublishGitHub);

            Assert.False(calls[2].Request.UpdateVersions);
            Assert.False(calls[2].Request.Build);
            Assert.False(calls[2].Request.PublishNuget);
            Assert.True(calls[2].Request.PublishGitHub);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_ExecutesPostModulePackageBuildsBeforeUnifiedReleaseStaging()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var stagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var moduleArtefactRoot = Path.Combine(root.FullName, "Artifacts", "Module");
            var packageOutput = Path.Combine(root.FullName, "Artifacts", "NuGet");
            var packagePath = Path.Combine(packageOutput, "HtmlTinkerX.2.0.1.nupkg");
            var sawModuleArtefactBeforePackageBuild = false;

            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: null,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: (request, configuration, configPath) =>
                {
                    sawModuleArtefactBeforePackageBuild =
                        Directory.Exists(moduleArtefactRoot) &&
                        Directory.EnumerateFiles(moduleArtefactRoot, "*.zip", SearchOption.AllDirectories).Any();

                    Directory.CreateDirectory(packageOutput);
                    File.WriteAllText(packagePath, "package");

                    var release = new DotNetRepositoryReleaseResult { Success = true };
                    var project = new DotNetRepositoryProjectResult
                    {
                        ProjectName = "HtmlTinkerX",
                        PackageId = "HtmlTinkerX",
                        IsPackable = true,
                        NewVersion = "2.0.1"
                    };
                    project.Packages.Add(packagePath);
                    release.Projects.Add(project);

                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        ConfigPath = configPath ?? request.ConfigPath,
                        RootPath = root.FullName,
                        OutputPath = packageOutput,
                        Result = new ProjectBuildResult
                        {
                            Success = true,
                            Release = release
                        }
                    };
                });

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0",
                    StagingPath = stagingPath
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Packed,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = moduleArtefactRoot,
                            ID = "module"
                        }
                    },
                    new ConfigurationPackageBuildSegment
                    {
                        Configuration = new PackageBuildConfiguration
                        {
                            Name = "InlinePackages",
                            RootPath = "Sources",
                            Build = true,
                            PublishNuget = false,
                            PublishGitHub = false
                        }
                    },
                    new ConfigurationReleaseSegment
                    {
                        Configuration = new ReleaseConfiguration
                        {
                            StageRoot = Path.Combine(root.FullName, "Artifacts", "Unified", "<ModuleName>", "<ModuleVersion>")
                        }
                    }
                }
            };

            var result = runner.Run(spec);

            Assert.True(sawModuleArtefactBeforePackageBuild);
            Assert.Single(result.ProjectBuildResults);
            Assert.NotNull(result.ReleaseCoordinationResult);
            Assert.Contains(result.ReleaseCoordinationResult!.PackageAssetPaths, path => string.Equals(path, Path.Combine(root.FullName, "Artifacts", "Unified", moduleName, "1.0.0", "nuget", Path.GetFileName(packagePath)), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_StopsBeforeModuleStagingWhenPackageBuildFails()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: null,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: (request, configuration, configPath) => new ProjectBuildHostExecutionResult
                {
                    Success = false,
                    ErrorMessage = "package build failed",
                    ConfigPath = configPath ?? request.ConfigPath,
                    RootPath = root.FullName,
                    Result = new ProjectBuildResult
                    {
                        Success = false,
                        ErrorMessage = "package build failed"
                    }
                });

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
                    new ConfigurationPackageBuildSegment
                    {
                        Configuration = new PackageBuildConfiguration
                        {
                            RootPath = "Sources",
                            BuildBeforeModule = true
                        }
                    }
                }
            };

            var plan = runner.Plan(spec);
            var reporter = new RecordingProgressReporter();
            var ex = Assert.Throws<InvalidOperationException>(() => runner.Run(spec, plan, reporter));

            Assert.Contains("package build failed", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("package:inline:01", reporter.StartedKeys);
            Assert.Contains("package:inline:01", reporter.FailedKeys);
            Assert.DoesNotContain("build:stage", reporter.StartedKeys);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_AddsProvidedPackageOutputsAsModuleRestoreSources()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var packageOutput = Path.Combine(root.FullName, "Artifacts", "NuGet");
            var packagePath = Path.Combine(packageOutput, "HtmlTinkerX.2.0.1.nupkg");

            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: null,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: (request, configuration, configPath) =>
                {
                    Directory.CreateDirectory(packageOutput);
                    File.WriteAllText(packagePath, "package");

                    var release = new DotNetRepositoryReleaseResult { Success = true };
                    release.Projects.Add(new DotNetRepositoryProjectResult
                    {
                        ProjectName = "HtmlTinkerX",
                        PackageId = "HtmlTinkerX",
                        IsPackable = true
                    });
                    release.Projects[0].Packages.Add(packagePath);

                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        ConfigPath = configPath ?? request.ConfigPath,
                        RootPath = root.FullName,
                        OutputPath = packageOutput,
                        Result = new ProjectBuildResult
                        {
                            Success = true,
                            Release = release
                        }
                    };
                });

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
                    new ConfigurationPackageBuildSegment
                    {
                        Configuration = new PackageBuildConfiguration
                        {
                            Name = "InlinePackages",
                            RootPath = "Sources",
                            BuildBeforeModule = true,
                            ProvideLocalNuGetFeed = true
                        }
                    }
                }
            };

            var plan = runner.Plan(spec);
            var result = runner.Run(spec, plan, new RecordingProgressReporter());

            Assert.Equal(new[] { Path.GetFullPath(packageOutput) }, plan.BuildSpec.NuGetRestoreSources);
            Assert.Equal(plan.BuildSpec.NuGetRestoreSources, result.Plan.BuildSpec.NuGetRestoreSources);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_FailsWhenProvidedLocalNuGetFeedHasNoPackages()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: null,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: (request, configuration, configPath) => new ProjectBuildHostExecutionResult
                {
                    Success = true,
                    ConfigPath = configPath ?? request.ConfigPath,
                    RootPath = root.FullName,
                    OutputPath = Path.Combine(root.FullName, "Artifacts", "NuGet"),
                    Result = new ProjectBuildResult { Success = true }
                });

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
                    new ConfigurationPackageBuildSegment
                    {
                        Configuration = new PackageBuildConfiguration
                        {
                            RootPath = "Sources",
                            BuildBeforeModule = true,
                            ProvideLocalNuGetFeed = true
                        }
                    }
                }
            };

            var plan = runner.Plan(spec);
            var reporter = new RecordingProgressReporter();
            var ex = Assert.Throws<InvalidOperationException>(() => runner.Run(spec, plan, reporter));

            Assert.Contains("ProvideLocalNuGetFeed", ex.Message, StringComparison.Ordinal);
            Assert.Contains("package:inline:01", reporter.FailedKeys);
            Assert.DoesNotContain("build:stage", reporter.StartedKeys);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
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

    private static void WriteProjectBuildConfig(string rootPath, string relativePath)
    {
        var path = Path.Combine(rootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            string.Join(Environment.NewLine, new[]
            {
                "{",
                "  \"RootPath\": \"Sources\",",
                "  \"Build\": true,",
                "  \"PublishNuget\": false,",
                "  \"PublishGitHub\": false",
                "}"
            }));
    }

    private sealed record PackageBuildCall(
        ProjectBuildHostRequest Request,
        ProjectBuildConfiguration? Configuration,
        string? ConfigPath);

    private sealed class RecordingProgressReporter : IModulePipelineProgressReporter
    {
        public List<string> StartedKeys { get; } = new();
        public List<string> FailedKeys { get; } = new();

        public void StepStarting(ModulePipelineStep step)
        {
            StartedKeys.Add(step.Key);
        }

        public void StepCompleted(ModulePipelineStep step)
        {
        }

        public void StepFailed(ModulePipelineStep step, Exception error)
        {
            FailedKeys.Add(step.Key);
        }
    }
}
