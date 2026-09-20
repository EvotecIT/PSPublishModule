using PowerForge;
using System.Text.Json;

namespace PowerForgeStudio.Tests;

public sealed class PublicationPreviewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreviewUsesSharedFeedRulesAndDoesNotOpenCredentialFiles(bool githubPackages)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-publish-preview-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var secret = Path.Combine(root, "locked-secret"); File.WriteAllText(secret, "must-not-appear");
            using var locked = new FileStream(secret, FileMode.Open, FileAccess.Read, FileShare.None);
            var config = Path.Combine(root, "project.build.json");
            File.WriteAllText(config, JsonSerializer.Serialize(new {
                PublishNuget = true, PublishGitHub = true, UseGitHubPackages = githubPackages,
                GitHubUsername = "EvotecIT", GitHubRepositoryName = "Fixture",
                PublishApiKeyFilePath = secret, GitHubAccessTokenFilePath = secret, NugetCredentialSecretFilePath = secret,
                PublishApiKey = "inline-key-must-not-appear", GitHubAccessToken = "inline-token-must-not-appear"
            }));
            var preview = new ProjectBuildPublishHostService().PreviewConfiguration(config);
            Assert.True(preview.PublishNuGet); Assert.True(preview.PublishGitHub);
            Assert.Equal(githubPackages ? "https://nuget.pkg.github.com/EvotecIT/index.json" : "https://api.nuget.org/v3/index.json", preview.NuGetDestination, ignoreCase: true);
            Assert.Equal("EvotecIT/Fixture", preview.GitHubRepository);
            Assert.DoesNotContain("must-not-appear", JsonSerializer.Serialize(preview));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("https://user:password@example.test/feed?key=private#secret", "https://example.test/feed")]
    [InlineData("https://user:password@example.test:bad/feed?key=private#secret", "Invalid publication URL (details omitted)")]
    [InlineData("https://user:password@[invalid/feed?key=private#secret", "Invalid publication URL (details omitted)")]
    public void PreviewRemovesUriCredentialComponents(string source, string expected)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-preview-redaction-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var config = Path.Combine(root, "project.build.json");
            File.WriteAllText(config, JsonSerializer.Serialize(new { PublishSource = source }));
            var preview = new ProjectBuildPublishHostService().PreviewConfiguration(config);
            Assert.True(preview.DestinationRedacted); Assert.Equal(expected, preview.NuGetDestination);
        }
        finally { Directory.Delete(root, true); }
    }
}
