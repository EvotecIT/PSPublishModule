using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineUnifiedReleaseTests
{
    [Fact]
    public void Run_RejectsConflictingPrimaryPackageReleaseVersions()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var stagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var call = 0;
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
                    call++;
                    var release = new DotNetRepositoryReleaseResult { Success = true };
                    var project = new DotNetRepositoryProjectResult
                    {
                        ProjectName = "HtmlTinkerX",
                        PackageId = "HtmlTinkerX",
                        IsPackable = true,
                        NewVersion = call == 1 ? "2.0.1" : "2.0.2"
                    };
                    release.Projects.Add(project);

                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        ConfigPath = configPath ?? request.ConfigPath,
                        RootPath = root.FullName,
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
                    new ConfigurationPackageBuildSegment
                    {
                        Configuration = new PackageBuildConfiguration
                        {
                            Name = "PackagesA",
                            RootPath = "SourcesA",
                            BuildBeforeModule = true,
                            Build = true,
                            PublishNuget = false,
                            PublishGitHub = false
                        }
                    },
                    new ConfigurationPackageBuildSegment
                    {
                        Configuration = new PackageBuildConfiguration
                        {
                            Name = "PackagesB",
                            RootPath = "SourcesB",
                            BuildBeforeModule = true,
                            Build = true,
                            PublishNuget = false,
                            PublishGitHub = false
                        }
                    },
                    new ConfigurationReleaseSegment
                    {
                        Configuration = new ReleaseConfiguration
                        {
                            VersionSource = ReleaseVersionSource.PackageBuild,
                            PrimaryProject = "HtmlTinkerX"
                        }
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));

            Assert.Contains("multiple versions", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("PrimaryProject 'HtmlTinkerX'", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_StagesUnifiedPackageAssetsAfterPackagePublishRewritesOutputs()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var packageOutput = Path.Combine(root.FullName, "Artifacts", "NuGet");
            var packagePath = Path.Combine(packageOutput, "HtmlTinkerX.1.0.0.nupkg");
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
                    File.WriteAllText(packagePath, request.PublishNuget == true ? "published-package" : "built-package");

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
                    gitHubRequest = request;
                    return new GitHubReleasePublishResult
                    {
                        Succeeded = true,
                        ReleaseCreationSucceeded = true,
                        AllAssetUploadsSucceeded = true,
                        HtmlUrl = "https://github.com/EvotecIT/TestModule/releases/tag/v1.0.0"
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
                            BuildBeforeModule = true,
                            Build = true,
                            PublishNuget = true,
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
                            StageRoot = stageRoot,
                            PublishOrder = new[] { "NuGet", "GitHub" }
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
            var stagedPackage = Assert.Single(gitHubRequest!.AssetFilePaths, path => path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("published-package", File.ReadAllText(stagedPackage));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
