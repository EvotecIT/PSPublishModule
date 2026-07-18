using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineUnifiedReleaseTests
{
    [Fact]
    public void Run_UsesDuplicateTolerantNuGetRetryForAttemptedPackageLane()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var firstStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        var secondStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            WriteSynchronizedProjectBuildConfig(
                root.FullName,
                "project.build.json",
                moduleName,
                publishNuGet: true,
                skipDuplicate: false);

            var runNumber = 1;
            var publishAttempts = 0;
            var firstAttemptPreservedConfiguration = false;
            var retryUsedDuplicateTolerance = false;
            ProjectBuildHostExecutionResult ExecutePackageBuild(
                ProjectBuildHostRequest request,
                ProjectBuildConfiguration? configuration,
                string? configPath)
            {
                var result = CreateProjectBuildResult(
                    root.FullName,
                    moduleName,
                    "2.0.11",
                    Path.Combine(root.FullName, "Artifacts", "NuGet"),
                    request,
                    configPath,
                    includePackage: true);
                if (request.PublishNuget == true)
                {
                    request.RemotePublishAttempted?.Invoke();
                    publishAttempts++;
                    if (runNumber == 1)
                    {
                        firstAttemptPreservedConfiguration = configuration?.SkipDuplicate == false;
                        result.Success = false;
                        result.ErrorMessage = "Simulated second-package failure after the first package was published.";
                    }
                    else
                    {
                        retryUsedDuplicateTolerance = configuration?.SkipDuplicate == true;
                    }
                }

                return result;
            }

            var firstRunner = CreateRunner(new FakeHostedOperations(new List<string>()), ExecutePackageBuild);
            var firstException = Assert.Throws<InvalidOperationException>(() => firstRunner.Run(
                CreateNuGetOnlyReleaseSpec(root.FullName, firstStagingPath, moduleName)));
            Assert.Contains("second-package failure", firstException.Message, StringComparison.OrdinalIgnoreCase);

            runNumber = 2;
            var secondRunner = CreateRunner(new FakeHostedOperations(new List<string>()), ExecutePackageBuild);
            var result = secondRunner.Run(CreateNuGetOnlyReleaseSpec(root.FullName, secondStagingPath, moduleName));

            Assert.Equal("2.0.11", result.Plan.ResolvedVersion);
            Assert.True(firstAttemptPreservedConfiguration);
            Assert.True(retryUsedDuplicateTolerance);
            Assert.Equal(2, publishAttempts);
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
    public void Run_BlocksBuildGateWhileCoordinatedReleaseCheckpointIsIncomplete()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var firstStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        var buildStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            WriteSynchronizedProjectBuildConfig(root.FullName, "project.build.json", moduleName, publishNuGet: true);

            ProjectBuildHostExecutionResult FailPackagePublish(
                ProjectBuildHostRequest request,
                ProjectBuildConfiguration? configuration,
                string? configPath)
            {
                var result = CreateProjectBuildResult(
                    root.FullName,
                    moduleName,
                    "2.0.11",
                    Path.Combine(root.FullName, "Artifacts", "NuGet"),
                    request,
                    configPath,
                    includePackage: true);
                if (request.PublishNuget == true)
                {
                    request.RemotePublishAttempted?.Invoke();
                    result.Success = false;
                    result.ErrorMessage = "Simulated package publish failure.";
                }

                return result;
            }

            var firstRunner = CreateRunner(new FakeHostedOperations(new List<string>()), FailPackagePublish);
            Assert.Throws<InvalidOperationException>(() => firstRunner.Run(
                CreateNuGetOnlyReleaseSpec(root.FullName, firstStagingPath, moduleName)));

            var packageBuildCalled = false;
            var buildRunner = CreateRunner(
                new FakeHostedOperations(new List<string>()),
                (request, configuration, configPath) =>
                {
                    packageBuildCalled = true;
                    return CreateProjectBuildResult(
                        root.FullName,
                        moduleName,
                        "2.0.12",
                        Path.Combine(root.FullName, "Artifacts", "NuGet"),
                        request,
                        configPath,
                        includePackage: true);
                });
            var buildSpec = CreateNuGetOnlyReleaseSpec(root.FullName, buildStagingPath, moduleName);
            buildSpec.Segments = buildSpec.Segments
                .Prepend(new ConfigurationGateSegment
                {
                    Configuration = new GateConfiguration { Mode = ConfigurationGateMode.Build }
                })
                .ToArray();

            var exception = Assert.Throws<InvalidOperationException>(() => buildRunner.Run(buildSpec));

            Assert.Contains("Gate mode Build", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("incomplete", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(packageBuildCalled);
            Assert.Single(Directory.GetFiles(GetCoordinatedReleaseCheckpointRoot(root.FullName), "*.json"));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(firstStagingPath)) Directory.Delete(firstStagingPath, recursive: true); } catch { }
            try { if (Directory.Exists(buildStagingPath)) Directory.Delete(buildStagingPath, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_BuildGateDiscardsPristineCoordinatedReleaseCheckpoint()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var firstStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        var buildStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            WriteSynchronizedProjectBuildConfig(root.FullName, "project.build.json", moduleName, publishNuGet: true);

            var packageBuildCalled = false;
            var firstRunner = CreateRunner(
                new FakeHostedOperations(new List<string>()),
                (request, configuration, configPath) =>
                {
                    packageBuildCalled = true;
                    return CreateProjectBuildResult(
                        root.FullName,
                        moduleName,
                        "2.0.11",
                        Path.Combine(root.FullName, "Artifacts", "NuGet"),
                        request,
                        configPath,
                        includePackage: true);
                });
            var invalidSpec = CreateNuGetOnlyReleaseSpec(root.FullName, firstStagingPath, moduleName);
            invalidSpec.Segments = invalidSpec.Segments
                .Prepend(new ConfigurationPackageBuildSegment
                {
                    Configuration = new PackageBuildConfiguration
                    {
                        Name = "InvalidCompanion",
                        RootPath = "Sources",
                        BuildBeforeModule = false,
                        ProvideLocalNuGetFeed = true
                    }
                })
                .ToArray();

            var firstException = Assert.Throws<InvalidOperationException>(() => firstRunner.Run(invalidSpec));
            Assert.Contains("ProvideLocalNuGetFeed", firstException.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(packageBuildCalled);
            Assert.Single(Directory.GetFiles(GetCoordinatedReleaseCheckpointRoot(root.FullName), "*.json"));

            var buildSpec = CreateNuGetOnlyReleaseSpec(root.FullName, buildStagingPath, moduleName);
            buildSpec.Segments = buildSpec.Segments
                .Prepend(new ConfigurationGateSegment
                {
                    Configuration = new GateConfiguration { Mode = ConfigurationGateMode.Build }
                })
                .ToArray();
            var buildRunner = CreateRunner(
                new FakeHostedOperations(new List<string>()),
                (request, configuration, configPath) =>
                {
                    packageBuildCalled = true;
                    return CreateProjectBuildResult(
                        root.FullName,
                        moduleName,
                        "2.0.11",
                        Path.Combine(root.FullName, "Artifacts", "NuGet"),
                        request,
                        configPath,
                        includePackage: true);
                });

            var result = buildRunner.Run(buildSpec);

            Assert.Equal(ConfigurationGateMode.Build, result.Plan.GateMode);
            Assert.True(packageBuildCalled);
            AssertNoCoordinatedReleaseCheckpoint(root.FullName);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(firstStagingPath)) Directory.Delete(firstStagingPath, recursive: true); } catch { }
            try { if (Directory.Exists(buildStagingPath)) Directory.Delete(buildStagingPath, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_DoesNotAuthorizeExistingModuleVersionAfterLocalPublishFailure()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var firstStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        var secondStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            WriteSynchronizedProjectBuildConfig(root.FullName, "project.build.json", moduleName, publishNuGet: false);

            var firstHosted = new FakeHostedOperations(new List<string>())
            {
                ModulePublishPreflightAction = (_, _) => throw new InvalidOperationException("Simulated missing publish credential.")
            };
            var firstRunner = CreateRunner(
                firstHosted,
                (request, configuration, configPath) => CreateProjectBuildResult(
                    root.FullName,
                    moduleName,
                    "2.0.11",
                    Path.Combine(root.FullName, "Artifacts", "NuGet"),
                    request,
                    configPath,
                    includePackage: false));

            var firstException = Assert.Throws<InvalidOperationException>(() => firstRunner.Run(
                CreateGalleryReleaseSpec(root.FullName, firstStagingPath, moduleName)));
            Assert.Contains("missing publish credential", firstException.Message, StringComparison.OrdinalIgnoreCase);

            var existingPolicies = new List<bool>();
            var secondHosted = new FakeHostedOperations(new List<string>())
            {
                ModulePublishVersionPreflightWithExistingPolicy = (_, _, allowExistingExactVersion) =>
                {
                    existingPolicies.Add(allowExistingExactVersion);
                    throw new InvalidOperationException("The synchronized module version already exists.");
                }
            };
            var secondRunner = CreateRunner(
                secondHosted,
                (request, configuration, configPath) => CreateProjectBuildResult(
                    root.FullName,
                    moduleName,
                    "2.0.11",
                    Path.Combine(root.FullName, "Artifacts", "NuGet"),
                    request,
                    configPath,
                    includePackage: false));

            var secondException = Assert.Throws<InvalidOperationException>(() => secondRunner.Run(
                CreateGalleryReleaseSpec(root.FullName, secondStagingPath, moduleName)));

            Assert.Contains("already exists", secondException.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(new[] { false }, existingPolicies);
            Assert.Empty(secondHosted.PublishedModuleVersions);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(firstStagingPath)) Directory.Delete(firstStagingPath, recursive: true); } catch { }
            try { if (Directory.Exists(secondStagingPath)) Directory.Delete(secondStagingPath, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_AuxiliaryRemoteSideEffectDoesNotAuthorizeExistingModuleVersion()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var firstStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        var secondStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            WriteSynchronizedProjectBuildConfig(root.FullName, "project.build.json", moduleName, publishNuGet: false);

            var firstHosted = new FakeHostedOperations(new List<string>())
            {
                ModulePublishRemoteSideEffectAction = observeRemoteSideEffect =>
                {
                    observeRemoteSideEffect?.Invoke();
                    throw new InvalidOperationException("Simulated failure after mirroring a required module.");
                }
            };
            var firstRunner = CreateRunner(
                firstHosted,
                (request, configuration, configPath) => CreateProjectBuildResult(
                    root.FullName,
                    moduleName,
                    "2.0.11",
                    Path.Combine(root.FullName, "Artifacts", "NuGet"),
                    request,
                    configPath,
                    includePackage: false));
            var firstSpec = CreateGalleryReleaseSpec(root.FullName, firstStagingPath, moduleName);
            Assert.IsType<ConfigurationPublishSegment>(firstSpec.Segments.Single(static segment => segment is ConfigurationPublishSegment))
                .Configuration.PublishRequiredModules = true;

            var firstException = Assert.Throws<InvalidOperationException>(() => firstRunner.Run(firstSpec));
            Assert.Contains("mirroring a required module", firstException.Message, StringComparison.OrdinalIgnoreCase);
            var checkpointPath = Assert.Single(Directory.GetFiles(GetCoordinatedReleaseCheckpointRoot(root.FullName), "*.json"));
            Assert.Contains("\"AuxiliaryRemoteSideEffectsObserved\": true", File.ReadAllText(checkpointPath), StringComparison.Ordinal);

            var existingPolicies = new List<bool>();
            var secondHosted = new FakeHostedOperations(new List<string>())
            {
                ModulePublishVersionPreflightWithExistingPolicy = (_, _, allowExistingExactVersion) =>
                {
                    existingPolicies.Add(allowExistingExactVersion);
                    throw new InvalidOperationException("The synchronized module version already exists.");
                }
            };
            var secondRunner = CreateRunner(
                secondHosted,
                (request, configuration, configPath) => CreateProjectBuildResult(
                    root.FullName,
                    moduleName,
                    "2.0.11",
                    Path.Combine(root.FullName, "Artifacts", "NuGet"),
                    request,
                    configPath,
                    includePackage: false));
            var secondSpec = CreateGalleryReleaseSpec(root.FullName, secondStagingPath, moduleName);
            Assert.IsType<ConfigurationPublishSegment>(secondSpec.Segments.Single(static segment => segment is ConfigurationPublishSegment))
                .Configuration.PublishRequiredModules = true;

            var secondException = Assert.Throws<InvalidOperationException>(() => secondRunner.Run(secondSpec));

            Assert.Contains("already exists", secondException.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(new[] { false }, existingPolicies);
            Assert.Empty(secondHosted.PublishedModuleVersions);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(firstStagingPath)) Directory.Delete(firstStagingPath, recursive: true); } catch { }
            try { if (Directory.Exists(secondStagingPath)) Directory.Delete(secondStagingPath, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_DoesNotEnableDuplicateToleranceAfterLocalPackagePublishFailure()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var firstStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        var secondStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            WriteSynchronizedProjectBuildConfig(
                root.FullName,
                "project.build.json",
                moduleName,
                publishNuGet: true,
                skipDuplicate: false);

            ProjectBuildHostExecutionResult FailBeforeRemotePublish(
                ProjectBuildHostRequest request,
                ProjectBuildConfiguration? configuration,
                string? configPath)
            {
                var result = CreateProjectBuildResult(
                    root.FullName,
                    moduleName,
                    "2.0.11",
                    Path.Combine(root.FullName, "Artifacts", "NuGet"),
                    request,
                    configPath,
                    includePackage: true);
                if (request.PublishNuget == true)
                {
                    result.Success = false;
                    result.ErrorMessage = "Simulated local publish preflight failure.";
                }

                return result;
            }

            var firstRunner = CreateRunner(new FakeHostedOperations(new List<string>()), FailBeforeRemotePublish);
            var firstException = Assert.Throws<InvalidOperationException>(() => firstRunner.Run(
                CreateNuGetOnlyReleaseSpec(root.FullName, firstStagingPath, moduleName)));
            Assert.Contains("local publish preflight failure", firstException.Message, StringComparison.OrdinalIgnoreCase);

            bool? retrySkipDuplicate = null;
            var secondRunner = CreateRunner(
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
                        includePackage: true);
                    if (request.PublishNuget == true)
                    {
                        retrySkipDuplicate = configuration?.SkipDuplicate;
                        request.RemotePublishAttempted?.Invoke();
                    }

                    return result;
                });

            secondRunner.Run(CreateNuGetOnlyReleaseSpec(root.FullName, secondStagingPath, moduleName));

            Assert.False(retrySkipDuplicate);
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
    [InlineData(ConfigurationGateMode.Manifest)]
    [InlineData(ConfigurationGateMode.Documentation)]
    public void Run_BlocksSourceMutatingGateWhileCoordinatedReleaseCheckpointIsIncomplete(ConfigurationGateMode gateMode)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var firstStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        var gateStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            WriteSynchronizedProjectBuildConfig(root.FullName, "project.build.json", moduleName, publishNuGet: true);

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
                        includePackage: true);
                    if (request.PublishNuget == true)
                    {
                        request.RemotePublishAttempted?.Invoke();
                        result.Success = false;
                        result.ErrorMessage = "Simulated remote package publish failure.";
                    }

                    return result;
                });
            Assert.Throws<InvalidOperationException>(() => firstRunner.Run(
                CreateNuGetOnlyReleaseSpec(root.FullName, firstStagingPath, moduleName)));

            var gateSpec = CreateNuGetOnlyReleaseSpec(root.FullName, gateStagingPath, moduleName);
            gateSpec.Segments = gateSpec.Segments
                .Prepend(new ConfigurationGateSegment
                {
                    Configuration = new GateConfiguration { Mode = gateMode }
                })
                .ToArray();
            if (gateMode == ConfigurationGateMode.Documentation)
            {
                gateSpec.Segments = gateSpec.Segments
                    .Append(new ConfigurationDocumentationSegment
                    {
                        Configuration = new DocumentationConfiguration
                        {
                            Path = "Docs",
                            PathReadme = Path.Combine("Docs", "Readme.md")
                        }
                    })
                    .Append(new ConfigurationBuildDocumentationSegment
                    {
                        Configuration = new BuildDocumentationConfiguration
                        {
                            Enable = true,
                            GenerateExternalHelp = false
                        }
                    })
                    .ToArray();
            }
            var gateRunner = CreateRunner(
                new FakeHostedOperations(new List<string>()),
                (request, configuration, configPath) => throw new InvalidOperationException("Package build should not run."));

            var exception = Assert.Throws<InvalidOperationException>(() => gateRunner.Run(gateSpec));

            Assert.Contains($"Gate mode {gateMode}", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("incomplete", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Single(Directory.GetFiles(GetCoordinatedReleaseCheckpointRoot(root.FullName), "*.json"));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(firstStagingPath)) Directory.Delete(firstStagingPath, recursive: true); } catch { }
            try { if (Directory.Exists(gateStagingPath)) Directory.Delete(gateStagingPath, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_ReplaysOnlyProjectNamesFromCoordinatedReleaseCheckpoint()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var firstStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        var secondStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string projectName = "A";
            const string packageId = "B";
            WriteMinimalModule(root.FullName, projectName, "2.0.10");
            WriteSynchronizedProjectBuildConfig(root.FullName, "project.build.json", projectName, publishNuGet: false);

            ProjectBuildHostExecutionResult CreateAliasedProjectResult(
                ProjectBuildHostRequest request,
                string? configPath)
            {
                var release = new DotNetRepositoryReleaseResult
                {
                    Success = true,
                    ResolvedVersion = "2.0.11"
                };
                release.ResolvedVersionsByProject[projectName] = "2.0.11";
                release.Projects.Add(new DotNetRepositoryProjectResult
                {
                    ProjectName = projectName,
                    PackageId = packageId,
                    IsPackable = true,
                    NewVersion = "2.0.11"
                });
                return new ProjectBuildHostExecutionResult
                {
                    Success = true,
                    ConfigPath = configPath ?? request.ConfigPath,
                    RootPath = root.FullName,
                    OutputPath = Path.Combine(root.FullName, "Artifacts", "NuGet"),
                    Result = new ProjectBuildResult
                    {
                        Success = true,
                        Release = release
                    }
                };
            }

            var firstHosted = new FakeHostedOperations(new List<string>())
            {
                ModulePublishAction = (_, _) => throw new InvalidOperationException("Simulated gallery outage.")
            };
            var firstRunner = CreateRunner(
                firstHosted,
                (request, configuration, configPath) => CreateAliasedProjectResult(request, configPath));
            Assert.Throws<InvalidOperationException>(() => firstRunner.Run(
                CreateGalleryReleaseSpec(root.FullName, firstStagingPath, projectName)));

            Dictionary<string, string>? replayedVersionMap = null;
            var secondRunner = CreateRunner(
                new FakeHostedOperations(new List<string>()),
                (request, configuration, configPath) =>
                {
                    replayedVersionMap = configuration?.ExpectedVersionMap is null
                        ? null
                        : new Dictionary<string, string>(configuration.ExpectedVersionMap, StringComparer.OrdinalIgnoreCase);
                    return CreateAliasedProjectResult(request, configPath);
                });

            secondRunner.Run(CreateGalleryReleaseSpec(root.FullName, secondStagingPath, projectName));

            Assert.NotNull(replayedVersionMap);
            Assert.Equal("2.0.11", replayedVersionMap![projectName]);
            Assert.DoesNotContain(packageId, replayedVersionMap.Keys, StringComparer.OrdinalIgnoreCase);
            AssertNoCoordinatedReleaseCheckpoint(root.FullName);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(firstStagingPath)) Directory.Delete(firstStagingPath, recursive: true); } catch { }
            try { if (Directory.Exists(secondStagingPath)) Directory.Delete(secondStagingPath, recursive: true); } catch { }
        }
    }

    private static ModulePipelineSpec CreateNuGetOnlyReleaseSpec(
        string rootPath,
        string stagingPath,
        string moduleName)
    {
        var spec = CreateGalleryReleaseSpec(rootPath, stagingPath, moduleName);
        spec.Segments = spec.Segments
            .Where(static segment => segment is not ConfigurationPublishSegment)
            .ToArray();
        var release = Assert.IsType<ConfigurationReleaseSegment>(spec.Segments[^1]);
        release.Configuration.PublishOrder = new[] { "NuGet" };
        return spec;
    }
}
