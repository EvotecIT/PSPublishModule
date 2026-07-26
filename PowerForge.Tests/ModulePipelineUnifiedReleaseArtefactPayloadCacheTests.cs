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
    public void Run_RebindsCachedArtefactsByStableIdentityWhenSegmentsReorder()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var firstStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        var secondStagingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "2.0.10");
            WriteSynchronizedProjectBuildConfig(root.FullName, "project.build.json", moduleName, publishNuGet: false);
            var firstMarkerPath = Path.Combine(root.FullName, "marker-first.txt");
            var secondMarkerPath = Path.Combine(root.FullName, "marker-second.txt");
            File.WriteAllText(firstMarkerPath, "first-artefact-payload");
            File.WriteAllText(secondMarkerPath, "second-artefact-payload");

            var gitHubPublishCount = 0;
            string? publishedMarker = null;
            GitHubReleasePublishResult PublishGitHub(GitHubReleasePublishRequest request)
            {
                gitHubPublishCount++;
                if (gitHubPublishCount == 1)
                    throw new InvalidOperationException("Simulated GitHub outage.");

                var artefactPath = Assert.Single(request.AssetFilePaths, path =>
                    string.Equals(Path.GetFileName(path), "First.zip", StringComparison.Ordinal));
                publishedMarker = ReadZipEntry(artefactPath, "marker-first.txt");
                return new GitHubReleasePublishResult
                {
                    Succeeded = true,
                    ReleaseCreationSucceeded = true,
                    AllAssetUploadsSucceeded = true,
                    HtmlUrl = $"https://github.com/EvotecIT/{moduleName}/releases/tag/{request.TagName}"
                };
            }

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
                    includePackage: true);

            FakeHostedOperations CreateHostedOperations()
                => new(new List<string>())
                {
                    ModuleAction = (action, context) => CreateActionResult(action, context, succeeded: true)
                };

            var firstRunner = CreateRunner(CreateHostedOperations(), ExecutePackageBuild, PublishGitHub);
            Assert.Throws<InvalidOperationException>(() => firstRunner.Run(
                CreateReorderedArtefactReleaseSpec(
                    root.FullName,
                    firstStagingPath,
                    moduleName,
                    firstMarkerPath,
                    secondMarkerPath,
                    reverse: false)));

            var secondRunner = CreateRunner(CreateHostedOperations(), ExecutePackageBuild, PublishGitHub);
            var result = secondRunner.Run(
                CreateReorderedArtefactReleaseSpec(
                    root.FullName,
                    secondStagingPath,
                    moduleName,
                    firstMarkerPath,
                    secondMarkerPath,
                    reverse: true));

            Assert.Equal(new[] { "Second", "First" }, result.ArtefactResults.Select(artefact => artefact.Id));
            foreach (var artefact in result.ArtefactResults)
            {
                var expectedMarker = string.Equals(artefact.Id, "First", StringComparison.Ordinal)
                    ? "marker-first.txt"
                    : "marker-second.txt";
                var expectedPayload = string.Equals(artefact.Id, "First", StringComparison.Ordinal)
                    ? "first-artefact-payload"
                    : "second-artefact-payload";
                Assert.Equal(expectedPayload, ReadZipEntry(artefact.OutputPath, expectedMarker));
            }
            Assert.Equal("first-artefact-payload", publishedMarker);
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

    private static ModulePipelineSpec CreateReorderedArtefactReleaseSpec(
        string rootPath,
        string stagingPath,
        string moduleName,
        string firstMarkerPath,
        string secondMarkerPath,
        bool reverse)
    {
        var spec = CreateGalleryAndGitHubReleaseSpec(rootPath, stagingPath, moduleName);
        var segments = spec.Segments.ToList();
        var artefactIndex = segments.FindIndex(static segment => segment is ConfigurationArtefactSegment);
        Assert.True(artefactIndex >= 0);
        segments.RemoveAt(artefactIndex);

        ConfigurationArtefactSegment CreateArtefact(string id, string markerPath, string markerName)
            => new()
            {
                ArtefactType = ArtefactType.Packed,
                Configuration = new ArtefactConfiguration
                {
                    Enabled = true,
                    Path = Path.Combine(rootPath, "Artifacts", "Module"),
                    ArtefactName = id + ".zip",
                    ID = id,
                    DestinationFilesRelative = true,
                    FilesOutput = new[]
                    {
                        new ArtefactCopyMapping
                        {
                            Source = markerPath,
                            Destination = markerName
                        }
                    }
                }
            };

        var first = CreateArtefact("First", firstMarkerPath, "marker-first.txt");
        var second = CreateArtefact("Second", secondMarkerPath, "marker-second.txt");
        segments.InsertRange(
            artefactIndex,
            reverse
                ? new IConfigurationSegment[] { second, first }
                : new IConfigurationSegment[] { first, second });
        var gitHubPublish = segments
            .OfType<ConfigurationPublishSegment>()
            .Single(segment => segment.Configuration.Destination == PublishDestination.GitHub);
        gitHubPublish.Configuration.ID = "First";
        spec.Segments = segments.ToArray();
        return spec;
    }

    private static string ReadZipEntry(string zipPath, string entryName)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = Assert.Single(archive.Entries, candidate =>
            string.Equals(candidate.Name, entryName, StringComparison.Ordinal));
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }
}
