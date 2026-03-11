using PowerForge;

namespace PowerForge.Tests;

public sealed class ProjectBuildSupportServiceTests
{
    [Fact]
    public void LoadConfig_supports_comments_and_trailing_commas()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-projectbuild-" + Guid.NewGuid().ToString("N")));

        try
        {
            var configPath = Path.Combine(root.FullName, "project.build.json");
            File.WriteAllText(configPath, """
{
  // comment
  "PublishGitHub": true,
  "GitHubRepositoryName": "PSPublishModule",
}
""");

            var service = new ProjectBuildSupportService(new NullLogger());
            var config = service.LoadConfig(configPath);

            Assert.True(config.PublishGitHub);
            Assert.Equal("PSPublishModule", config.GitHubRepositoryName);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void ResolveSecret_prefers_file_then_environment_then_inline()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-projectbuild-secret-" + Guid.NewGuid().ToString("N")));
        var envName = "PF_TEST_SECRET_" + Guid.NewGuid().ToString("N");

        try
        {
            var secretPath = Path.Combine(root.FullName, "secret.txt");
            File.WriteAllText(secretPath, " from-file ");
            Environment.SetEnvironmentVariable(envName, "from-env");

            Assert.Equal("from-file", ProjectBuildSupportService.ResolveSecret("inline", secretPath, envName, root.FullName));
            Assert.Equal("from-env", ProjectBuildSupportService.ResolveSecret("inline", null, envName, root.FullName));
            Assert.Equal("inline", ProjectBuildSupportService.ResolveSecret("inline", null, null, root.FullName));
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, null);
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void ValidatePreflight_requires_github_token_and_repository_information()
    {
        var service = new ProjectBuildSupportService(new NullLogger());
        var config = new ProjectBuildConfiguration
        {
            GitHubUsername = "EvotecIT",
            GitHubRepositoryName = "PSPublishModule"
        };

        var missingToken = service.ValidatePreflight(
            publishNuget: false,
            publishGitHub: true,
            createReleaseZip: true,
            publishApiKey: null,
            gitHubToken: null,
            gitHubUsername: config.GitHubUsername,
            gitHubRepositoryName: config.GitHubRepositoryName);
        Assert.Contains("GitHubAccessToken", missingToken, StringComparison.Ordinal);

        config.GitHubRepositoryName = null;
        var missingRepo = service.ValidatePreflight(
            publishNuget: false,
            publishGitHub: true,
            createReleaseZip: true,
            publishApiKey: null,
            gitHubToken: "token",
            gitHubUsername: config.GitHubUsername,
            gitHubRepositoryName: config.GitHubRepositoryName);
        Assert.Contains("GitHubUsername/GitHubRepositoryName", missingRepo, StringComparison.Ordinal);
    }
}
