using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

                    var release = new DotNetRepositoryReleaseResult { Success = true };
                    var project = new DotNetRepositoryProjectResult
                    {
                        ProjectName = "HtmlTinkerX",
                        PackageId = "HtmlTinkerX",
                        IsPackable = true,
                        NewVersion = "2.0.1-beta1+build.7"
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
                    gitHubRequest = request;
                    return new GitHubReleasePublishResult
                    {
                        Succeeded = true,
                        ReleaseCreationSucceeded = true,
                        AllAssetUploadsSucceeded = true,
                        HtmlUrl = "https://github.com/EvotecIT/TestModule/releases/tag/v2.0.1-beta1"
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
            Assert.Equal("v2.0.1-beta1", gitHubRequest.TagName);
            Assert.True(gitHubRequest.IsPreRelease);
            Assert.True(gitHubRequest.GenerateReleaseNotes);
            Assert.Equal("2.0.1", result.Plan.ResolvedVersion);
            Assert.Equal("beta1", result.Plan.PreRelease);
            Assert.Equal("2.0.1", result.Plan.BuildSpec.Version);
            var projectManifestPath = Path.Combine(root.FullName, $"{moduleName}.psd1");
            var projectManifest = File.ReadAllText(projectManifestPath);
            Assert.Contains("ModuleVersion = '2.0.1'", projectManifest, StringComparison.Ordinal);
            Assert.False(ManifestEditor.TryGetTopLevelString(projectManifestPath, "Prerelease", out _));
            Assert.True(ManifestEditor.TryGetPsDataStringArray(projectManifestPath, "Prerelease", out var prerelease));
            Assert.Equal(new[] { "beta1" }, prerelease);

            var resolvedStageRoot = Path.Combine(root.FullName, "Artifacts", "Unified", moduleName, "2.0.1");
            Assert.NotNull(result.ReleaseCoordinationResult);
            Assert.Equal(Path.GetFullPath(resolvedStageRoot), result.ReleaseCoordinationResult!.StageRoot);
            Assert.Equal(4, gitHubRequest.AssetFilePaths.Count);
            Assert.Contains(gitHubRequest.AssetFilePaths, path => path.StartsWith(Path.Combine(resolvedStageRoot, "modules"), StringComparison.OrdinalIgnoreCase));
            Assert.Contains(gitHubRequest.AssetFilePaths, path => string.Equals(path, Path.Combine(resolvedStageRoot, "nuget", Path.GetFileName(packagePath)), StringComparison.OrdinalIgnoreCase));
            Assert.Contains(gitHubRequest.AssetFilePaths, path => string.Equals(path, Path.Combine(resolvedStageRoot, "metadata", "release-manifest.json"), StringComparison.OrdinalIgnoreCase));
            Assert.Contains(gitHubRequest.AssetFilePaths, path => string.Equals(path, Path.Combine(resolvedStageRoot, "metadata", "SHA256SUMS.txt"), StringComparison.OrdinalIgnoreCase));
            Assert.All(gitHubRequest.AssetFilePaths, path => Assert.True(File.Exists(path), path));
            var manifestJson = File.ReadAllText(Path.Combine(resolvedStageRoot, "metadata", "release-manifest.json"));
            Assert.Contains("\"version\": \"2.0.1-beta1\"", manifestJson, StringComparison.Ordinal);
            Assert.Contains("HtmlTinkerX.2.0.1-beta1.nupkg", manifestJson, StringComparison.Ordinal);
            Assert.Contains("nuget/HtmlTinkerX.2.0.1-beta1.nupkg", File.ReadAllText(Path.Combine(resolvedStageRoot, "metadata", "SHA256SUMS.txt")), StringComparison.Ordinal);
            Assert.Single(result.PublishResults);
            Assert.Equal(gitHubRequest.AssetFilePaths.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase), result.PublishResults[0].AssetPaths.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_UsesExplicitPackageLaneAsReleaseVersionSourceWithoutReleaseSegment()
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

            Assert.Equal("3.4.5", result.Plan.ResolvedVersion);
            Assert.Equal("3.4.5", result.Plan.BuildSpec.Version);
            Assert.Contains("ModuleVersion = '3.4.5'", File.ReadAllText(result.BuildResult.ManifestPath), StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_UsesExplicitPackageLaneAsReleaseVersionSourceWithReleaseSegment()
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

            Assert.Equal("3.4.5", result.Plan.ResolvedVersion);
            Assert.Equal("3.4.5", result.Plan.BuildSpec.Version);
            Assert.Contains("ModuleVersion = '3.4.5'", File.ReadAllText(result.BuildResult.ManifestPath), StringComparison.Ordinal);
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

    private static void WriteMinimalModule(string moduleRoot, string moduleName, string version)
    {
        Directory.CreateDirectory(moduleRoot);
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

    private sealed class FakeHostedOperations : IModulePipelineHostedOperations
    {
        private readonly List<string> _events;

        public FakeHostedOperations(List<string> events)
        {
            _events = events;
        }

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
            _events.Add($"module:{publish.Destination}");
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

        public ModulePipelineActionResult RunAction(
            ModulePipelineActionConfiguration action,
            ModulePipelineActionContext context,
            string contextPath,
            string projectRoot)
            => throw new InvalidOperationException("Actions are not used in this test.");

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
