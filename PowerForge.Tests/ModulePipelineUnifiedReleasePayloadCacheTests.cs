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
            Assert.Empty(Directory.GetDirectories(
                GetCoordinatedReleaseCheckpointRoot(root.FullName),
                "*.payload"));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
            try { if (Directory.Exists(firstStagingPath)) Directory.Delete(firstStagingPath, recursive: true); } catch { }
            try { if (Directory.Exists(secondStagingPath)) Directory.Delete(secondStagingPath, recursive: true); } catch { }
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
