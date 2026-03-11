using PowerForge;

namespace PowerForge.Tests;

public sealed class ProjectBuildGitHubPublisherTests
{
    [Fact]
    public void Publish_per_project_builds_project_tags_and_collects_results()
    {
        var requests = new List<GitHubReleasePublishRequest>();
        var publisher = new ProjectBuildGitHubPublisher(
            new NullLogger(),
            request =>
            {
                requests.Add(request);
                return new GitHubReleasePublishResult
                {
                    Succeeded = true,
                    HtmlUrl = $"https://example.test/{request.TagName}"
                };
            },
            localNow: () => new DateTime(2026, 3, 11, 12, 0, 0, DateTimeKind.Local),
            utcNow: () => new DateTime(2026, 3, 11, 11, 0, 0, DateTimeKind.Utc));

        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-gh-" + Guid.NewGuid().ToString("N")));

        try
        {
            var assetA = Path.Combine(root.FullName, "ProjectA.1.2.3.zip");
            var assetB = Path.Combine(root.FullName, "ProjectB.2.0.0.zip");
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
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
