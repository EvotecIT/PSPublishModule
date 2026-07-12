using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelinePackageBuildTests
{
    [Fact]
    public void ApplyPublishedNuGetArtifactOutcomes_PreservesMixedPrimaryAndSymbolResults()
    {
        var primary = Path.Combine(Path.GetTempPath(), "Sample.1.0.0.nupkg");
        var symbols = Path.Combine(Path.GetTempPath(), "Sample.1.0.0.snupkg");
        var release = new DotNetRepositoryReleaseResult();
        release.Projects.Add(new DotNetRepositoryProjectResult
        {
            Packages = { primary },
            SymbolPackages = { symbols }
        });
        var publish = new NuGetPackagePublishResult();
        publish.PublishedItems.Add(primary);
        publish.SkippedDuplicateItems.Add(primary);
        publish.PackagePushResults[primary] = new DotNetRepositoryReleaseService.PackagePushResult
        {
            Outcome = DotNetRepositoryReleaseService.PackagePushOutcome.SkippedDuplicate,
            Message = string.Join(Environment.NewLine, new[]
            {
                $"Pushing {Path.GetFileName(primary)}...",
                $"Package '{Path.GetFileName(primary)}' already exists and cannot be modified.",
                $"Pushing {Path.GetFileName(symbols)}...",
                "Your package was pushed."
            })
        };

        ModulePipelineRunner.ApplyPublishedNuGetArtifactOutcomes(release, publish);

        Assert.Equal(new[] { symbols }, release.PublishedPackages);
        Assert.Equal(new[] { primary }, release.SkippedDuplicatePackages);
    }

    [Fact]
    public void Run_DoesNotPublishPackageLaneWithoutExplicitPublishIntent()
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
                            BuildBeforeModule = true
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
            try { if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_GateBuild_BuildsPostModulePublishOnlyPackageLanesWithoutPublishing()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var stagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
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
                    "  \"PublishNuget\": true",
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
                packageBuildExecutor: (request, configuration, path) =>
                {
                    calls.Add(new PackageBuildCall(request, configuration, path));
                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        ConfigPath = path ?? request.ConfigPath,
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
                    new ConfigurationGateSegment
                    {
                        Configuration = new GateConfiguration
                        {
                            Mode = ConfigurationGateMode.Build
                        }
                    },
                    new ConfigurationProjectBuildSegment
                    {
                        Configuration = new ProjectBuildConfigurationReference
                        {
                            Name = "JsonPackages",
                            ConfigPath = Path.Combine("Build", "project.build.json")
                        }
                    },
                    new ConfigurationPackageBuildSegment
                    {
                        Configuration = new PackageBuildConfiguration
                        {
                            Name = "InlinePackages",
                            RootPath = "Sources",
                            PublishGitHub = true
                        }
                    }
                }
            };

            var result = runner.Run(spec);

            Assert.Equal(2, calls.Count);
            Assert.Equal(2, result.ProjectBuildResults.Length);
            foreach (var call in calls)
            {
                Assert.True(call.Request.UpdateVersions);
                Assert.True(call.Request.Build);
                Assert.False(call.Request.PublishNuget);
                Assert.False(call.Request.PublishGitHub);
            }
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_HonorsReleaseBuildOrderForPackageBuildPlacement()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var stagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var packageRanBeforeStaging = false;
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
                    packageRanBeforeStaging = !Directory.Exists(stagingPath);
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
                            Build = true,
                            PublishNuget = false,
                            PublishGitHub = false
                        }
                    },
                    new ConfigurationReleaseSegment
                    {
                        Configuration = new ReleaseConfiguration
                        {
                            BuildOrder = new[] { "PackageBuild", "Module" }
                        }
                    }
                }
            };

            runner.Run(spec);

            Assert.True(packageRanBeforeStaging);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_RejectsLateLocalNuGetFeedPackageLane()
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
                packageBuildExecutor: (request, configuration, configPath) => throw new InvalidOperationException("Package build should not run."));

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
                            ProvideLocalNuGetFeed = true
                        }
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));

            Assert.Contains("ProvideLocalNuGetFeed", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("must run before the module build", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_AllowsLatePackageReleaseVersionSourceLane()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var packageBuildCalls = 0;
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
                    packageBuildCalls++;
                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        ConfigPath = configPath ?? request.ConfigPath,
                        RootPath = root.FullName,
                        Result = new ProjectBuildResult
                        {
                            Success = true,
                            Release = new DotNetRepositoryReleaseResult
                            {
                                Success = true,
                                ResolvedVersion = "3.4.5"
                            }
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
                            RootPath = "Sources",
                            UseAsReleaseVersionSource = true
                        }
                    },
                    new ConfigurationReleaseSegment
                    {
                        Configuration = new ReleaseConfiguration
                        {
                            StageRoot = Path.Combine(root.FullName, "Artifacts", "Unified"),
                            VersionSource = ReleaseVersionSource.PackageBuild
                        }
                    }
                }
            };

            var result = runner.Run(spec);

            Assert.Equal(1, packageBuildCalls);
            Assert.NotNull(result.ReleaseCoordinationResult);
            Assert.Equal("1.0.0", result.ReleaseCoordinationResult!.ModuleVersion);
            Assert.Equal("3.4.5", result.ReleaseCoordinationResult.ReleaseVersion);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
