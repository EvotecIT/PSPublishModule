using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineUnifiedReleaseTests
{
    [Fact]
    public void Run_DoesNotTrustUnattemptedExistingModuleVersionOnResume()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var firstStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        var secondStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            const string synchronizedVersion = "2.0.11";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            WriteSynchronizedProjectBuildConfig(root.FullName, "project.build.json", moduleName, publishNuGet: false);

            var existingPolicies = new List<bool>();
            var hosted = new FakeHostedOperations(new List<string>())
            {
                ModulePublishVersionPreflightWithExistingPolicy = (_, _, allowExistingExactVersion) =>
                {
                    existingPolicies.Add(allowExistingExactVersion);
                    if (!allowExistingExactVersion)
                    {
                        throw new InvalidOperationException("The synchronized module version already exists.");
                    }

                    return ModulePublishVersionPreflightResult.AlreadyPublished;
                }
            };

            ProjectBuildHostExecutionResult ExecutePackageBuild(
                ProjectBuildHostRequest request,
                ProjectBuildConfiguration? configuration,
                string? configPath)
                => CreateProjectBuildResult(
                    root.FullName,
                    moduleName,
                    synchronizedVersion,
                    Path.Combine(root.FullName, "Artifacts", "NuGet"),
                    request,
                    configPath,
                    includePackage: false);

            var firstRunner = CreateRunner(hosted, ExecutePackageBuild);
            var firstException = Assert.Throws<InvalidOperationException>(() => firstRunner.Run(
                CreateGalleryReleaseSpec(root.FullName, firstStagingPath, moduleName)));
            Assert.Contains("already exists", firstException.Message, StringComparison.OrdinalIgnoreCase);

            var secondRunner = CreateRunner(hosted, ExecutePackageBuild);
            var secondException = Assert.Throws<InvalidOperationException>(() => secondRunner.Run(
                CreateGalleryReleaseSpec(root.FullName, secondStagingPath, moduleName)));

            Assert.Contains("already exists", secondException.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(new[] { false, false }, existingPolicies);
            Assert.Empty(hosted.PublishedModuleVersions);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(firstStagingPath)) Directory.Delete(firstStagingPath, recursive: true); } catch { }
            try { if (Directory.Exists(secondStagingPath)) Directory.Delete(secondStagingPath, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_KeepsCheckpointOutsideClearableArtefactRoot()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var stagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            const string synchronizedVersion = "2.0.11";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            WriteSynchronizedProjectBuildConfig(root.FullName, "project.build.json", moduleName, publishNuGet: false);

            var hosted = new FakeHostedOperations(new List<string>())
            {
                ModulePublishAction = (_, _) => throw new InvalidOperationException("Simulated gallery outage.")
            };
            var runner = CreateRunner(
                hosted,
                (request, configuration, configPath) => CreateProjectBuildResult(
                    root.FullName,
                    moduleName,
                    synchronizedVersion,
                    Path.Combine(root.FullName, "PackageOutput", "NuGet"),
                    request,
                    configPath,
                    includePackage: false));
            var spec = CreateGalleryReleaseSpec(root.FullName, stagingPath, moduleName);
            spec.Segments = spec.Segments
                .Prepend(new ConfigurationArtefactSegment
                {
                    ArtefactType = ArtefactType.Unpacked,
                    Configuration = new ArtefactConfiguration
                    {
                        Enabled = true,
                        Path = "Artefacts"
                    }
                })
                .ToArray();

            var exception = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));

            Assert.Contains("gallery outage", exception.Message, StringComparison.OrdinalIgnoreCase);
            var checkpointRoot = GetCoordinatedReleaseCheckpointRoot(root.FullName);
            Assert.Single(Directory.GetFiles(checkpointRoot, "*.json"));
            Assert.False(Directory.Exists(Path.Combine(root.FullName, "Artefacts", ".powerforge")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_SkipsCompletedModuleGitHubPublishAfterPostPublishFailure()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var firstStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        var secondStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            const string synchronizedVersion = "2.0.11";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            WriteSynchronizedProjectBuildConfig(root.FullName, "project.build.json", moduleName, publishNuGet: false);

            var gitHubPublishCount = 0;
            ProjectBuildHostExecutionResult ExecutePackageBuild(
                ProjectBuildHostRequest request,
                ProjectBuildConfiguration? configuration,
                string? configPath)
                => CreateProjectBuildResult(
                    root.FullName,
                    moduleName,
                    synchronizedVersion,
                    Path.Combine(root.FullName, "Artifacts", "NuGet"),
                    request,
                    configPath,
                    includePackage: false);
            GitHubReleasePublishResult PublishGitHub(GitHubReleasePublishRequest request)
            {
                gitHubPublishCount++;
                return new GitHubReleasePublishResult
                {
                    Succeeded = true,
                    ReleaseCreationSucceeded = true,
                    AllAssetUploadsSucceeded = true,
                    HtmlUrl = $"https://github.com/EvotecIT/{moduleName}/releases/tag/{request.TagName}"
                };
            }

            var firstHosted = new FakeHostedOperations(new List<string>())
            {
                ModuleAction = (action, context) => CreateActionResult(action, context, succeeded: false)
            };
            var firstRunner = CreateRunner(firstHosted, ExecutePackageBuild, PublishGitHub);
            var firstException = Assert.Throws<InvalidOperationException>(() => firstRunner.Run(
                CreateGitHubPostPublishSpec(root.FullName, firstStagingPath, moduleName)));
            Assert.Contains("post-publish failure", firstException.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, gitHubPublishCount);

            var secondHosted = new FakeHostedOperations(new List<string>())
            {
                ModuleAction = (action, context) => CreateActionResult(action, context, succeeded: true)
            };
            var secondRunner = CreateRunner(secondHosted, ExecutePackageBuild, PublishGitHub);
            var result = secondRunner.Run(CreateGitHubPostPublishSpec(root.FullName, secondStagingPath, moduleName));

            Assert.Equal(synchronizedVersion, result.Plan.ResolvedVersion);
            Assert.Equal(1, gitHubPublishCount);
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
    public void Run_CheckpointsEachPackagePublishOperationBeforeAdvancing()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var firstStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        var secondStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            const string companionName = "Companion";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            WriteSynchronizedProjectBuildConfig(root.FullName, "source.build.json", moduleName, publishNuGet: true);
            WriteSynchronizedProjectBuildConfig(root.FullName, "companion.build.json", companionName, publishNuGet: true);

            var sourcePublishCount = 0;
            var companionPublishCount = 0;
            var failCompanionPublish = true;
            ProjectBuildHostExecutionResult ExecutePackageBuild(
                ProjectBuildHostRequest request,
                ProjectBuildConfiguration? configuration,
                string? configPath)
            {
                var companion = configPath?.EndsWith("companion.build.json", StringComparison.OrdinalIgnoreCase) == true;
                var projectName = companion ? companionName : moduleName;
                var version = companion ? "5.0.7" : "2.0.11";
                var result = CreateProjectBuildResult(
                    root.FullName,
                    projectName,
                    version,
                    Path.Combine(root.FullName, "Artifacts", projectName),
                    request,
                    configPath,
                    includePackage: false);
                if (request.PublishNuget == true)
                {
                    if (companion)
                    {
                        companionPublishCount++;
                        if (failCompanionPublish)
                        {
                            failCompanionPublish = false;
                            result.Success = false;
                            result.ErrorMessage = "Simulated companion NuGet failure.";
                        }
                    }
                    else
                    {
                        sourcePublishCount++;
                    }
                }

                return result;
            }

            var firstRunner = CreateRunner(new FakeHostedOperations(new List<string>()), ExecutePackageBuild);
            var firstException = Assert.Throws<InvalidOperationException>(() => firstRunner.Run(
                CreatePackageOnlyReleaseSpec(root.FullName, firstStagingPath, moduleName, companionName)));
            Assert.Contains("companion NuGet failure", firstException.Message, StringComparison.OrdinalIgnoreCase);

            var secondRunner = CreateRunner(new FakeHostedOperations(new List<string>()), ExecutePackageBuild);
            var result = secondRunner.Run(
                CreatePackageOnlyReleaseSpec(root.FullName, secondStagingPath, moduleName, companionName));

            Assert.Equal("2.0.11", result.Plan.ResolvedVersion);
            Assert.Equal(1, sourcePublishCount);
            Assert.Equal(2, companionPublishCount);
            AssertNoCoordinatedReleaseCheckpoint(root.FullName);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(firstStagingPath)) Directory.Delete(firstStagingPath, recursive: true); } catch { }
            try { if (Directory.Exists(secondStagingPath)) Directory.Delete(secondStagingPath, recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_RetainsFailedCompanionLaneVersionForRetry(bool companionBuildBeforeModule)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var firstStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        var secondStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            const string companionName = "Companion";
            const string companionVersion = "5.0.7";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            WriteSynchronizedProjectBuildConfig(root.FullName, "source.build.json", moduleName, publishNuGet: false);
            WriteSynchronizedProjectBuildConfig(root.FullName, "companion.build.json", companionName, publishNuGet: false);

            var runNumber = 1;
            var resumedCompanionUsedExactVersion = false;
            ProjectBuildHostExecutionResult ExecutePackageBuild(
                ProjectBuildHostRequest request,
                ProjectBuildConfiguration? configuration,
                string? configPath)
            {
                var companion = configPath?.EndsWith("companion.build.json", StringComparison.OrdinalIgnoreCase) == true;
                var projectName = companion ? companionName : moduleName;
                var version = companion ? companionVersion : "2.0.11";
                if (runNumber == 2 && companion && request.PublishNuget != true)
                {
                    resumedCompanionUsedExactVersion = configuration?.ExpectedVersionMap is not null &&
                        configuration.ExpectedVersionMap.TryGetValue(companionName, out var expected) &&
                        string.Equals(expected, companionVersion, StringComparison.OrdinalIgnoreCase);
                }

                var result = CreateProjectBuildResult(
                    root.FullName,
                    projectName,
                    version,
                    Path.Combine(root.FullName, "Artifacts", projectName),
                    request,
                    configPath,
                    includePackage: false);
                result.Result.Release!.Projects.Add(new DotNetRepositoryProjectResult
                {
                    ProjectName = $"{projectName}.Tests",
                    PackageId = $"{projectName}.Tests",
                    IsPackable = false
                });
                if (runNumber == 1 && companion && request.PublishNuget != true)
                {
                    result.Success = false;
                    result.ErrorMessage = "Simulated companion build failure.";
                }

                return result;
            }

            var firstRunner = CreateRunner(new FakeHostedOperations(new List<string>()), ExecutePackageBuild);
            var firstException = Assert.Throws<InvalidOperationException>(() => firstRunner.Run(
                CreateCompanionFailureSpec(
                    root.FullName,
                    firstStagingPath,
                    moduleName,
                    companionName,
                    companionBuildBeforeModule)));
            Assert.Contains("companion build failure", firstException.Message, StringComparison.OrdinalIgnoreCase);

            runNumber = 2;
            var secondRunner = CreateRunner(new FakeHostedOperations(new List<string>()), ExecutePackageBuild);
            var result = secondRunner.Run(
                CreateCompanionFailureSpec(
                    root.FullName,
                    secondStagingPath,
                    moduleName,
                    companionName,
                    companionBuildBeforeModule));

            Assert.Equal("2.0.11", result.Plan.ResolvedVersion);
            Assert.True(resumedCompanionUsedExactVersion);
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
    public void Run_RejectsRetryWhenAttemptedLaneHasNoVersionEvidence()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var firstStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        var secondStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            WriteSynchronizedProjectBuildConfig(root.FullName, "project.build.json", moduleName, publishNuGet: false);

            var firstRunner = CreateRunner(
                new FakeHostedOperations(new List<string>()),
                (request, configuration, configPath) => throw new InvalidOperationException("Simulated interrupted package lane."));
            var firstException = Assert.Throws<InvalidOperationException>(() => firstRunner.Run(
                CreateGalleryReleaseSpec(root.FullName, firstStagingPath, moduleName)));
            Assert.Contains("interrupted package lane", firstException.Message, StringComparison.OrdinalIgnoreCase);

            var retryExecutorCalls = 0;
            var secondRunner = CreateRunner(
                new FakeHostedOperations(new List<string>()),
                (request, configuration, configPath) =>
                {
                    retryExecutorCalls++;
                    return CreateProjectBuildResult(
                        root.FullName,
                        moduleName,
                        "2.0.11",
                        Path.Combine(root.FullName, "Artifacts", "NuGet"),
                        request,
                        configPath,
                        includePackage: false);
                });
            var secondException = Assert.Throws<InvalidOperationException>(() => secondRunner.Run(
                CreateGalleryReleaseSpec(root.FullName, secondStagingPath, moduleName)));

            Assert.Contains("without exact version state", secondException.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, retryExecutorCalls);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(firstStagingPath)) Directory.Delete(firstStagingPath, recursive: true); } catch { }
            try { if (Directory.Exists(secondStagingPath)) Directory.Delete(secondStagingPath, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_RejectsRetryWhenFailedLaneHasOnlyPartialProjectVersionState()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var firstStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        var secondStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            WriteSynchronizedProjectBuildConfig(root.FullName, "project.build.json", moduleName, publishNuGet: false);

            var firstRunner = CreateRunner(
                new FakeHostedOperations(new List<string>()),
                (request, configuration, configPath) =>
                {
                    var result = CreateProjectBuildResult(
                        root.FullName,
                        moduleName,
                        "2.0.11",
                        Path.Combine(root.FullName, "Artifacts", "NuGet"),
                        request,
                        configPath,
                        includePackage: false);
                    result.Result.Release!.Projects.Add(new DotNetRepositoryProjectResult
                    {
                        ProjectName = "Companion",
                        PackageId = "Companion",
                        IsPackable = true
                    });
                    result.Success = false;
                    result.ErrorMessage = "Simulated partial multi-project failure.";
                    return result;
                });
            var firstException = Assert.Throws<InvalidOperationException>(() => firstRunner.Run(
                CreateGalleryReleaseSpec(root.FullName, firstStagingPath, moduleName)));
            Assert.Contains("partial multi-project failure", firstException.Message, StringComparison.OrdinalIgnoreCase);

            var retryExecutorCalls = 0;
            var secondRunner = CreateRunner(
                new FakeHostedOperations(new List<string>()),
                (request, configuration, configPath) =>
                {
                    retryExecutorCalls++;
                    return CreateProjectBuildResult(
                        root.FullName,
                        moduleName,
                        "2.0.11",
                        Path.Combine(root.FullName, "Artifacts", "NuGet"),
                        request,
                        configPath,
                        includePackage: false);
                });
            var secondException = Assert.Throws<InvalidOperationException>(() => secondRunner.Run(
                CreateGalleryReleaseSpec(root.FullName, secondStagingPath, moduleName)));

            Assert.Contains("without exact version state", secondException.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, retryExecutorCalls);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(firstStagingPath)) Directory.Delete(firstStagingPath, recursive: true); } catch { }
            try { if (Directory.Exists(secondStagingPath)) Directory.Delete(secondStagingPath, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_RejectsChangedPayloadBeforeResumingRemainingPublishes()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var firstStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        var secondStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            WriteSynchronizedProjectBuildConfig(root.FullName, "project.build.json", moduleName, publishNuGet: false);

            var gitHubPublishCount = 0;
            ProjectBuildHostExecutionResult ExecutePackageBuild(
                ProjectBuildHostRequest request,
                ProjectBuildConfiguration? configuration,
                string? configPath)
                => CreateProjectBuildResult(
                    root.FullName,
                    moduleName,
                    "2.0.11",
                    Path.Combine(root.FullName, "Artifacts", "NuGet"),
                    request,
                    configPath,
                    includePackage: false);
            GitHubReleasePublishResult PublishGitHub(GitHubReleasePublishRequest request)
            {
                gitHubPublishCount++;
                return new GitHubReleasePublishResult
                {
                    Succeeded = true,
                    ReleaseCreationSucceeded = true,
                    AllAssetUploadsSucceeded = true,
                    HtmlUrl = $"https://github.com/EvotecIT/{moduleName}/releases/tag/{request.TagName}"
                };
            }

            var firstHosted = new FakeHostedOperations(new List<string>())
            {
                ModuleAction = (action, context) => CreateActionResult(action, context, succeeded: false)
            };
            var firstRunner = CreateRunner(firstHosted, ExecutePackageBuild, PublishGitHub);
            Assert.Throws<InvalidOperationException>(() => firstRunner.Run(
                CreateGitHubPostPublishSpec(root.FullName, firstStagingPath, moduleName)));
            Assert.Equal(1, gitHubPublishCount);

            File.WriteAllText(
                Path.Combine(root.FullName, $"{moduleName}.psm1"),
                "function Get-Test { 'changed' }");
            var secondHosted = new FakeHostedOperations(new List<string>())
            {
                ModuleAction = (action, context) => CreateActionResult(action, context, succeeded: true)
            };
            var secondRunner = CreateRunner(secondHosted, ExecutePackageBuild, PublishGitHub);
            var secondException = Assert.Throws<InvalidOperationException>(() => secondRunner.Run(
                CreateGitHubPostPublishSpec(root.FullName, secondStagingPath, moduleName)));

            Assert.Contains("source differs", secondException.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"source/{moduleName}.psm1", secondException.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, gitHubPublishCount);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(firstStagingPath)) Directory.Delete(firstStagingPath, recursive: true); } catch { }
            try { if (Directory.Exists(secondStagingPath)) Directory.Delete(secondStagingPath, recursive: true); } catch { }
        }
    }

    private static ModulePipelineRunner CreateRunner(
        FakeHostedOperations hosted,
        ModulePipelineRunnerDefaults.ModulePackageBuildExecutor packageBuildExecutor,
        ModulePipelineRunnerDefaults.ModuleGitHubReleasePublisher? gitHubReleasePublisher = null)
        => new(
            new NullLogger(),
            powerShellRunner: null,
            moduleDependencyMetadataProvider: null,
            hostedOperations: hosted,
            manifestMutator: null,
            missingFunctionAnalysisService: null,
            scriptFunctionExportDetector: null,
            packageBuildExecutor: packageBuildExecutor,
            gitHubReleasePublisher: gitHubReleasePublisher);

    private static void WriteSynchronizedProjectBuildConfig(
        string rootPath,
        string fileName,
        string projectName,
        bool publishNuGet,
        bool? skipDuplicate = null)
    {
        var path = Path.Combine(rootPath, "Build", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var skipDuplicateProperty = skipDuplicate.HasValue
            ? $",\"SkipDuplicate\":{skipDuplicate.Value.ToString().ToLowerInvariant()}"
            : string.Empty;
        File.WriteAllText(
            path,
            $"{{\"RootPath\":\"Sources\",\"ExpectedVersionMap\":{{\"{projectName}\":\"2.0.X\"}},\"ExpectedVersionMapAsInclude\":true,\"UpdateVersions\":true,\"Build\":true,\"PublishNuget\":{publishNuGet.ToString().ToLowerInvariant()},\"PublishGitHub\":false{skipDuplicateProperty}}}");
    }

    private static ModulePipelineSpec CreateGalleryReleaseSpec(
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
                CreateProjectBuildSegment(
                    moduleName,
                    enabled: true,
                    buildBeforeModule: true,
                    configPath: Path.Combine("Build", "project.build.json")),
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
                        PublishOrder = new[] { "PowerShellGallery" }
                    }
                }
            }
        };

    private static ModulePipelineSpec CreateGitHubPostPublishSpec(
        string rootPath,
        string stagingPath,
        string moduleName)
    {
        var spec = CreateGalleryReleaseSpec(rootPath, stagingPath, moduleName);
        spec.Segments = new IConfigurationSegment[]
        {
            CreateProjectBuildSegment(
                moduleName,
                enabled: true,
                buildBeforeModule: true,
                configPath: Path.Combine("Build", "project.build.json")),
            new ConfigurationArtefactSegment
            {
                ArtefactType = ArtefactType.Packed,
                Configuration = new ArtefactConfiguration
                {
                    Enabled = true,
                    Path = Path.Combine(rootPath, "Artifacts", "Module"),
                    ArtefactName = $"{moduleName}.zip"
                }
            },
            new ConfigurationPublishSegment
            {
                Configuration = new PublishConfiguration
                {
                    Enabled = true,
                    Destination = PublishDestination.GitHub,
                    UserName = "EvotecIT",
                    RepositoryName = moduleName,
                    ApiKey = "test-token"
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
                    SynchronizeModuleVersion = true,
                    PublishOrder = new[] { "GitHub" }
                }
            }
        };
        return spec;
    }

    private static ModulePipelineSpec CreatePackageOnlyReleaseSpec(
        string rootPath,
        string stagingPath,
        string moduleName,
        string companionName,
        bool companionBuildBeforeModule = false)
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
                CreateProjectBuildSegment(
                    moduleName,
                    enabled: true,
                    buildBeforeModule: true,
                    configPath: Path.Combine("Build", "source.build.json")),
                CreateProjectBuildSegment(
                    companionName,
                    enabled: true,
                    buildBeforeModule: companionBuildBeforeModule,
                    configPath: Path.Combine("Build", "companion.build.json"),
                    useAsReleaseVersionSource: false),
                new ConfigurationReleaseSegment
                {
                    Configuration = new ReleaseConfiguration
                    {
                        VersionSource = ReleaseVersionSource.ProjectBuild,
                        PrimaryProject = moduleName,
                        SynchronizeModuleVersion = true,
                        PublishOrder = new[] { "NuGet" }
                    }
                }
            }
        };

    private static ModulePipelineSpec CreateCompanionFailureSpec(
        string rootPath,
        string stagingPath,
        string moduleName,
        string companionName,
        bool companionBuildBeforeModule)
    {
        var spec = CreatePackageOnlyReleaseSpec(
            rootPath,
            stagingPath,
            moduleName,
            companionName,
            companionBuildBeforeModule);
        var segments = spec.Segments.ToList();
        segments.Insert(
            segments.Count - 1,
            new ConfigurationPublishSegment
            {
                Configuration = new PublishConfiguration
                {
                    Enabled = true,
                    Destination = PublishDestination.PowerShellGallery,
                    RepositoryName = "TestRepository"
                }
            });
        var release = Assert.IsType<ConfigurationReleaseSegment>(segments[^1]);
        release.Configuration.PublishOrder = new[] { "PowerShellGallery" };
        spec.Segments = segments.ToArray();
        return spec;
    }

    private static ModulePipelineActionResult CreateActionResult(
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
}
