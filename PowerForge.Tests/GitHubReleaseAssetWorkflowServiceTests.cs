using PowerForge;

namespace PowerForge.Tests;

public sealed class GitHubReleaseAssetWorkflowServiceTests
{
    [Fact]
    public void Execute_groups_shared_tag_into_single_publish_request()
    {
        var publishRequests = new List<GitHubReleasePublishRequest>();
        var service = new GitHubReleaseAssetWorkflowService(
            new NullLogger(),
            request =>
            {
                publishRequests.Add(request);
                return new GitHubReleasePublishResult
                {
                    Succeeded = true,
                    HtmlUrl = $"https://example.test/{request.TagName}"
                };
            });

        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-gh-asset-" + Guid.NewGuid().ToString("N")));
        try
        {
            var projectA = CreateProject(root.FullName, "ProjectA", "1.2.3");
            var projectB = CreateProject(root.FullName, "ProjectB", "1.2.3");

            var results = service.Execute(
                new GitHubReleaseAssetWorkflowRequest
                {
                    ProjectPaths = new[] { projectA, projectB },
                    Owner = "EvotecIT",
                    Repository = "PSPublishModule",
                    Token = "token"
                },
                publish: true);

            Assert.Equal(2, results.Count);
            Assert.All(results, result => Assert.True(result.Success));
            Assert.Single(publishRequests);
            Assert.Equal("v1.2.3", publishRequests[0].TagName);
            Assert.Equal(2, publishRequests[0].AssetFilePaths.Count);
        }
        finally
        {
            TryDelete(root.FullName);
        }
    }

    [Fact]
    public void Execute_skips_publisher_in_plan_mode_and_returns_tag_url()
    {
        var publishCalls = 0;
        var service = new GitHubReleaseAssetWorkflowService(
            new NullLogger(),
            request =>
            {
                publishCalls++;
                return new GitHubReleasePublishResult { Succeeded = true, HtmlUrl = "https://example.test/release" };
            });

        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-gh-asset-plan-" + Guid.NewGuid().ToString("N")));
        try
        {
            var project = CreateProject(root.FullName, "ProjectA", "2.0.0");

            var results = service.Execute(
                new GitHubReleaseAssetWorkflowRequest
                {
                    ProjectPaths = new[] { project },
                    Owner = "EvotecIT",
                    Repository = "PSPublishModule",
                    Token = "token",
                    TagTemplate = "{Project}-v{Version}"
                },
                publish: false);

            var result = Assert.Single(results);
            Assert.True(result.Success);
            Assert.Equal("ProjectA-v2.0.0", result.TagName);
            Assert.Equal("https://github.com/EvotecIT/PSPublishModule/releases/tag/ProjectA-v2.0.0", result.ReleaseUrl);
            Assert.Equal(0, publishCalls);
        }
        finally
        {
            TryDelete(root.FullName);
        }
    }

    [Fact]
    public void Execute_returns_error_when_multiple_projects_use_zip_override()
    {
        var service = new GitHubReleaseAssetWorkflowService(new NullLogger());

        var results = service.Execute(
            new GitHubReleaseAssetWorkflowRequest
            {
                ProjectPaths = new[] { "A", "B" },
                Owner = "EvotecIT",
                Repository = "PSPublishModule",
                Token = "token",
                ZipPath = "release.zip"
            },
            publish: true);

        var result = Assert.Single(results);
        Assert.False(result.Success);
        Assert.Contains("ZipPath override", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_returns_error_when_default_zip_is_missing()
    {
        var service = new GitHubReleaseAssetWorkflowService(new NullLogger());
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-gh-asset-missingzip-" + Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "ProjectA"));
            var csprojPath = Path.Combine(project.FullName, "ProjectA.csproj");
            File.WriteAllText(csprojPath, "<Project><PropertyGroup><VersionPrefix>3.0.0</VersionPrefix></PropertyGroup></Project>");

            var results = service.Execute(
                new GitHubReleaseAssetWorkflowRequest
                {
                    ProjectPaths = new[] { project.FullName },
                    Owner = "EvotecIT",
                    Repository = "PSPublishModule",
                    Token = "token"
                },
                publish: true);

            var result = Assert.Single(results);
            Assert.False(result.Success);
            Assert.Contains("Zip file", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("ProjectA.3.0.0.zip", result.ZipPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root.FullName);
        }
    }

    private static string CreateProject(string root, string projectName, string version)
    {
        var projectDir = Directory.CreateDirectory(Path.Combine(root, projectName));
        var csprojPath = Path.Combine(projectDir.FullName, projectName + ".csproj");
        File.WriteAllText(csprojPath, $"<Project><PropertyGroup><VersionPrefix>{version}</VersionPrefix></PropertyGroup></Project>");

        var zipDir = Directory.CreateDirectory(Path.Combine(projectDir.FullName, "bin", "Release"));
        var zipPath = Path.Combine(zipDir.FullName, $"{projectName}.{version}.zip");
        File.WriteAllText(zipPath, "zip");
        return projectDir.FullName;
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }
}
