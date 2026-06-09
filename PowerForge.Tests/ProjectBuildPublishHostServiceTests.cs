using PowerForge;

namespace PowerForge.Tests;

public sealed class ProjectBuildPublishHostServiceTests
{
    [Fact]
    public void LoadConfiguration_ResolvesSecretsAndSupportsRelaxedJson()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"))).FullName;
        var buildDirectory = Directory.CreateDirectory(Path.Combine(root, "Build")).FullName;
        var apiKeyPath = Path.Combine(buildDirectory, "nuget.key");
        File.WriteAllText(apiKeyPath, "file-secret");
        var configPath = Path.Combine(buildDirectory, "project.build.json");
        File.WriteAllText(
            configPath,
            """
            {
              // comments and trailing commas should be accepted by the shared host reader
              "PublishNuget": true,
              "PublishApiKeyFilePath": "nuget.key",
              "PublishGitHub": true,
              "GitHubAccessTokenEnvName": "PFGH_TOKEN",
              "GitHubUsername": "EvotecIT",
              "GitHubRepositoryName": "PSPublishModule",
              "GitHubReleaseMode": "PerProject",
            }
            """);

        try
        {
            using var _ = new EnvironmentScope()
                .Set("PFGH_TOKEN", "env-secret");

            var configuration = new ProjectBuildPublishHostService().LoadConfiguration(configPath);

            Assert.True(configuration.PublishNuget);
            Assert.True(configuration.PublishGitHub);
            Assert.Equal("https://api.nuget.org/v3/index.json", configuration.PublishSource);
            Assert.Equal("file-secret", configuration.PublishApiKey);
            Assert.Equal("env-secret", configuration.GitHubToken);
            Assert.Equal("EvotecIT", configuration.GitHubUsername);
            Assert.Equal("PSPublishModule", configuration.GitHubRepositoryName);
            Assert.Equal("PerProject", configuration.GitHubReleaseMode);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void PublishGitHub_MapsHostConfigurationIntoSharedRequest()
    {
        ProjectBuildGitHubPublishRequest? captured = null;
        var service = new ProjectBuildPublishHostService(
            new NullLogger(),
            request => {
                captured = request;
                return new ProjectBuildGitHubPublishSummary {
                    Success = true,
                    SummaryTag = "v1.2.3"
                };
            });

        var configuration = new ProjectBuildPublishHostConfiguration {
            GitHubUsername = "EvotecIT",
            GitHubRepositoryName = "PSPublishModule",
            GitHubToken = "token",
            GitHubReleaseMode = "PerProject",
            GitHubIncludeProjectNameInTag = false,
            GitHubIsPreRelease = true,
            GitHubGenerateReleaseNotes = true,
            GitHubReleaseName = "Release {Version}",
            GitHubTagName = "v1.2.3",
            GitHubTagTemplate = "{Project}-v{Version}",
            GitHubPrimaryProject = "PSPublishModule",
            GitHubTagConflictPolicy = "AppendUtcTimestamp"
        };

        var release = new DotNetRepositoryReleaseResult {
            Success = true,
            Projects = {
                new DotNetRepositoryProjectResult {
                    ProjectName = "PSPublishModule",
                    IsPackable = true,
                    NewVersion = "1.2.3",
                    ReleaseZipPath = @"C:\Temp\PSPublishModule.1.2.3.zip"
                }
            }
        };

        var summary = service.PublishGitHub(configuration, release);

        Assert.True(summary.Success);
        Assert.NotNull(captured);
        Assert.Equal("EvotecIT", captured!.Owner);
        Assert.Equal("PSPublishModule", captured.Repository);
        Assert.Equal("token", captured.Token);
        Assert.Same(release, captured.Release);
        Assert.Equal("PerProject", captured.ReleaseMode);
        Assert.False(captured.IncludeProjectNameInTag);
        Assert.True(captured.IsPreRelease);
        Assert.True(captured.GenerateReleaseNotes);
        Assert.Equal("Release {Version}", captured.ReleaseName);
        Assert.Equal("v1.2.3", captured.TagName);
        Assert.Equal("{Project}-v{Version}", captured.TagTemplate);
        Assert.Equal("PSPublishModule", captured.PrimaryProject);
        Assert.Equal("AppendUtcTimestamp", captured.TagConflictPolicy);
    }

    private sealed class EnvironmentScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originalValues = new(StringComparer.OrdinalIgnoreCase);

        public EnvironmentScope Set(string name, string? value)
        {
            if (!_originalValues.ContainsKey(name))
                _originalValues[name] = Environment.GetEnvironmentVariable(name);

            Environment.SetEnvironmentVariable(name, value);
            return this;
        }

        public void Dispose()
        {
            foreach (var entry in _originalValues)
                Environment.SetEnvironmentVariable(entry.Key, entry.Value);
        }
    }
}
