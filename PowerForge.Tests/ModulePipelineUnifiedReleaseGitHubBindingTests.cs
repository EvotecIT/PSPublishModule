using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineUnifiedReleaseTests
{
    [Fact]
    public void Run_RejectsCompletedPackageGitHubPublishWithoutExactIdentity()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var stagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            const string coordinatedTag = "TestModule-v2.0.11";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            WriteSynchronizedProjectBuildConfig(
                root.FullName,
                "project.build.json",
                moduleName,
                publishNuGet: false,
                publishGitHub: true);

            var runner = CreateRunner(
                new FakeHostedOperations(new List<string>()),
                (request, configuration, configPath) =>
                {
                    var result = CreateProjectBuildResult(
                        root.FullName,
                        moduleName,
                        "2.0.11",
                        Path.Combine(root.FullName, "PackageOutput", "NuGet"),
                        request,
                        configPath);
                    if (request.PublishGitHub == true)
                    {
                        result.Result.GitHub.Add(new ProjectBuildGitHubResult
                        {
                            ProjectName = moduleName,
                            Owner = "EvotecIT",
                            Repository = moduleName,
                            Success = true,
                            TagName = coordinatedTag,
                            ReleaseId = 0
                        });
                    }

                    return result;
                },
                _ => throw new Xunit.Sdk.XunitException("Unified GitHub publishing must not run without an exact package release identity."));

            var exception = Assert.Throws<InvalidOperationException>(() => runner.Run(
                CreateCoordinatedGitHubSpec(root.FullName, stagingPath, moduleName, coordinatedTag)));

            Assert.Contains("complete release identities", exception.Message, StringComparison.OrdinalIgnoreCase);
            var checkpointPath = Assert.Single(Directory.GetFiles(GetCoordinatedReleaseCheckpointRoot(root.FullName), "*.json"));
            using var checkpoint = JsonDocument.Parse(File.ReadAllText(checkpointPath));
            Assert.Empty(checkpoint.RootElement.GetProperty("GitHubReleases").EnumerateArray());
            Assert.DoesNotContain(
                checkpoint.RootElement.GetProperty("AttemptedOperations").EnumerateArray()
                    .Select(static item => item.GetString()),
                operation => checkpoint.RootElement.GetProperty("CompletedOperations").EnumerateArray()
                    .Select(static item => item.GetString())
                    .Contains(operation, StringComparer.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_ReusesPackageCreatedGitHubReleaseByExactIdAfterResume()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var firstStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        var secondStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            const string synchronizedVersion = "2.0.11";
            const string coordinatedTag = "TestModule-v2.0.11";
            const long packageReleaseId = 4242;
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            WriteSynchronizedProjectBuildConfig(
                root.FullName,
                "project.build.json",
                moduleName,
                publishNuGet: false,
                publishGitHub: true);

            var packageGitHubPublishCount = 0;
            var unifiedGitHubRequests = new List<GitHubReleasePublishRequest>();

            ProjectBuildHostExecutionResult ExecutePackageBuild(
                ProjectBuildHostRequest request,
                ProjectBuildConfiguration? configuration,
                string? configPath)
            {
                var result = CreateProjectBuildResult(
                    root.FullName,
                    moduleName,
                    synchronizedVersion,
                    Path.Combine(root.FullName, "PackageOutput", "NuGet"),
                    request,
                    configPath);
                if (request.PublishGitHub == true)
                {
                    packageGitHubPublishCount++;
                    result.Result.GitHub.Add(new ProjectBuildGitHubResult
                    {
                        ProjectName = moduleName,
                        Owner = "EvotecIT",
                        Repository = moduleName,
                        Success = true,
                        TagName = coordinatedTag,
                        ReleaseId = packageReleaseId,
                        ReleaseUrl = $"https://github.com/EvotecIT/{moduleName}/releases/tag/{coordinatedTag}"
                    });
                }

                return result;
            }

            GitHubReleasePublishResult PublishUnifiedGitHub(GitHubReleasePublishRequest request)
            {
                unifiedGitHubRequests.Add(request);
                Assert.True(request.ReuseExistingReleaseOnConflict);
                Assert.True(request.RequireExpectedExistingRelease);
                Assert.Equal(packageReleaseId, request.ExpectedExistingReleaseId);
                Assert.False(request.ReplaceExistingAssets);

                if (unifiedGitHubRequests.Count == 1)
                    throw new InvalidOperationException("Simulated unified GitHub interruption.");

                return new GitHubReleasePublishResult
                {
                    Succeeded = true,
                    ReleaseCreationSucceeded = true,
                    ReleaseId = packageReleaseId,
                    AllAssetUploadsSucceeded = true,
                    HtmlUrl = $"https://github.com/EvotecIT/{moduleName}/releases/tag/{coordinatedTag}"
                };
            }

            var firstRunner = CreateRunner(
                new FakeHostedOperations(new List<string>()),
                ExecutePackageBuild,
                PublishUnifiedGitHub);
            var firstException = Assert.Throws<InvalidOperationException>(() => firstRunner.Run(
                CreateCoordinatedGitHubSpec(root.FullName, firstStagingPath, moduleName, coordinatedTag)));

            Assert.Contains("interruption", firstException.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, packageGitHubPublishCount);
            var checkpointPath = Assert.Single(Directory.GetFiles(GetCoordinatedReleaseCheckpointRoot(root.FullName), "*.json"));
            using (var checkpoint = JsonDocument.Parse(File.ReadAllText(checkpointPath)))
            {
                var release = Assert.Single(checkpoint.RootElement.GetProperty("GitHubReleases").EnumerateArray());
                Assert.Equal(packageReleaseId, release.GetProperty("ReleaseId").GetInt64());
                Assert.Equal("EvotecIT", release.GetProperty("Owner").GetString());
                Assert.Equal(moduleName, release.GetProperty("Repository").GetString());
                Assert.Equal(coordinatedTag, release.GetProperty("TagName").GetString());
                Assert.Contains(
                    release.GetProperty("OperationKey").GetString(),
                    checkpoint.RootElement.GetProperty("CompletedOperations").EnumerateArray()
                        .Select(static item => item.GetString()),
                    StringComparer.OrdinalIgnoreCase);
            }

            var secondRunner = CreateRunner(
                new FakeHostedOperations(new List<string>()),
                ExecutePackageBuild,
                PublishUnifiedGitHub);
            var result = secondRunner.Run(
                CreateCoordinatedGitHubSpec(root.FullName, secondStagingPath, moduleName, coordinatedTag));

            Assert.Equal(synchronizedVersion, result.Plan.ResolvedVersion);
            Assert.Equal(1, packageGitHubPublishCount);
            Assert.Equal(2, unifiedGitHubRequests.Count);
            Assert.All(unifiedGitHubRequests, request => Assert.Equal(packageReleaseId, request.ExpectedExistingReleaseId));
            var restoredGitHub = Assert.Single(Assert.Single(result.ProjectBuildResults).Result.GitHub);
            Assert.Equal(moduleName, restoredGitHub.ProjectName);
            Assert.Equal("EvotecIT", restoredGitHub.Owner);
            Assert.Equal(moduleName, restoredGitHub.Repository);
            Assert.Equal(coordinatedTag, restoredGitHub.TagName);
            Assert.Equal(packageReleaseId, restoredGitHub.ReleaseId);
            AssertNoCoordinatedReleaseCheckpoint(root.FullName);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(firstStagingPath)) Directory.Delete(firstStagingPath, recursive: true); } catch { }
            try { if (Directory.Exists(secondStagingPath)) Directory.Delete(secondStagingPath, recursive: true); } catch { }
        }
    }

    private static ModulePipelineSpec CreateCoordinatedGitHubSpec(
        string rootPath,
        string stagingPath,
        string moduleName,
        string coordinatedTag)
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
                new ConfigurationArtefactSegment
                {
                    ArtefactType = ArtefactType.Packed,
                    Configuration = new ArtefactConfiguration
                    {
                        Enabled = true,
                        ID = "module",
                        Path = Path.Combine(rootPath, "Artifacts", "Module")
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
                        ApiKey = "test-token",
                        OverwriteTagName = coordinatedTag
                    }
                },
                new ConfigurationReleaseSegment
                {
                    Configuration = new ReleaseConfiguration
                    {
                        VersionSource = ReleaseVersionSource.ProjectBuild,
                        PrimaryProject = moduleName,
                        SynchronizeModuleVersion = true,
                        ResumeIncompleteRelease = true,
                        PublishOrder = new[] { "GitHub" }
                    }
                }
            }
        };
}
