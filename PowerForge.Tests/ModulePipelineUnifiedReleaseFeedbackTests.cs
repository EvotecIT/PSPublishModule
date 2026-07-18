using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineUnifiedReleaseTests
{
    [Fact]
    public void Run_ResumesExactCoordinatedVersionAfterPartialPublishFailure()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var firstStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        var secondStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            const string synchronizedVersion = "2.0.11";
            const string companionName = "Companion";
            const string companionVersion = "5.0.7";
            const string companionExtraName = "CompanionExtra";
            const string companionExtraVersion = "6.0.9";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            var configPath = Path.Combine(root.FullName, "Build", "project.build.json");
            var companionConfigPath = Path.Combine(root.FullName, "Build", "companion.build.json");
            var companionExtraConfigPath = Path.Combine(root.FullName, "Build", "companion-extra.build.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(
                configPath,
                string.Join(Environment.NewLine, new[]
                {
                    "{",
                    "  \"RootPath\": \"Sources\",",
                    "  \"ExpectedVersionMap\": { \"TestModule\": \"2.0.X\" },",
                    "  \"ExpectedVersionMapAsInclude\": true,",
                    "  \"UpdateVersions\": true,",
                    "  \"Build\": true,",
                    "  \"PublishNuget\": true,",
                    "  \"PublishGitHub\": false",
                    "}"
                }));
            File.WriteAllText(
                companionConfigPath,
                string.Join(Environment.NewLine, new[]
                {
                    "{",
                    "  \"RootPath\": \"CompanionSources\",",
                    "  \"ExpectedVersionMap\": { \"Companion\": \"5.0.X\" },",
                    "  \"ExpectedVersionMapAsInclude\": true,",
                    "  \"UpdateVersions\": true,",
                    "  \"Build\": true,",
                    "  \"PublishNuget\": true,",
                    "  \"PublishGitHub\": false",
                    "}"
                }));
            File.WriteAllText(
                companionExtraConfigPath,
                string.Join(Environment.NewLine, new[]
                {
                    "{",
                    "  \"RootPath\": \"CompanionExtraSources\",",
                    "  \"ExpectedVersionMap\": { \"CompanionExtra\": \"6.0.X\" },",
                    "  \"ExpectedVersionMapAsInclude\": true,",
                    "  \"UpdateVersions\": true,",
                    "  \"Build\": true,",
                    "  \"PublishNuget\": true,",
                    "  \"PublishGitHub\": false",
                    "}"
                }));

            var packagePublishCount = 0;
            var resumedDependencyBuildUsedExactVersion = false;
            var resumedCompanionBuildUsedExactVersion = false;
            var resumedCompanionExtraBuildUsedExactVersion = false;
            var runNumber = 1;
            ProjectBuildHostExecutionResult ExecutePackageBuild(
                ProjectBuildHostRequest request,
                ProjectBuildConfiguration? configuration,
                string? loadedConfigPath)
            {
                Assert.NotNull(configuration);
                var isCompanion = loadedConfigPath?.EndsWith(
                    "companion.build.json",
                    StringComparison.OrdinalIgnoreCase) == true;
                var isCompanionExtra = loadedConfigPath?.EndsWith(
                    "companion-extra.build.json",
                    StringComparison.OrdinalIgnoreCase) == true;
                var projectName = isCompanion
                    ? companionName
                    : isCompanionExtra ? companionExtraName : moduleName;
                var projectVersion = isCompanion
                    ? companionVersion
                    : isCompanionExtra ? companionExtraVersion : synchronizedVersion;
                if (request.PublishNuget == true)
                    packagePublishCount++;
                if (runNumber == 2 && request.PublishNuget != true)
                {
                    var usedExactVersion =
                        configuration!.ExpectedVersionMap is not null &&
                        configuration.ExpectedVersionMap.TryGetValue(projectName, out var expected) &&
                        string.Equals(expected, projectVersion, StringComparison.OrdinalIgnoreCase);
                    if (isCompanion)
                        resumedCompanionBuildUsedExactVersion = usedExactVersion;
                    else if (isCompanionExtra)
                        resumedCompanionExtraBuildUsedExactVersion = usedExactVersion;
                    else
                        resumedDependencyBuildUsedExactVersion = usedExactVersion;
                }

                return CreateProjectBuildResult(
                    root.FullName,
                    projectName,
                    projectVersion,
                    Path.Combine(root.FullName, "Artifacts", "NuGet"),
                    request,
                    loadedConfigPath,
                    includePackage: false);
            }

            var firstHosted = new FakeHostedOperations(new List<string>())
            {
                ModulePublishAction = (_, _) => throw new InvalidOperationException("Simulated gallery outage.")
            };
            var firstRunner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: firstHosted,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: ExecutePackageBuild);

            var firstException = Assert.Throws<InvalidOperationException>(() =>
                firstRunner.Run(CreateResumableReleaseSpec(root.FullName, firstStagingPath, moduleName)));

            Assert.Contains("gallery outage", firstException.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(3, packagePublishCount);
            var checkpointRoot = GetCoordinatedReleaseCheckpointRoot(root.FullName);
            Assert.Single(Directory.GetFiles(checkpointRoot, "*.json"));

            runNumber = 2;
            var secondHosted = new FakeHostedOperations(new List<string>());
            var secondRunner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: secondHosted,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: ExecutePackageBuild);

            var result = secondRunner.Run(CreateResumableReleaseSpec(root.FullName, secondStagingPath, moduleName));

            Assert.True(resumedDependencyBuildUsedExactVersion);
            Assert.True(resumedCompanionBuildUsedExactVersion);
            Assert.True(resumedCompanionExtraBuildUsedExactVersion);
            Assert.Equal(synchronizedVersion, result.Plan.ResolvedVersion);
            Assert.Equal(3, packagePublishCount);
            Assert.Equal(new[] { synchronizedVersion }, secondHosted.PublishedModuleVersions);
            AssertNoCoordinatedReleaseCheckpoint(root.FullName);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(firstStagingPath)) Directory.Delete(firstStagingPath, recursive: true); } catch { }
            try { if (Directory.Exists(secondStagingPath)) Directory.Delete(secondStagingPath, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_RetainsCompletedPublishCheckpointUntilPostPublishPhasesSucceed()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var firstStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        var secondStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        var thirdStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            const string synchronizedVersion = "2.0.11";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            var configPath = Path.Combine(root.FullName, "Build", "project.build.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            const string originalProjectBuildConfiguration =
                "{\"RootPath\":\"Sources\",\"ExpectedVersion\":\"2.0.X\",\"Build\":true,\"PublishNuget\":true,\"PublishGitHub\":false}";
            File.WriteAllText(configPath, originalProjectBuildConfiguration);

            var packagePublishCount = 0;
            ProjectBuildHostExecutionResult ExecutePackageBuild(
                ProjectBuildHostRequest request,
                ProjectBuildConfiguration? configuration,
                string? loadedConfigPath)
            {
                if (request.PublishNuget == true)
                    packagePublishCount++;
                return CreateProjectBuildResult(
                    root.FullName,
                    moduleName,
                    synchronizedVersion,
                    Path.Combine(root.FullName, "Artifacts", "NuGet"),
                    request,
                    loadedConfigPath,
                    includePackage: false);
            }

            static ModulePipelineActionResult ActionResult(
                ModulePipelineActionConfiguration action,
                ModulePipelineActionContext context,
                bool succeeded)
                => new()
                {
                    Name = action.Name ?? context.Stage.ToString(),
                    Stage = context.Stage,
                    Succeeded = succeeded,
                    ExitCode = succeeded ? 0 : 1,
                    Executable = "fake-pwsh",
                    Inline = true,
                    WorkingDirectory = context.ProjectRoot,
                    ContextPath = context.ContextPath,
                    StdErr = succeeded ? string.Empty : "Simulated post-publish failure."
                };

            var firstHosted = new FakeHostedOperations(new List<string>())
            {
                ModuleAction = (action, context) => ActionResult(action, context, succeeded: false)
            };
            var firstRunner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: firstHosted,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: ExecutePackageBuild);

            var firstException = Assert.Throws<InvalidOperationException>(() => firstRunner.Run(
                CreatePostPublishFailureSpec(root.FullName, firstStagingPath, moduleName, "TestRepository")));

            Assert.Contains("post-publish failure", firstException.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, packagePublishCount);
            Assert.Single(firstHosted.PublishedModuleVersions);
            var checkpointRoot = GetCoordinatedReleaseCheckpointRoot(root.FullName);
            Assert.Single(Directory.GetFiles(checkpointRoot, "*.json"));

            var disabledSynchronizationHosted = new FakeHostedOperations(new List<string>());
            var disabledSynchronizationRunner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: disabledSynchronizationHosted,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: ExecutePackageBuild);

            var disabledSynchronizationException = Assert.Throws<InvalidOperationException>(() =>
                disabledSynchronizationRunner.Run(CreatePostPublishFailureSpec(
                    root.FullName,
                    secondStagingPath,
                    moduleName,
                    "TestRepository",
                    synchronizeModuleVersion: false)));

            Assert.Contains("configuration no longer matches", disabledSynchronizationException.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, packagePublishCount);

            File.WriteAllText(
                configPath,
                "{\"RootPath\":\"Sources\",\"ExpectedVersionMap\":{\"OtherProject\":\"2.1.X\"},\"ExpectedVersionMapAsInclude\":true,\"Build\":true,\"PublishNuget\":true,\"PublishGitHub\":false}");
            var changedSelectionHosted = new FakeHostedOperations(new List<string>());
            var changedSelectionRunner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: changedSelectionHosted,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: ExecutePackageBuild);

            var changedSelectionException = Assert.Throws<InvalidOperationException>(() =>
                changedSelectionRunner.Run(CreatePostPublishFailureSpec(
                    root.FullName,
                    secondStagingPath,
                    moduleName,
                    "TestRepository")));

            Assert.Contains("configuration no longer matches", changedSelectionException.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, packagePublishCount);
            File.WriteAllText(configPath, originalProjectBuildConfiguration);

            var changedTargetHosted = new FakeHostedOperations(new List<string>())
            {
                ModuleAction = (action, context) => ActionResult(action, context, succeeded: true)
            };
            var changedTargetRunner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: changedTargetHosted,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: ExecutePackageBuild);

            var changedTargetException = Assert.Throws<InvalidOperationException>(() => changedTargetRunner.Run(
                CreatePostPublishFailureSpec(root.FullName, secondStagingPath, moduleName, "OtherRepository")));

            Assert.Contains("configuration no longer matches", changedTargetException.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, packagePublishCount);
            Assert.Empty(changedTargetHosted.PublishedModuleVersions);

            var finalHosted = new FakeHostedOperations(new List<string>())
            {
                ModuleAction = (action, context) => ActionResult(action, context, succeeded: true)
            };
            var finalRunner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: finalHosted,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: ExecutePackageBuild);

            var result = finalRunner.Run(
                CreatePostPublishFailureSpec(root.FullName, thirdStagingPath, moduleName, "TestRepository"));

            Assert.Equal(synchronizedVersion, result.Plan.ResolvedVersion);
            Assert.Equal(1, packagePublishCount);
            Assert.Empty(finalHosted.PublishedModuleVersions);
            AssertNoCoordinatedReleaseCheckpoint(root.FullName);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(firstStagingPath)) Directory.Delete(firstStagingPath, recursive: true); } catch { }
            try { if (Directory.Exists(secondStagingPath)) Directory.Delete(secondStagingPath, recursive: true); } catch { }
            try { if (Directory.Exists(thirdStagingPath)) Directory.Delete(thirdStagingPath, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_RevalidatesTokenizedDeliveryPathsAfterModuleVersionSynchronization()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            const string synchronizedVersion = "2.0.0";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            WriteProjectBuildConfig(root.FullName, Path.Combine("Build", "project.build.json"));
            var packageOutput = Path.Combine(root.FullName, "Artifacts", "NuGet");
            var packagePath = Path.Combine(packageOutput, $"{moduleName}.{synchronizedVersion}.nupkg");
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: null,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: (request, configuration, configPath) =>
                    CreateProjectBuildResult(
                        root.FullName,
                        moduleName,
                        synchronizedVersion,
                        packageOutput,
                        request,
                        configPath));
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
                    new ConfigurationOptionsSegment
                    {
                        Options = new ConfigurationOptions
                        {
                            Delivery = new DeliveryOptionsConfiguration
                            {
                                Enable = true,
                                InternalsPath = Path.Combine("Payload", synchronizedVersion)
                            }
                        }
                    },
                    CreateProjectBuildSegment(moduleName, enabled: true, buildBeforeModule: true),
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Unpacked,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = Path.Combine("Payload", "<ModuleVersion>")
                        }
                    },
                    CreateSynchronizedReleaseSegment(moduleName)
                }
            };

            var exception = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));

            Assert.Contains("Delivery configuration is unsafe", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("overlaps artefact output root", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(packagePath), "The synchronized path guard should stop before artefact creation.");
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void SyncSourceProjectVersionIfRequested_PreservesPrereleaseVersion()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            const string numericVersion = "2.0.11";
            const string preRelease = "beta.2";
            WriteMinimalModule(root.FullName, moduleName, numericVersion);
            var sourceProjectPath = Path.Combine(root.FullName, $"{moduleName}.csproj");
            File.WriteAllText(
                sourceProjectPath,
                "<Project><PropertyGroup><Version>2.0.10</Version></PropertyGroup></Project>");
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = numericVersion,
                    CsprojPath = sourceProjectPath
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildSegment
                    {
                        BuildModule = new BuildModuleConfiguration
                        {
                            SyncNETProjectVersion = true
                        }
                    },
                    new ConfigurationManifestSegment
                    {
                        Configuration = new ManifestConfiguration
                        {
                            ModuleVersion = numericVersion,
                            Prerelease = preRelease
                        }
                    }
                }
            };
            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);

            runner.SyncSourceProjectVersionIfRequested(plan);

            Assert.True(CsprojVersionEditor.TryGetVersion(sourceProjectPath, out var synchronizedVersion));
            Assert.Equal($"{numericVersion}-{preRelease}", synchronizedVersion);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_IgnoresDisabledSelectedLaneWhenValidatingSynchronizedOrdering()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var stagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            const string synchronizedVersion = "2.0.13";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            WriteProjectBuildConfig(root.FullName, Path.Combine("Build", "project.build.json"));
            var packageOutput = Path.Combine(root.FullName, "Artifacts", "NuGet");
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                powerShellRunner: null,
                moduleDependencyMetadataProvider: null,
                hostedOperations: null,
                manifestMutator: null,
                missingFunctionAnalysisService: null,
                scriptFunctionExportDetector: null,
                packageBuildExecutor: (request, configuration, configPath) =>
                    CreateProjectBuildResult(
                        root.FullName,
                        moduleName,
                        synchronizedVersion,
                        packageOutput,
                        request,
                        configPath));
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "2.0.10",
                    StagingPath = stagingPath
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    CreateProjectBuildSegment(moduleName, enabled: true, buildBeforeModule: true),
                    CreateProjectBuildSegment("Disabled", enabled: false, buildBeforeModule: false),
                    CreateSynchronizedReleaseSegment(moduleName)
                }
            };

            var result = runner.Run(spec);

            Assert.Equal(synchronizedVersion, result.Plan.ResolvedVersion);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, recursive: true); } catch { }
        }
    }

    private static ConfigurationProjectBuildSegment CreateProjectBuildSegment(
        string name,
        bool enabled,
        bool buildBeforeModule,
        string? configPath = null,
        bool useAsReleaseVersionSource = true)
        => new()
        {
            Configuration = new ProjectBuildConfigurationReference
            {
                Enabled = enabled,
                Name = name,
                ConfigPath = configPath ?? Path.Combine("Build", "project.build.json"),
                BuildBeforeModule = buildBeforeModule,
                UseAsReleaseVersionSource = useAsReleaseVersionSource
            }
        };

    private static ModulePipelineSpec CreateResumableReleaseSpec(
        string rootPath,
        string stagingPath,
        string moduleName)
        => new()
        {
            Build = new ModuleBuildSpec
            {
                Name = moduleName,
                SourcePath = rootPath,
                Version = "2.0.10",
                StagingPath = stagingPath
            },
            Install = new ModulePipelineInstallOptions { Enabled = false },
            Segments = new IConfigurationSegment[]
            {
                CreateProjectBuildSegment(moduleName, enabled: true, buildBeforeModule: true),
                CreateProjectBuildSegment(
                    "Companion",
                    enabled: true,
                    buildBeforeModule: false,
                    configPath: Path.Combine("Build", "companion.build.json"),
                    useAsReleaseVersionSource: false),
                CreateProjectBuildSegment(
                    "Companion",
                    enabled: true,
                    buildBeforeModule: false,
                    configPath: Path.Combine("Build", "companion-extra.build.json"),
                    useAsReleaseVersionSource: false),
                new ConfigurationPublishSegment
                {
                    Configuration = new PublishConfiguration
                    {
                        Enabled = true,
                        Destination = PublishDestination.PowerShellGallery,
                        RepositoryName = "TestRepository"
                    }
                },
                new ConfigurationReleaseSegment
                {
                    Configuration = new ReleaseConfiguration
                    {
                        VersionSource = ReleaseVersionSource.ProjectBuild,
                        PrimaryProject = moduleName,
                        SynchronizeModuleVersion = true,
                        PublishOrder = new[] { "NuGet", "PowerShellGallery" }
                    }
                }
            }
        };

    private static ModulePipelineSpec CreatePostPublishFailureSpec(
        string rootPath,
        string stagingPath,
        string moduleName,
        string repositoryName,
        bool synchronizeModuleVersion = true)
        => new()
        {
            Build = new ModuleBuildSpec
            {
                Name = moduleName,
                SourcePath = rootPath,
                Version = "2.0.10",
                StagingPath = stagingPath
            },
            Install = new ModulePipelineInstallOptions { Enabled = false },
            Segments = new IConfigurationSegment[]
            {
                CreateProjectBuildSegment(moduleName, enabled: true, buildBeforeModule: true),
                new ConfigurationPublishSegment
                {
                    Configuration = new PublishConfiguration
                    {
                        Enabled = true,
                        Destination = PublishDestination.PowerShellGallery,
                        RepositoryName = repositoryName
                    }
                },
                new ConfigurationActionSegment
                {
                    Configuration = new ModulePipelineActionConfiguration
                    {
                        Enabled = true,
                        Name = "PostPublish",
                        At = ModulePipelineActionStage.AfterPublish,
                        InlineScript = "Write-Output ignored"
                    }
                },
                new ConfigurationReleaseSegment
                {
                    Configuration = new ReleaseConfiguration
                    {
                        VersionSource = ReleaseVersionSource.ProjectBuild,
                        PrimaryProject = moduleName,
                        SynchronizeModuleVersion = synchronizeModuleVersion,
                        PublishOrder = new[] { "NuGet", "PowerShellGallery" }
                    }
                }
            }
        };

    private static ConfigurationReleaseSegment CreateSynchronizedReleaseSegment(string primaryProject)
        => new()
        {
            Configuration = new ReleaseConfiguration
            {
                VersionSource = ReleaseVersionSource.ProjectBuild,
                PrimaryProject = primaryProject,
                SynchronizeModuleVersion = true
            }
        };

    private static ProjectBuildHostExecutionResult CreateProjectBuildResult(
        string rootPath,
        string projectName,
        string version,
        string packageOutput,
        ProjectBuildHostRequest request,
        string? configPath,
        bool includePackage = true)
    {
        Directory.CreateDirectory(packageOutput);
        var packagePath = Path.Combine(packageOutput, $"{projectName}.{version}.nupkg");
        if (includePackage)
            File.WriteAllText(packagePath, "package");
        var release = new DotNetRepositoryReleaseResult
        {
            Success = true,
            ResolvedVersion = version
        };
        release.ResolvedVersionsByProject[projectName] = version;
        var project = new DotNetRepositoryProjectResult
        {
            ProjectName = projectName,
            PackageId = projectName,
            IsPackable = true,
            NewVersion = version
        };
        if (includePackage)
            project.Packages.Add(packagePath);
        release.Projects.Add(project);
        return new ProjectBuildHostExecutionResult
        {
            Success = true,
            ConfigPath = configPath ?? request.ConfigPath,
            RootPath = rootPath,
            OutputPath = packageOutput,
            Result = new ProjectBuildResult
            {
                Success = true,
                Release = release
            }
        };
    }
}
