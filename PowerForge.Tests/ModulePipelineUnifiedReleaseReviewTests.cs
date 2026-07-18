using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineUnifiedReleaseTests
{
    [Theory]
    [InlineData(ReleaseVersionSource.Module, true, true, "VersionSource ProjectBuild or PackageBuild")]
    [InlineData(ReleaseVersionSource.PackageBuild, false, true, "UseAsReleaseVersionSource")]
    [InlineData(ReleaseVersionSource.PackageBuild, true, false, "BuildBeforeModule")]
    public void Plan_RejectsInvalidModuleVersionSynchronizationBeforePackageExecution(
        ReleaseVersionSource versionSource,
        bool useAsReleaseVersionSource,
        bool buildBeforeModule,
        string expectedMessage)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var packageExecutorCalled = false;
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
                    packageExecutorCalled = true;
                    return new ProjectBuildHostExecutionResult { Success = true };
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
                            BuildBeforeModule = buildBeforeModule,
                            UseAsReleaseVersionSource = useAsReleaseVersionSource
                        }
                    },
                    new ConfigurationReleaseSegment
                    {
                        Configuration = new ReleaseConfiguration
                        {
                            VersionSource = versionSource,
                            SynchronizeModuleVersion = true
                        }
                    }
                }
            };

            var exception = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));

            Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(packageExecutorCalled);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_PreflightsSynchronizedModuleVersionBeforePublishingNuGet()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var stagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "Mailozaurr";
            const string packageVersion = "2.0.11";
            WriteMinimalModule(root.FullName, moduleName, "2.1.6");
            var packageOutput = Path.Combine(root.FullName, "Artifacts", "NuGet");
            var packagePath = Path.Combine(packageOutput, $"{moduleName}.{packageVersion}.nupkg");
            var dependencyBuildCalls = 0;
            var nugetPublishCalls = 0;
            var hostedOperations = new FakeHostedOperations(new List<string>())
            {
                ModulePublishVersionPreflight = (_, plan) => throw new InvalidOperationException(
                    $"Module version '{plan.ResolvedVersion}' is not greater than repository version '2.1.6'.")
            };
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
                    {
                        nugetPublishCalls++;
                    }
                    else
                    {
                        dependencyBuildCalls++;
                    }

                    Directory.CreateDirectory(packageOutput);
                    File.WriteAllText(packagePath, "package");
                    var release = new DotNetRepositoryReleaseResult { Success = true, ResolvedVersion = packageVersion };
                    release.ResolvedVersionsByProject[moduleName] = packageVersion;
                    var project = new DotNetRepositoryProjectResult
                    {
                        ProjectName = moduleName,
                        PackageId = moduleName,
                        IsPackable = true,
                        NewVersion = packageVersion
                    };
                    project.Packages.Add(packagePath);
                    release.Projects.Add(project);
                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        ConfigPath = configPath ?? request.ConfigPath,
                        RootPath = root.FullName,
                        OutputPath = packageOutput,
                        Result = new ProjectBuildResult { Success = true, Release = release }
                    };
                });
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "2.1.X",
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
                            UseAsReleaseVersionSource = true,
                            PublishNuget = true
                        }
                    },
                    new ConfigurationReleaseSegment
                    {
                        Configuration = new ReleaseConfiguration
                        {
                            VersionSource = ReleaseVersionSource.PackageBuild,
                            PrimaryProject = moduleName,
                            SynchronizeModuleVersion = true,
                            PublishOrder = new[] { "NuGet", "PowerShellGallery" }
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

            var exception = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));

            Assert.Contains("not greater than repository version", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, dependencyBuildCalls);
            Assert.Equal(0, nugetPublishCalls);
            Assert.Empty(hostedOperations.PublishedModuleVersions);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_ManifestGateSkipsRuntimeModuleVersionSynchronization()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var packageExecutorCalled = false;
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
                    packageExecutorCalled = true;
                    return new ProjectBuildHostExecutionResult { Success = true };
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
                            UseAsReleaseVersionSource = true
                        }
                    },
                    new ConfigurationReleaseSegment
                    {
                        Configuration = new ReleaseConfiguration
                        {
                            VersionSource = ReleaseVersionSource.PackageBuild,
                            SynchronizeModuleVersion = true
                        }
                    },
                    new ConfigurationGateSegment
                    {
                        Configuration = new GateConfiguration { Mode = ConfigurationGateMode.Manifest }
                    }
                }
            };

            var result = runner.Run(spec);

            Assert.Equal("1.0.0", result.Plan.ResolvedVersion);
            Assert.False(packageExecutorCalled);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

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

    [Fact]
    public void Run_RejectsArtefactOutputRootThatContainsReleaseAndProjectBuildOutputs()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var artefactsRoot = Path.Combine(root.FullName, "Artifacts");
            var packageOutput = Path.Combine(artefactsRoot, "ProjectBuild", "packages");
            var packagePath = Path.Combine(packageOutput, "Mailozaurr.1.0.0.nupkg");

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
                        ProjectName = "Mailozaurr",
                        PackageId = "Mailozaurr",
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
                        StagingPath = Path.Combine(artefactsRoot, "ProjectBuild"),
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
                            Name = "Packages",
                            RootPath = "Sources",
                            BuildBeforeModule = true
                        }
                    },
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Unpacked,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = artefactsRoot
                        }
                    },
                    new ConfigurationReleaseSegment
                    {
                        Configuration = new ReleaseConfiguration
                        {
                            StageRoot = Path.Combine(artefactsRoot, "UploadReady")
                        }
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));

            Assert.Contains("Release artefact configuration is unsafe", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("would affect release stage root", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("would affect project build output path", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(@"Artefacts\Unpacked", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(packagePath), "The guard should stop before the unpacked artefact clears the package output tree.");
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_AllowsDoNotClearUnpackedArtefactRootWithSiblingReleaseAndProjectBuildOutputs()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var artefactsRoot = Path.Combine(root.FullName, "Artifacts");
            var packageOutput = Path.Combine(artefactsRoot, "ProjectBuild", "packages");
            var packagePath = Path.Combine(packageOutput, "Mailozaurr.1.0.0.nupkg");

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
                        ProjectName = "Mailozaurr",
                        PackageId = "Mailozaurr",
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
                        StagingPath = Path.Combine(artefactsRoot, "ProjectBuild"),
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
                            Name = "Packages",
                            RootPath = "Sources",
                            BuildBeforeModule = true
                        }
                    },
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Unpacked,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            DoNotClear = true,
                            Path = artefactsRoot
                        }
                    },
                    new ConfigurationReleaseSegment
                    {
                        Configuration = new ReleaseConfiguration
                        {
                            StageRoot = Path.Combine(artefactsRoot, "UploadReady")
                        }
                    }
                }
            };

            var result = runner.Run(spec);

            Assert.True(File.Exists(packagePath), "DoNotClear unpacked artefacts should not clear sibling project-build package output.");
            Assert.NotNull(result.ReleaseCoordinationResult);
            Assert.Contains(result.ReleaseCoordinationResult!.PackageAssetPaths, path => string.Equals(path, Path.Combine(artefactsRoot, "UploadReady", "nuget", Path.GetFileName(packagePath)), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_AllowsPackedArtefactRootWithSiblingReleaseAndProjectBuildDirectories()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var artefactsRoot = Path.Combine(root.FullName, "Artifacts");
            var packageOutput = Path.Combine(artefactsRoot, "ProjectBuild", "packages");
            var packagePath = Path.Combine(packageOutput, "Mailozaurr.1.0.0.nupkg");

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
                        ProjectName = "Mailozaurr",
                        PackageId = "Mailozaurr",
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
                        StagingPath = Path.Combine(artefactsRoot, "ProjectBuild"),
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
                            Path = artefactsRoot,
                            ID = "module"
                        }
                    },
                    new ConfigurationReleaseSegment
                    {
                        Configuration = new ReleaseConfiguration
                        {
                            StageRoot = Path.Combine(artefactsRoot, "UploadReady")
                        }
                    }
                }
            };

            var result = runner.Run(spec);

            Assert.True(File.Exists(packagePath), "Packed artefacts should not clear sibling project-build package directories.");
            Assert.NotNull(result.ReleaseCoordinationResult);
            Assert.Single(result.ReleaseCoordinationResult!.ModuleAssetPaths);
            Assert.Contains(result.ReleaseCoordinationResult.PackageAssetPaths, path => string.Equals(path, Path.Combine(artefactsRoot, "UploadReady", "nuget", Path.GetFileName(packagePath)), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_RejectsUnpackedCopyMappingThatWouldClearProjectBuildOutput()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var artefactsRoot = Path.Combine(root.FullName, "Artifacts");
            var mappingSource = Path.Combine(root.FullName, "MappingSource");
            var packageOutput = Path.Combine(artefactsRoot, "ProjectBuild", "packages");
            var packagePath = Path.Combine(packageOutput, "Mailozaurr.1.0.0.nupkg");
            Directory.CreateDirectory(mappingSource);
            File.WriteAllText(Path.Combine(mappingSource, "payload.txt"), "payload");

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
                        ProjectName = "Mailozaurr",
                        PackageId = "Mailozaurr",
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
                        ArtefactType = ArtefactType.Unpacked,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = Path.Combine(artefactsRoot, "Unpacked"),
                            DirectoryOutput = new[]
                            {
                                new ArtefactCopyMapping
                                {
                                    Source = mappingSource,
                                    Destination = packageOutput
                                }
                            }
                        }
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));

            Assert.Contains("directory copy mapping output", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("project build output path", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(packagePath), "The guard should stop before an unpacked copy mapping clears package output.");
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_RejectsPackedArtefactZipThatWouldOverwriteProjectBuildReleaseZip()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var releaseOutput = Path.Combine(root.FullName, "Artifacts", "Releases");
            var releaseZipPath = Path.Combine(releaseOutput, "Mailozaurr.1.0.0.zip");

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
                    Directory.CreateDirectory(releaseOutput);
                    File.WriteAllText(releaseZipPath, "project-release-zip");

                    var release = new DotNetRepositoryReleaseResult { Success = true };
                    var project = new DotNetRepositoryProjectResult
                    {
                        ProjectName = "Mailozaurr",
                        PackageId = "Mailozaurr",
                        IsPackable = true,
                        NewVersion = "1.0.0",
                        ReleaseZipPath = releaseZipPath
                    };
                    release.Projects.Add(project);

                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        ConfigPath = configPath ?? request.ConfigPath,
                        RootPath = root.FullName,
                        ReleaseZipOutputPath = releaseOutput,
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
                            Path = releaseOutput,
                            ArtefactName = Path.GetFileName(releaseZipPath)
                        }
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));

            Assert.Contains("zip output file", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("project build release zip asset", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("project-release-zip", File.ReadAllText(releaseZipPath));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_RejectsUnpackedLayoutModuleRootThatWouldClearProjectBuildStaging()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var artefactsRoot = Path.Combine(root.FullName, "Artifacts");
            var projectBuildRoot = Path.Combine(artefactsRoot, "ProjectBuild");
            var protectedStagingPath = Path.Combine(projectBuildRoot, moduleName);
            Directory.CreateDirectory(protectedStagingPath);
            File.WriteAllText(Path.Combine(protectedStagingPath, "project-build.txt"), "project-build");

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
                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        ConfigPath = configPath ?? request.ConfigPath,
                        RootPath = root.FullName,
                        StagingPath = protectedStagingPath,
                        Result = new ProjectBuildResult
                        {
                            Success = true,
                            Release = new DotNetRepositoryReleaseResult { Success = true }
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
                            Name = "Packages",
                            RootPath = "Sources",
                            BuildBeforeModule = true
                        }
                    },
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Unpacked,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = Path.Combine(artefactsRoot, "Unpacked"),
                            RequiredModules = new ArtefactRequiredModulesConfiguration
                            {
                                ModulesPath = projectBuildRoot
                            }
                        }
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));

            Assert.Contains("main module copy root", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("project build staging path", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("project-build", File.ReadAllText(Path.Combine(protectedStagingPath, "project-build.txt")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_RejectsPackedCopyMappingThatEscapesTemporaryPayloadRoot()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var artefactsRoot = Path.Combine(root.FullName, "Artifacts");
            var mappingSource = Path.Combine(root.FullName, "MappingSource");
            var packageOutput = Path.Combine(artefactsRoot, "ProjectBuild", "packages");
            var packagePath = Path.Combine(packageOutput, "Mailozaurr.1.0.0.nupkg");
            Directory.CreateDirectory(mappingSource);
            File.WriteAllText(Path.Combine(mappingSource, "payload.txt"), "payload");

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
                        ProjectName = "Mailozaurr",
                        PackageId = "Mailozaurr",
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
                            Path = Path.Combine(artefactsRoot, "Packed"),
                            DirectoryOutput = new[]
                            {
                                new ArtefactCopyMapping
                                {
                                    Source = mappingSource,
                                    Destination = Path.Combine("..", "Escaped")
                                }
                            }
                        }
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));

            Assert.Contains("resolves outside the temporary packing root", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(packagePath), "The guard should stop before a packed copy mapping can escape the payload root.");
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
