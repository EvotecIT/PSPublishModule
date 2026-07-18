using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineUnifiedReleaseTests
{
    [Fact]
    public void Run_PublishesGitHubReleaseWithStagedModuleAndPackageAssets()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.2.3");

            var packageOutput = Path.Combine(root.FullName, "Artifacts", "NuGet");
            var packagePath = Path.Combine(packageOutput, "HtmlTinkerX.2.0.1-beta1.nupkg");
            var symbolPackagePath = Path.Combine(packageOutput, "HtmlTinkerX.2.0.1-beta1.snupkg");
            GitHubReleasePublishRequest? gitHubRequest = null;

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
                    File.WriteAllText(symbolPackagePath, "symbols");

                    var release = new DotNetRepositoryReleaseResult { Success = true };
                    var project = new DotNetRepositoryProjectResult
                    {
                        ProjectName = "HtmlTinkerX",
                        PackageId = "HtmlTinkerX",
                        IsPackable = true,
                        NewVersion = "2.0.1-beta1+build.7"
                    };
                    project.Packages.Add(packagePath);
                    project.SymbolPackages.Add(symbolPackagePath);
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
                },
                gitHubReleasePublisher: request =>
                {
                    gitHubRequest = request;
                    return new GitHubReleasePublishResult
                    {
                        Succeeded = true,
                        ReleaseCreationSucceeded = true,
                        AllAssetUploadsSucceeded = true,
                        HtmlUrl = "https://github.com/EvotecIT/TestModule/releases/tag/v1.2.3"
                    };
                });

            var stageRoot = Path.Combine(root.FullName, "Artifacts", "Unified", "<ModuleName>", "<ModuleVersion>");
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.2.3"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationPackageBuildSegment
                    {
                        Configuration = new PackageBuildConfiguration
                        {
                            Name = "Packages",
                            RootPath = "Sources",
                            BuildBeforeModule = true,
                            IncludeSymbols = true
                        }
                    },
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Packed,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = Path.Combine(root.FullName, "Artifacts", "Module"),
                            ID = "module"
                        }
                    },
                    new ConfigurationReleaseSegment
                    {
                        Configuration = new ReleaseConfiguration
                        {
                            StageRoot = stageRoot,
                            VersionSource = ReleaseVersionSource.PackageBuild,
                            PrimaryProject = "HtmlTinkerX"
                        }
                    },
                    new ConfigurationPublishSegment
                    {
                        Configuration = new PublishConfiguration
                        {
                            Enabled = true,
                            Destination = PublishDestination.GitHub,
                            UserName = "EvotecIT",
                            RepositoryName = "TestModule",
                            ApiKey = "token",
                            GenerateReleaseNotes = true
                        }
                    }
                }
            };

            var result = runner.Run(spec);

            Assert.NotNull(gitHubRequest);
            Assert.Equal("EvotecIT", gitHubRequest!.Owner);
            Assert.Equal("TestModule", gitHubRequest.Repository);
            Assert.Equal("v1.2.3", gitHubRequest.TagName);
            Assert.False(gitHubRequest.IsPreRelease);
            Assert.True(gitHubRequest.GenerateReleaseNotes);
            Assert.Equal("1.2.3", result.Plan.ResolvedVersion);
            Assert.Null(result.Plan.PreRelease);
            Assert.Equal("1.2.3", result.Plan.BuildSpec.Version);
            var projectManifestPath = Path.Combine(root.FullName, $"{moduleName}.psd1");
            var projectManifest = File.ReadAllText(projectManifestPath);
            Assert.Contains("ModuleVersion = '1.2.3'", projectManifest, StringComparison.Ordinal);
            Assert.False(ManifestEditor.TryGetTopLevelString(projectManifestPath, "Prerelease", out _));
            Assert.False(ManifestEditor.TryGetPsDataStringArray(projectManifestPath, "Prerelease", out _));

            var resolvedStageRoot = Path.Combine(root.FullName, "Artifacts", "Unified", moduleName, "1.2.3");
            Assert.NotNull(result.ReleaseCoordinationResult);
            Assert.Equal(Path.GetFullPath(resolvedStageRoot), result.ReleaseCoordinationResult!.StageRoot);
            Assert.Equal("1.2.3", result.ReleaseCoordinationResult.ModuleVersion);
            Assert.Equal("2.0.1-beta1+build.7", result.ReleaseCoordinationResult.ReleaseVersion);
            Assert.Equal(5, gitHubRequest.AssetFilePaths.Count);
            Assert.Contains(gitHubRequest.AssetFilePaths, path => path.StartsWith(Path.Combine(resolvedStageRoot, "modules"), StringComparison.OrdinalIgnoreCase));
            Assert.Contains(gitHubRequest.AssetFilePaths, path => string.Equals(path, Path.Combine(resolvedStageRoot, "nuget", Path.GetFileName(packagePath)), StringComparison.OrdinalIgnoreCase));
            Assert.Contains(gitHubRequest.AssetFilePaths, path => string.Equals(path, Path.Combine(resolvedStageRoot, "nuget", Path.GetFileName(symbolPackagePath)), StringComparison.OrdinalIgnoreCase));
            Assert.Contains(gitHubRequest.AssetFilePaths, path => string.Equals(path, Path.Combine(resolvedStageRoot, "metadata", "release-manifest.json"), StringComparison.OrdinalIgnoreCase));
            Assert.Contains(gitHubRequest.AssetFilePaths, path => string.Equals(path, Path.Combine(resolvedStageRoot, "metadata", "SHA256SUMS.txt"), StringComparison.OrdinalIgnoreCase));
            Assert.All(gitHubRequest.AssetFilePaths, path => Assert.True(File.Exists(path), path));
            var manifestJson = File.ReadAllText(Path.Combine(resolvedStageRoot, "metadata", "release-manifest.json"));
            using var releaseManifest = JsonDocument.Parse(manifestJson);
            Assert.Equal("1.2.3", releaseManifest.RootElement.GetProperty("version").GetString());
            Assert.Equal("1.2.3", releaseManifest.RootElement.GetProperty("moduleVersion").GetString());
            Assert.Equal("2.0.1-beta1+build.7", releaseManifest.RootElement.GetProperty("releaseVersion").GetString());
            Assert.Contains("HtmlTinkerX.2.0.1-beta1.nupkg", manifestJson, StringComparison.Ordinal);
            Assert.Contains("HtmlTinkerX.2.0.1-beta1.snupkg", manifestJson, StringComparison.Ordinal);
            Assert.Contains("nuget/HtmlTinkerX.2.0.1-beta1.nupkg", File.ReadAllText(Path.Combine(resolvedStageRoot, "metadata", "SHA256SUMS.txt")), StringComparison.Ordinal);
            Assert.Contains("nuget/HtmlTinkerX.2.0.1-beta1.snupkg", File.ReadAllText(Path.Combine(resolvedStageRoot, "metadata", "SHA256SUMS.txt")), StringComparison.Ordinal);
            Assert.Single(result.PublishResults);
            Assert.Equal(gitHubRequest.AssetFilePaths.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase), result.PublishResults[0].AssetPaths.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_DoesNotUseExplicitPackageLaneAsModuleVersionSourceWithoutReleaseSegment()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var stagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
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
                    Result = new ProjectBuildResult
                    {
                        Success = true,
                        Release = new DotNetRepositoryReleaseResult
                        {
                            Success = true,
                            ResolvedVersion = "3.4.5"
                        }
                    }
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
                            Name = "Packages",
                            RootPath = "Sources",
                            BuildBeforeModule = true,
                            UseAsReleaseVersionSource = true
                        }
                    }
                }
            };

            var result = runner.Run(spec);

            Assert.Equal("1.0.0", result.Plan.ResolvedVersion);
            Assert.Equal("1.0.0", result.Plan.BuildSpec.Version);
            Assert.Contains("ModuleVersion = '1.0.0'", File.ReadAllText(result.BuildResult.ManifestPath), StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_UsesExplicitPackageLaneAsDefaultReleaseVersionSourceWithoutChangingModuleVersion()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var stagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
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
                    Result = new ProjectBuildResult
                    {
                        Success = true,
                        Release = new DotNetRepositoryReleaseResult
                        {
                            Success = true,
                            ResolvedVersion = "3.4.5"
                        }
                    }
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
                            Name = "Packages",
                            RootPath = "Sources",
                            BuildBeforeModule = true,
                            UseAsReleaseVersionSource = true
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

            Assert.Equal("1.0.0", result.Plan.ResolvedVersion);
            Assert.Equal("1.0.0", result.Plan.BuildSpec.Version);
            Assert.Contains("ModuleVersion = '1.0.0'", File.ReadAllText(result.BuildResult.ManifestPath), StringComparison.Ordinal);
            Assert.NotNull(result.ReleaseCoordinationResult);
            Assert.Equal("1.0.0", result.ReleaseCoordinationResult!.ModuleVersion);
            Assert.Equal("3.4.5", result.ReleaseCoordinationResult.ReleaseVersion);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_ValidatesReleaseVersionSourceBeforeModulePublish()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var stagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var hostedOperations = new FakeHostedOperations(new List<string>());
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: hostedOperations,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null);

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
                    new ConfigurationReleaseSegment
                    {
                        Configuration = new ReleaseConfiguration
                        {
                            StageRoot = Path.Combine(root.FullName, "Artifacts", "Unified"),
                            VersionSource = ReleaseVersionSource.PackageBuild
                        }
                    },
                    new ConfigurationPublishSegment
                    {
                        Configuration = new PublishConfiguration
                        {
                            Enabled = true,
                            Destination = PublishDestination.PowerShellGallery,
                            RepositoryName = "PSGallery",
                            ApiKey = "gallery-token"
                        }
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));

            Assert.Contains("Release version source 'PackageBuild'", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(hostedOperations.PublishedModuleVersions);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(false, "2.1.6", "2.0.11", "2.1.6", null, false)]
    [InlineData(true, "2.0.10", "2.0.11", "2.0.11", null, false)]
    [InlineData(true, "2.0.10", "2.0.11-beta.2", "2.0.11", "beta.2", false)]
    [InlineData(true, "2.0.10", "2.0.12", "2.0.12", null, true)]
    public void Run_UsesProjectBuildReleaseVersionWithOptInModuleSynchronization(
        bool synchronizeModuleVersion,
        string moduleVersion,
        string projectVersion,
        string expectedModuleVersion,
        string? expectedPreRelease,
        bool useReleaseBuildOrder)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var stagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "Mailozaurr";
            WriteMinimalModule(root.FullName, moduleName, moduleVersion);
            WriteProjectBuildConfig(root.FullName, Path.Combine("Build", "project.build.json"));

            var packageOutput = Path.Combine(root.FullName, "Artifacts", "NuGet");
            var packagePath = Path.Combine(packageOutput, $"Mailozaurr.{projectVersion}.nupkg");
            var hostedOperations = new FakeHostedOperations(new List<string>());
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: hostedOperations,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: (request, configuration, configPath) =>
                {
                    Directory.CreateDirectory(packageOutput);
                    File.WriteAllText(packagePath, "package");

                    var release = new DotNetRepositoryReleaseResult { Success = true, ResolvedVersion = projectVersion };
                    release.ResolvedVersionsByProject[moduleName] = projectVersion;
                    var project = new DotNetRepositoryProjectResult
                    {
                        ProjectName = moduleName,
                        PackageId = moduleName,
                        IsPackable = true,
                        NewVersion = projectVersion
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
                    Version = moduleVersion,
                    StagingPath = stagingPath
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationProjectBuildSegment
                    {
                        Configuration = new ProjectBuildConfigurationReference
                        {
                            Name = moduleName,
                            ConfigPath = Path.Combine("Build", "project.build.json"),
                            BuildBeforeModule = !useReleaseBuildOrder,
                            UseAsReleaseVersionSource = true
                        }
                    },
                    new ConfigurationReleaseSegment
                    {
                        Configuration = new ReleaseConfiguration
                        {
                            StageRoot = Path.Combine(root.FullName, "Artifacts", "Unified"),
                            VersionSource = ReleaseVersionSource.ProjectBuild,
                            PrimaryProject = moduleName,
                            SynchronizeModuleVersion = synchronizeModuleVersion,
                            BuildOrder = useReleaseBuildOrder
                                ? new[] { "ProjectBuild", "Module" }
                                : null
                        }
                    },
                    new ConfigurationPublishSegment
                    {
                        Configuration = new PublishConfiguration
                        {
                            Enabled = true,
                            Destination = PublishDestination.PowerShellGallery,
                            RepositoryName = "PSGallery",
                            ApiKey = "gallery-token"
                        }
                    }
                }
            };

            var result = runner.Run(spec);

            Assert.Equal(expectedModuleVersion, result.Plan.ResolvedVersion);
            Assert.Equal(expectedPreRelease, result.Plan.PreRelease);
            Assert.Equal(expectedModuleVersion, result.Plan.BuildSpec.Version);
            Assert.Contains($"ModuleVersion = '{expectedModuleVersion}'", File.ReadAllText(result.BuildResult.ManifestPath), StringComparison.Ordinal);
            if (string.IsNullOrWhiteSpace(expectedPreRelease))
            {
                Assert.False(ManifestEditor.TryGetPsDataStringArray(result.BuildResult.ManifestPath, "Prerelease", out _));
            }
            else
            {
                Assert.True(ManifestEditor.TryGetPsDataStringArray(result.BuildResult.ManifestPath, "Prerelease", out var manifestPreRelease));
                Assert.Equal(new[] { expectedPreRelease }, manifestPreRelease);
            }
            Assert.NotNull(result.ReleaseCoordinationResult);
            var expectedPublishedModuleVersion = string.IsNullOrWhiteSpace(expectedPreRelease)
                ? expectedModuleVersion
                : $"{expectedModuleVersion}-{expectedPreRelease}";
            Assert.Equal(expectedPublishedModuleVersion, result.ReleaseCoordinationResult!.ModuleVersion);
            Assert.Equal(projectVersion, result.ReleaseCoordinationResult.ReleaseVersion);
            Assert.Equal(new[] { expectedModuleVersion }, hostedOperations.PublishedModuleVersions);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_FailsUnifiedGitHubReleaseWhenNoPayloadAssetsExist()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var gitHubPublishCalled = false;
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: null,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: null,
                gitHubReleasePublisher: _ =>
                {
                    gitHubPublishCalled = true;
                    return new GitHubReleasePublishResult { Succeeded = true };
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
                    new ConfigurationReleaseSegment
                    {
                        Configuration = new ReleaseConfiguration
                        {
                            StageRoot = Path.Combine(root.FullName, "Artifacts", "Unified", "<ModuleName>", "<ModuleVersion>")
                        }
                    },
                    new ConfigurationPublishSegment
                    {
                        Configuration = new PublishConfiguration
                        {
                            Enabled = true,
                            Destination = PublishDestination.GitHub,
                            UserName = "EvotecIT",
                            RepositoryName = "TestModule",
                            ApiKey = "token"
                        }
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));

            Assert.Contains("No module or package assets", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(gitHubPublishCalled);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_ReusesReleaseAssetsAlreadyInStageFolder()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            GitHubReleasePublishRequest? gitHubRequest = null;
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: null,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: null,
                gitHubReleasePublisher: request =>
                {
                    gitHubRequest = request;
                    return new GitHubReleasePublishResult
                    {
                        Succeeded = true,
                        ReleaseCreationSucceeded = true,
                        AllAssetUploadsSucceeded = true,
                        HtmlUrl = "https://github.com/EvotecIT/TestModule/releases/tag/v1.0.0"
                    };
                });

            var stageRoot = Path.Combine(root.FullName, "Artifacts", "Unified", moduleName, "1.0.0");
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
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Packed,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = Path.Combine(stageRoot, "modules"),
                            ID = "module"
                        }
                    },
                    new ConfigurationReleaseSegment
                    {
                        Configuration = new ReleaseConfiguration
                        {
                            StageRoot = stageRoot
                        }
                    },
                    new ConfigurationPublishSegment
                    {
                        Configuration = new PublishConfiguration
                        {
                            Enabled = true,
                            Destination = PublishDestination.GitHub,
                            UserName = "EvotecIT",
                            RepositoryName = "TestModule",
                            ApiKey = "token"
                        }
                    }
                }
            };

            var result = runner.Run(spec);

            Assert.NotNull(gitHubRequest);
            Assert.NotNull(result.ReleaseCoordinationResult);
            var moduleAsset = Assert.Single(result.ReleaseCoordinationResult!.ModuleAssetPaths);
            Assert.StartsWith(Path.Combine(stageRoot, "modules"), moduleAsset, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(gitHubRequest!.AssetFilePaths, path => string.Equals(path, moduleAsset, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_OverwritesStagedReleaseAssetsOnRepeatedBuilds()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var packageOutput = Path.Combine(root.FullName, "Artifacts", "NuGet");
            var packagePath = Path.Combine(packageOutput, "HtmlTinkerX.1.0.0.nupkg");
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
                    Directory.CreateDirectory(packageOutput);
                    File.WriteAllText(packagePath, $"package-{packageBuildCalls}");

                    var release = new DotNetRepositoryReleaseResult { Success = true };
                    var project = new DotNetRepositoryProjectResult
                    {
                        ProjectName = "HtmlTinkerX",
                        PackageId = "HtmlTinkerX",
                        IsPackable = true,
                        NewVersion = "1.0.0"
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

            var stageRoot = Path.Combine(root.FullName, "Artifacts", "Unified", "<ModuleName>", "<ModuleVersion>");
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
                            Name = "Packages",
                            RootPath = "Sources",
                            BuildBeforeModule = true
                        }
                    },
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Packed,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = Path.Combine(root.FullName, "Artifacts", "Module"),
                            ID = "module"
                        }
                    },
                    new ConfigurationReleaseSegment
                    {
                        Configuration = new ReleaseConfiguration
                        {
                            StageRoot = stageRoot
                        }
                    }
                }
            };

            runner.Run(spec);
            var second = runner.Run(spec);

            var resolvedStageRoot = Path.Combine(root.FullName, "Artifacts", "Unified", moduleName, "1.0.0");
            var stagedPackage = Path.Combine(resolvedStageRoot, "nuget", Path.GetFileName(packagePath));
            Assert.True(File.Exists(stagedPackage), stagedPackage);
            Assert.Equal("package-2", File.ReadAllText(stagedPackage));
            Assert.Equal(2, packageBuildCalls);
            Assert.NotNull(second.ReleaseCoordinationResult);
            Assert.Contains(second.ReleaseCoordinationResult!.AssetPaths, path => string.Equals(path, stagedPackage, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void StageReleaseAsset_RejectsSameRunFilenameCollisionsButAllowsRepeatedBuildOverwrite()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var firstSource = Path.Combine(root.FullName, "first", "Package.1.0.0.nupkg");
            var secondSource = Path.Combine(root.FullName, "second", "Package.1.0.0.nupkg");
            Directory.CreateDirectory(Path.GetDirectoryName(firstSource)!);
            Directory.CreateDirectory(Path.GetDirectoryName(secondSource)!);
            File.WriteAllText(firstSource, "first");
            File.WriteAllText(secondSource, "second");

            var method = typeof(ModulePipelineRunner).GetMethod("StageReleaseAsset", BindingFlags.NonPublic | BindingFlags.Static)!;
            var currentRunSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var stageRoot = Path.Combine(root.FullName, "stage");

            var stagedPath = Assert.IsType<string>(method.Invoke(null, new object[] { stageRoot, "nuget", firstSource, currentRunSources })!);
            Assert.Equal("first", File.ReadAllText(stagedPath));

            var collision = Assert.Throws<TargetInvocationException>(() =>
                method.Invoke(null, new object[] { stageRoot, "nuget", secondSource, currentRunSources }));
            var invalidOperation = Assert.IsType<InvalidOperationException>(collision.InnerException);
            Assert.Contains("Release staging collision", invalidOperation.Message, StringComparison.Ordinal);
            Assert.Equal("first", File.ReadAllText(stagedPath));

            File.WriteAllText(firstSource, "first-updated");
            var nextRunSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var restagedPath = Assert.IsType<string>(method.Invoke(null, new object[] { stageRoot, "nuget", firstSource, nextRunSources })!);

            Assert.Equal(stagedPath, restagedPath);
            Assert.Equal("first-updated", File.ReadAllText(restagedPath));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_AppliesUnifiedReleasePublishOrderAcrossPackageAndModuleDestinations()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var events = new List<string>();
            var packageOutput = Path.Combine(root.FullName, "Artifacts", "NuGet");
            var packagePath = Path.Combine(packageOutput, "HtmlTinkerX.1.0.0.nupkg");
            var hostedOperations = new FakeHostedOperations(events);

            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: hostedOperations,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: (request, configuration, configPath) =>
                {
                    if (request.PublishNuget == true)
                        events.Add("package:nuget");
                    else if (request.PublishGitHub == true)
                        events.Add("package:github");
                    else
                        events.Add("package:build");

                    Directory.CreateDirectory(packageOutput);
                    File.WriteAllText(packagePath, "package");

                    var release = new DotNetRepositoryReleaseResult { Success = true };
                    var project = new DotNetRepositoryProjectResult
                    {
                        ProjectName = "HtmlTinkerX",
                        PackageId = "HtmlTinkerX",
                        IsPackable = true,
                        NewVersion = "1.0.0"
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
                },
                gitHubReleasePublisher: request =>
                {
                    events.Add("module:github");
                    return new GitHubReleasePublishResult
                    {
                        Succeeded = true,
                        ReleaseCreationSucceeded = true,
                        AllAssetUploadsSucceeded = true,
                        HtmlUrl = $"https://github.com/{request.Owner}/{request.Repository}/releases/tag/{request.TagName}"
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
                            Name = "Packages",
                            RootPath = "Sources",
                            BuildBeforeModule = true,
                            Build = true,
                            PublishNuget = true,
                            PublishGitHub = true
                        }
                    },
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Packed,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = Path.Combine(root.FullName, "Artifacts", "Module"),
                            ID = "module"
                        }
                    },
                    new ConfigurationReleaseSegment
                    {
                        Configuration = new ReleaseConfiguration
                        {
                            StageRoot = Path.Combine(root.FullName, "Artifacts", "Unified", "<ModuleName>", "<ModuleVersion>"),
                            PublishOrder = new[] { "PowerShellGallery", "NuGet", "GitHub" }
                        }
                    },
                    new ConfigurationPublishSegment
                    {
                        Configuration = new PublishConfiguration
                        {
                            Enabled = true,
                            Destination = PublishDestination.PowerShellGallery,
                            RepositoryName = "PSGallery",
                            ApiKey = "gallery-token"
                        }
                    },
                    new ConfigurationPublishSegment
                    {
                        Configuration = new PublishConfiguration
                        {
                            Enabled = true,
                            Destination = PublishDestination.GitHub,
                            UserName = "EvotecIT",
                            RepositoryName = "TestModule",
                            ApiKey = "token"
                        }
                    }
                }
            };

            runner.Run(spec);

            Assert.Equal(new[] { "package:build", "module:PowerShellGallery", "package:nuget", "package:github", "module:github" }, events);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_HonorsGitHubPublishIdWhenSelectingUnifiedModuleAssets()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            GitHubReleasePublishRequest? gitHubRequest = null;
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: null,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: null,
                gitHubReleasePublisher: request =>
                {
                    gitHubRequest = request;
                    return new GitHubReleasePublishResult
                    {
                        Succeeded = true,
                        ReleaseCreationSucceeded = true,
                        AllAssetUploadsSucceeded = true,
                        HtmlUrl = $"https://github.com/{request.Owner}/{request.Repository}/releases/tag/{request.TagName}"
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
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Packed,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = Path.Combine(root.FullName, "Artifacts", "Primary"),
                            ID = "primary"
                        }
                    },
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Packed,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = Path.Combine(root.FullName, "Artifacts", "Secondary"),
                            ID = "secondary"
                        }
                    },
                    new ConfigurationReleaseSegment
                    {
                        Configuration = new ReleaseConfiguration
                        {
                            StageRoot = Path.Combine(root.FullName, "Artifacts", "Unified", "<ModuleName>", "<ModuleVersion>")
                        }
                    },
                    new ConfigurationPublishSegment
                    {
                        Configuration = new PublishConfiguration
                        {
                            Enabled = true,
                            Destination = PublishDestination.GitHub,
                            UserName = "EvotecIT",
                            RepositoryName = "TestModule",
                            ApiKey = "token",
                            ID = "secondary"
                        }
                    }
                }
            };

            var result = runner.Run(spec);

            Assert.NotNull(gitHubRequest);
            Assert.NotNull(result.ReleaseCoordinationResult);
            Assert.Single(result.ReleaseCoordinationResult!.ModuleAssetPaths);
            Assert.Equal(3, gitHubRequest!.AssetFilePaths.Count);
            Assert.Contains(gitHubRequest.AssetFilePaths, path => path.StartsWith(Path.Combine(root.FullName, "Artifacts", "Unified", moduleName, "1.0.0", "modules"), StringComparison.OrdinalIgnoreCase));
            Assert.Contains(gitHubRequest.AssetFilePaths, path => path.EndsWith(Path.Combine("metadata", "release-manifest.json"), StringComparison.OrdinalIgnoreCase));
            Assert.Contains(gitHubRequest.AssetFilePaths, path => path.EndsWith(Path.Combine("metadata", "SHA256SUMS.txt"), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_DoesNotStageStalePackagesFromReusedOutputFolder()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var packageOutput = Path.Combine(root.FullName, "Artifacts", "NuGet");
            var currentPackage = Path.Combine(packageOutput, "HtmlTinkerX.2.0.1.nupkg");
            var stalePackage = Path.Combine(packageOutput, "HtmlTinkerX.1.9.9.nupkg");
            GitHubReleasePublishRequest? gitHubRequest = null;

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
                    File.WriteAllText(currentPackage, "current-package");
                    File.WriteAllText(stalePackage, "stale-package");

                    var release = new DotNetRepositoryReleaseResult { Success = true };
                    var project = new DotNetRepositoryProjectResult
                    {
                        ProjectName = "HtmlTinkerX",
                        PackageId = "HtmlTinkerX",
                        IsPackable = true,
                        NewVersion = "2.0.1"
                    };
                    project.Packages.Add(currentPackage);
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
                },
                gitHubReleasePublisher: request =>
                {
                    gitHubRequest = request;
                    return new GitHubReleasePublishResult
                    {
                        Succeeded = true,
                        ReleaseCreationSucceeded = true,
                        AllAssetUploadsSucceeded = true,
                        HtmlUrl = $"https://github.com/{request.Owner}/{request.Repository}/releases/tag/{request.TagName}"
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
                            Name = "Packages",
                            RootPath = "Sources",
                            BuildBeforeModule = true,
                            Build = true,
                            PublishNuget = false,
                            PublishGitHub = false
                        }
                    },
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Packed,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = Path.Combine(root.FullName, "Artifacts", "Module"),
                            ID = "module"
                        }
                    },
                    new ConfigurationReleaseSegment
                    {
                        Configuration = new ReleaseConfiguration
                        {
                            StageRoot = Path.Combine(root.FullName, "Artifacts", "Unified", "<ModuleName>", "<ModuleVersion>")
                        }
                    },
                    new ConfigurationPublishSegment
                    {
                        Configuration = new PublishConfiguration
                        {
                            Enabled = true,
                            Destination = PublishDestination.GitHub,
                            UserName = "EvotecIT",
                            RepositoryName = "TestModule",
                            ApiKey = "token"
                        }
                    }
                }
            };

            runner.Run(spec);

            Assert.NotNull(gitHubRequest);
            Assert.Contains(gitHubRequest!.AssetFilePaths, path => string.Equals(Path.GetFileName(path), Path.GetFileName(currentPackage), StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(gitHubRequest.AssetFilePaths, path => string.Equals(Path.GetFileName(path), Path.GetFileName(stalePackage), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    private static void WriteMinimalModule(string moduleRoot, string moduleName, string version)
    {
        Directory.CreateDirectory(moduleRoot);
        Directory.CreateDirectory(Path.Combine(moduleRoot, ".git"));
        File.WriteAllText(Path.Combine(moduleRoot, $"{moduleName}.psm1"), "function Get-Test { 'ok' }");

        var psd1 = string.Join(Environment.NewLine, new[]
        {
            "@{",
            $"    RootModule = '{moduleName}.psm1'",
            $"    ModuleVersion = '{version}'",
            "    FunctionsToExport = @('Get-Test')",
            "    CmdletsToExport = @()",
            "    AliasesToExport = @()",
            "}"
        }) + Environment.NewLine;

        File.WriteAllText(Path.Combine(moduleRoot, $"{moduleName}.psd1"), psd1);
    }

    private static string GetCoordinatedReleaseCheckpointRoot(string projectRoot)
        => Path.Combine(
            ModulePipelineRunner.ResolveSynchronizedReleaseStateRoot(projectRoot),
            "coordinated-release");

    private static void AssertNoCoordinatedReleaseCheckpoint(string projectRoot)
    {
        var checkpointRoot = GetCoordinatedReleaseCheckpointRoot(projectRoot);
        if (Directory.Exists(checkpointRoot))
            Assert.Empty(Directory.GetFiles(checkpointRoot, "*.json"));
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

    private sealed class FakeHostedOperations :
        IModulePipelineHostedOperations,
        IModulePipelinePublishPreflightOperations
    {
        private readonly List<string> _events;

        public FakeHostedOperations(List<string> events)
        {
            _events = events;
        }

        public List<string> PublishedModuleVersions { get; } = new();
        public Func<PublishConfiguration, ModulePipelinePlan, ModulePublishVersionPreflightResult>? ModulePublishVersionPreflight { get; set; }
        public Func<PublishConfiguration, ModulePipelinePlan, bool, ModulePublishVersionPreflightResult>? ModulePublishVersionPreflightWithExistingPolicy { get; set; }
        public Action<PublishConfiguration, ModulePipelinePlan>? ModulePublishAction { get; set; }
        public Func<ModulePipelineActionConfiguration, ModulePipelineActionContext, ModulePipelineActionResult>? ModuleAction { get; set; }

        public IReadOnlyList<ModuleDependencyInstallResult> EnsureDependenciesInstalled(
            ModuleDependency[] dependencies,
            ModuleSkipConfiguration? skipModules,
            bool force,
            string? repository,
            RepositoryCredential? credential,
            bool prerelease)
            => Array.Empty<ModuleDependencyInstallResult>();

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
            => throw new InvalidOperationException("Documentation is not used in this test.");

        public ModuleValidationReport ValidateModule(ModuleValidationSpec spec)
            => throw new InvalidOperationException("Validation is not used in this test.");

        public void EnsureBinaryDependenciesValid(string moduleRoot, string powerShellEdition, string? modulePath, string? validationTarget)
            => throw new InvalidOperationException("Binary validation is not used in this test.");

        public ModuleTestSuiteResult RunModuleTestSuite(ModuleTestSuiteSpec spec)
            => throw new InvalidOperationException("Tests are not used in this test.");

        public ModulePublishResult PublishModule(
            PublishConfiguration publish,
            ModulePipelinePlan plan,
            ModuleBuildResult buildResult,
            IReadOnlyList<ArtefactBuildResult> artefactResults,
            bool includeScriptFolders)
        {
            ModulePublishAction?.Invoke(publish, plan);
            _events.Add($"module:{publish.Destination}");
            PublishedModuleVersions.Add(plan.ResolvedVersion);
            return new ModulePublishResult(
                publish.Destination,
                publish.RepositoryName,
                publish.UserName,
                tagName: null,
                versionText: plan.ResolvedVersion,
                isPreRelease: false,
                assetPaths: Array.Empty<string>(),
                releaseUrl: null,
                succeeded: true,
                errorMessage: null);
        }

        public ModulePublishVersionPreflightResult ValidateModulePublishVersion(
            PublishConfiguration publish,
            ModulePipelinePlan plan,
            bool allowExistingExactVersion)
            => ModulePublishVersionPreflightWithExistingPolicy?.Invoke(publish, plan, allowExistingExactVersion) ??
               ModulePublishVersionPreflight?.Invoke(publish, plan) ??
               ModulePublishVersionPreflightResult.Available;

        public ModulePipelineActionResult RunAction(
            ModulePipelineActionConfiguration action,
            ModulePipelineActionContext context,
            string contextPath,
            string projectRoot)
            => ModuleAction?.Invoke(action, context) ??
               throw new InvalidOperationException("Actions are not used in this test.");

        public void ValidateModuleImports(
            string manifestPath,
            ImportModuleEntry[] modules,
            bool importRequired,
            bool importSelf,
            bool verbose,
            ModuleImportValidationTarget[] targets)
            => throw new InvalidOperationException("Import validation is not used in this test.");

        public ModuleSigningResult SignModuleOutput(
            string moduleName,
            string rootPath,
            string[] includePatterns,
            string[] excludeSubstrings,
            SigningOptionsConfiguration signing)
            => throw new InvalidOperationException("Signing is not used in this test.");
    }
}
