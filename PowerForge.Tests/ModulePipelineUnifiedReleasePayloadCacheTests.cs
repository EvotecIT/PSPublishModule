using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineUnifiedReleaseTests
{
    [Fact]
    public void Run_ResumesRemainingPublishWithExactCachedSignedPayload()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var firstStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        var secondStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            WriteSynchronizedProjectBuildConfig(root.FullName, "project.build.json", moduleName, publishNuGet: false);

            var runNumber = 1;
            var gitHubPublishCount = 0;
            string? resumedModulePayload = null;
            string? resumedPackagePayload = null;
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
                var packagePath = Assert.Single(result.Result.Release!.Projects[0].Packages);
                File.WriteAllText(packagePath, $"timestamped-package-signature-{runNumber}");
                return result;
            }
            GitHubReleasePublishResult PublishGitHub(GitHubReleasePublishRequest request)
            {
                gitHubPublishCount++;
                if (gitHubPublishCount == 1)
                    throw new InvalidOperationException("Simulated GitHub outage.");

                var moduleArchive = Assert.Single(request.AssetFilePaths, path =>
                    string.Equals(Path.GetFileName(path), $"{moduleName}.zip", StringComparison.OrdinalIgnoreCase));
                using var archive = ZipFile.OpenRead(moduleArchive);
                var moduleEntry = Assert.Single(archive.Entries, entry =>
                    string.Equals(entry.Name, $"{moduleName}.psm1", StringComparison.OrdinalIgnoreCase));
                using var reader = new StreamReader(moduleEntry.Open());
                resumedModulePayload = reader.ReadToEnd();
                var packagePath = Assert.Single(request.AssetFilePaths, path =>
                    string.Equals(Path.GetExtension(path), ".nupkg", StringComparison.OrdinalIgnoreCase));
                resumedPackagePayload = File.ReadAllText(packagePath);
                return new GitHubReleasePublishResult
                {
                    Succeeded = true,
                    ReleaseCreationSucceeded = true,
                    AllAssetUploadsSucceeded = true,
                    HtmlUrl = $"https://github.com/EvotecIT/{moduleName}/releases/tag/{request.TagName}"
                };
            }

            FakeHostedOperations CreateHostedOperations()
                => new(new List<string>())
                {
                    ModuleAction = (action, context) =>
                    {
                        if (context.Stage == ModulePipelineActionStage.BeforeArtefacts)
                        {
                            File.AppendAllText(
                                Path.Combine(context.StagingPath!, $"{moduleName}.psm1"),
                                $"{Environment.NewLine}# timestamped-signature-{runNumber}");
                        }
                        return CreateActionResult(action, context, succeeded: true);
                    }
                };

            var firstRunner = CreateRunner(CreateHostedOperations(), ExecutePackageBuild, PublishGitHub);
            var firstException = Assert.Throws<InvalidOperationException>(() => firstRunner.Run(
                CreateGalleryAndGitHubReleaseSpec(root.FullName, firstStagingPath, moduleName)));
            Assert.Contains("GitHub outage", firstException.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, gitHubPublishCount);
            var checkpointRoot = GetCoordinatedReleaseCheckpointRoot(root.FullName);
            var payloadCache = Assert.Single(Directory.GetDirectories(checkpointRoot, "*.payload"));
            var interruptedCache = payloadCache + ".tmp-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(interruptedCache);
            File.WriteAllText(Path.Combine(interruptedCache, "partial.bin"), "partial-signed-payload");

            runNumber = 2;
            var secondRunner = CreateRunner(CreateHostedOperations(), ExecutePackageBuild, PublishGitHub);
            var result = secondRunner.Run(
                CreateGalleryAndGitHubReleaseSpec(root.FullName, secondStagingPath, moduleName));

            Assert.Equal("2.0.11", result.Plan.ResolvedVersion);
            Assert.Equal(2, gitHubPublishCount);
            Assert.Contains("timestamped-signature-1", resumedModulePayload, StringComparison.Ordinal);
            Assert.DoesNotContain("timestamped-signature-2", resumedModulePayload, StringComparison.Ordinal);
            Assert.Equal("timestamped-package-signature-1", resumedPackagePayload);
            AssertNoCoordinatedReleaseCheckpoint(root.FullName);
            Assert.Empty(Directory.GetDirectories(checkpointRoot, "*.payload"));
            Assert.Empty(Directory.GetDirectories(checkpointRoot, "*.payload.tmp-*"));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(firstStagingPath)) Directory.Delete(firstStagingPath, recursive: true); } catch { }
            try { if (Directory.Exists(secondStagingPath)) Directory.Delete(secondStagingPath, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_RebindsCachedPackagesAndReleaseZipsByStableProjectIdentity()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var firstStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        var secondStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "Primary";
            const string companionName = "Companion";
            const string version = "2.0.11";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            WriteSynchronizedProjectBuildConfig(root.FullName, "project.build.json", moduleName, publishNuGet: false);

            var runNumber = 1;
            var gitHubPublishCount = 0;
            ProjectBuildHostExecutionResult ExecutePackageBuild(
                ProjectBuildHostRequest request,
                ProjectBuildConfiguration? configuration,
                string? configPath)
            {
                var outputPath = Path.Combine(root.FullName, "Artifacts", "Projects");
                Directory.CreateDirectory(outputPath);
                var release = new DotNetRepositoryReleaseResult
                {
                    Success = true,
                    ResolvedVersion = version
                };
                release.ResolvedVersionsByProject[moduleName] = version;
                release.ResolvedVersionsByProject[companionName] = version;

                DotNetRepositoryProjectResult CreateProject(string projectName)
                {
                    var packagePath = Path.Combine(outputPath, $"{projectName}.{version}.nupkg");
                    var releaseZipPath = Path.Combine(outputPath, $"{projectName}-{version}.zip");
                    File.WriteAllText(packagePath, $"{projectName}-package-run-{runNumber}");
                    File.WriteAllText(releaseZipPath, $"{projectName}-release-run-{runNumber}");
                    var project = new DotNetRepositoryProjectResult
                    {
                        ProjectName = projectName,
                        PackageId = projectName,
                        IsPackable = true,
                        NewVersion = version,
                        ReleaseZipPath = releaseZipPath
                    };
                    project.Packages.Add(packagePath);
                    return project;
                }

                var primary = CreateProject(moduleName);
                var companion = CreateProject(companionName);
                if (runNumber == 1)
                {
                    release.Projects.Add(primary);
                    release.Projects.Add(companion);
                }
                else
                {
                    release.Projects.Add(companion);
                    release.Projects.Add(primary);
                }

                return new ProjectBuildHostExecutionResult
                {
                    Success = true,
                    ConfigPath = configPath ?? request.ConfigPath,
                    RootPath = root.FullName,
                    OutputPath = outputPath,
                    ReleaseZipOutputPath = outputPath,
                    Result = new ProjectBuildResult
                    {
                        Success = true,
                        Release = release
                    }
                };
            }

            GitHubReleasePublishResult PublishGitHub(GitHubReleasePublishRequest request)
            {
                gitHubPublishCount++;
                if (gitHubPublishCount == 1)
                    throw new InvalidOperationException("Simulated GitHub outage.");
                return new GitHubReleasePublishResult
                {
                    Succeeded = true,
                    ReleaseCreationSucceeded = true,
                    AllAssetUploadsSucceeded = true,
                    HtmlUrl = $"https://github.com/EvotecIT/{moduleName}/releases/tag/{request.TagName}"
                };
            }

            FakeHostedOperations CreateHostedOperations()
                => new(new List<string>())
                {
                    ModuleAction = (action, context) => CreateActionResult(action, context, succeeded: true)
                };

            var firstRunner = CreateRunner(CreateHostedOperations(), ExecutePackageBuild, PublishGitHub);
            Assert.Throws<InvalidOperationException>(() => firstRunner.Run(
                CreateGalleryAndGitHubReleaseSpec(root.FullName, firstStagingPath, moduleName)));

            runNumber = 2;
            var secondRunner = CreateRunner(CreateHostedOperations(), ExecutePackageBuild, PublishGitHub);
            var result = secondRunner.Run(
                CreateGalleryAndGitHubReleaseSpec(root.FullName, secondStagingPath, moduleName));

            var projects = Assert.Single(result.ProjectBuildResults).Result.Release!.Projects;
            Assert.Equal(new[] { companionName, moduleName }, projects.Select(project => project.ProjectName));
            foreach (var project in projects)
            {
                Assert.Equal($"{project.ProjectName}-package-run-1", File.ReadAllText(Assert.Single(project.Packages)));
                Assert.Equal($"{project.ProjectName}-release-run-1", File.ReadAllText(project.ReleaseZipPath!));
            }
            Assert.Equal(2, gitHubPublishCount);
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
    public void Run_RejectsDuplicateCachedProjectIdentity()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var stagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            WriteSynchronizedProjectBuildConfig(root.FullName, "project.build.json", moduleName, publishNuGet: false);

            var runner = CreateRunner(
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
                    result.Result.Release!.Projects.Add(new DotNetRepositoryProjectResult
                    {
                        ProjectName = moduleName,
                        PackageId = moduleName,
                        IsPackable = true,
                        NewVersion = "2.0.11"
                    });
                    return result;
                });

            var exception = Assert.Throws<InvalidOperationException>(() => runner.Run(
                CreateGitHubPostPublishSpec(root.FullName, stagingPath, moduleName)));

            Assert.Contains("duplicate project identity", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, recursive: true); } catch { }
        }
    }

    private static ModulePipelineSpec CreateGalleryAndGitHubReleaseSpec(
        string rootPath,
        string stagingPath,
        string moduleName)
    {
        var spec = CreateGitHubPostPublishSpec(rootPath, stagingPath, moduleName);
        var segments = spec.Segments.ToList();
        segments.Insert(
            1,
            new ConfigurationActionSegment
            {
                Configuration = new ModulePipelineActionConfiguration
                {
                    Enabled = true,
                    Name = "TimestampSignature",
                    At = ModulePipelineActionStage.BeforeArtefacts,
                    InlineScript = "Write-Output ignored"
                }
            });
        segments.Insert(
            3,
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
        release.Configuration.PublishOrder = new[] { "PowerShellGallery", "GitHub" };
        spec.Segments = segments.ToArray();
        return spec;
    }
}
