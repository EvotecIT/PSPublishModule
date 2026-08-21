using PowerForge;

namespace PowerForge.Tests;

public sealed class ProjectBuildGitHubPublisherTests
{
    [Fact]
    public void Publish_per_project_builds_project_tags_and_collects_results()
    {
        var requests = new List<GitHubReleasePublishRequest>();
        var progress = new RecordingProjectProgress();
        var publisher = new ProjectBuildGitHubPublisher(
            new NullLogger(),
            request =>
            {
                requests.Add(request);
                var assetPath = Assert.Single(request.AssetFilePaths!);
                request.Progress?.Report(new GitHubReleaseAssetProgress
                {
                    FilePath = assetPath,
                    FileName = Path.GetFileName(assetPath),
                    Position = 1,
                    TotalAssets = 1,
                    State = GitHubReleaseAssetProgressState.Uploaded
                });
                return new GitHubReleasePublishResult
                {
                    Succeeded = true,
                    ReleaseId = 42,
                    HtmlUrl = $"https://example.test/{request.TagName}"
                };
            },
            localNow: () => new DateTime(2026, 3, 11, 12, 0, 0, DateTimeKind.Local),
            utcNow: () => new DateTime(2026, 3, 11, 11, 0, 0, DateTimeKind.Utc));

        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-gh-" + Guid.NewGuid().ToString("N")));

        try
        {
            var projectAPath = Directory.CreateDirectory(Path.Combine(root.FullName, "ProjectA"));
            var projectBPath = Directory.CreateDirectory(Path.Combine(root.FullName, "ProjectB"));
            var assetA = Path.Combine(projectAPath.FullName, "Package.zip");
            var assetB = Path.Combine(projectBPath.FullName, "Package.zip");
            File.WriteAllText(assetA, "a");
            File.WriteAllText(assetB, "b");

            var summary = publisher.Publish(new ProjectBuildGitHubPublishRequest
            {
                Owner = "EvotecIT",
                Repository = "PSPublishModule",
                Token = "token",
                ReleaseMode = "PerProject",
                TagTemplate = "{Project}-v{Version}",
                ReleaseName = "{Project} {Version}",
                Progress = progress,
                Release = new DotNetRepositoryReleaseResult
                {
                    Success = true,
                    Projects =
                    {
                        new DotNetRepositoryProjectResult
                        {
                            ProjectName = "ProjectA",
                            IsPackable = true,
                            NewVersion = "1.2.3",
                            ReleaseZipPath = assetA
                        },
                        new DotNetRepositoryProjectResult
                        {
                            ProjectName = "ProjectB",
                            IsPackable = true,
                            NewVersion = "2.0.0",
                            ReleaseZipPath = assetB
                        }
                    }
                }
            });

            Assert.True(summary.Success);
            Assert.True(summary.PerProject);
            Assert.Equal(2, summary.Results.Count);
            Assert.All(summary.Results, result => Assert.Equal(42, result.ReleaseId));
            Assert.All(summary.Results, result => Assert.Equal("EvotecIT", result.Owner));
            Assert.All(summary.Results, result => Assert.Equal("PSPublishModule", result.Repository));
            Assert.Collection(
                requests.OrderBy(request => request.TagName, StringComparer.OrdinalIgnoreCase),
                first =>
                {
                    Assert.Equal("ProjectA-v1.2.3", first.TagName);
                    Assert.Equal("ProjectA 1.2.3", first.ReleaseName);
                    Assert.Single(first.AssetFilePaths!);
                    Assert.Equal(assetA, first.AssetFilePaths![0]);
                },
                second =>
                {
                    Assert.Equal("ProjectB-v2.0.0", second.TagName);
                    Assert.Equal("ProjectB 2.0.0", second.ReleaseName);
                    Assert.Single(second.AssetFilePaths!);
                    Assert.Equal(assetB, second.AssetFilePaths![0]);
                });
            Assert.Collection(
                progress.Items,
                first =>
                {
                    Assert.Equal(1, first.Position);
                    Assert.Equal(2, first.Total);
                },
                second =>
                {
                    Assert.Equal(2, second.Position);
                    Assert.Equal(2, second.Total);
                });
            Assert.Equal(2, progress.Items.Select(static item => item.Key).Distinct(StringComparer.Ordinal).Count());
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    private sealed class RecordingProjectProgress : IProjectBuildProgressReporterV2
    {
        internal List<ProjectBuildProgressItem> Items { get; } = new();

        public void PhaseStarted(ProjectBuildProgressPhase phase, int totalItems, string? detail = null) { }
        public void PhaseUpdated(ProjectBuildProgressPhase phase, int completedItems, int totalItems, string? detail = null) { }
        public void PhaseCompleted(ProjectBuildProgressPhase phase, string? detail = null) { }
        public void PhaseFailed(ProjectBuildProgressPhase phase, string? detail = null) { }
        public void ItemsPlanned(ProjectBuildProgressPhase phase, IReadOnlyList<ProjectBuildProgressItem> items)
            => Items.AddRange(items);
        public void ItemUpdated(ProjectBuildProgressItem item, ProjectBuildProgressItemState state, string? detail = null) { }
    }
}
