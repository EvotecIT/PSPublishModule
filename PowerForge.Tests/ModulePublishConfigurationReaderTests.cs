using PowerForge;

namespace PowerForge.Tests;

public sealed class ModulePublishConfigurationReaderTests
{
    [Fact]
    public void ReadFromJson_ResolvesRepositoryAndGitHubPublishSegments()
    {
        const string json = """
        {
          "Build": {
            "Name": "PSPublishModule",
            "SourcePath": "Module/PSPublishModule",
            "Version": "2.0.0"
          },
          "Segments": [
            {
              "Type": "GalleryNuget",
              "Configuration": {
                "Destination": "PowerShellGallery",
                "Tool": "PSResourceGet",
                "ApiKey": "gallery-key",
                "Enabled": true,
                "RepositoryName": "PSGallery",
                "Repository": {
                  "Name": "PSGallery",
                  "Uri": "https://www.powershellgallery.com/api/v2",
                  "EnsureRegistered": true,
                  "Trusted": true,
                  "Credential": {
                    "UserName": "user",
                    "Secret": "secret"
                  }
                }
              }
            },
            {
              "Type": "GitHubNuget",
              "Configuration": {
                "Destination": "GitHub",
                "ApiKey": "token",
                "Enabled": true,
                "UserName": "EvotecIT",
                "RepositoryName": "PSPublishModule",
                "GenerateReleaseNotes": true,
                "OverwriteTagName": "{TagModuleVersionWithPreRelease}"
              }
            }
          ]
        }
        """;

        var configs = new ModulePublishConfigurationReader().ReadFromJson(json);

        Assert.Equal(2, configs.Count);

        var repositoryPublish = configs[0];
        Assert.Equal(PublishDestination.PowerShellGallery, repositoryPublish.Destination);
        Assert.Equal(PublishTool.PSResourceGet, repositoryPublish.Tool);
        Assert.Equal("gallery-key", repositoryPublish.ApiKey);
        Assert.Equal("PSGallery", repositoryPublish.RepositoryName);
        Assert.NotNull(repositoryPublish.Repository);
        Assert.Equal("PSGallery", repositoryPublish.Repository!.Name);
        Assert.Equal("https://www.powershellgallery.com/api/v2", repositoryPublish.Repository.Uri);
        Assert.NotNull(repositoryPublish.Repository.Credential);
        Assert.Equal("user", repositoryPublish.Repository.Credential!.UserName);
        Assert.Equal("secret", repositoryPublish.Repository.Credential.Secret);

        var gitHubPublish = configs[1];
        Assert.Equal(PublishDestination.GitHub, gitHubPublish.Destination);
        Assert.Equal("EvotecIT", gitHubPublish.UserName);
        Assert.Equal("PSPublishModule", gitHubPublish.RepositoryName);
        Assert.True(gitHubPublish.GenerateReleaseNotes);
        Assert.Equal("{TagModuleVersionWithPreRelease}", gitHubPublish.OverwriteTagName);
    }

    [Fact]
    public void BuildTag_UsesSharedModulePublishTokenRules()
    {
        var tag = new ModulePublishTagBuilder().BuildTag(
            new PublishConfiguration {
                OverwriteTagName = "{TagModuleVersionWithPreRelease}"
            },
            moduleName: "PSPublishModule",
            resolvedVersion: "2.0.0",
            preRelease: "preview1");

        Assert.Equal("v2.0.0-preview1", tag);
    }
}
