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
