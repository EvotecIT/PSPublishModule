using PowerForge;

namespace PowerForge.Tests;

public sealed class GitHubReleasePublisherTests
{
    [Fact]
    public void PublishRelease_ThrowsWhenReleaseNotesConflictWithGenerateReleaseNotes()
    {
        var publisher = new GitHubReleasePublisher(new NullLogger());

        var request = new GitHubReleasePublishRequest
        {
            Owner = "EvotecIT",
            Repository = "PSPublishModule",
            Token = "token",
            TagName = "v1.2.3",
            ReleaseNotes = "notes",
            GenerateReleaseNotes = true
        };

        var ex = Assert.Throws<ArgumentException>(() => publisher.PublishRelease(request));
        Assert.Contains("ReleaseNotes", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublishRelease_ThrowsWhenAssetDoesNotExist()
    {
        var publisher = new GitHubReleasePublisher(new NullLogger());

        var request = new GitHubReleasePublishRequest
        {
            Owner = "EvotecIT",
            Repository = "PSPublishModule",
            Token = "token",
            TagName = "v1.2.3",
            AssetFilePaths = new[] { Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip") }
        };

        Assert.Throws<FileNotFoundException>(() => publisher.PublishRelease(request));
    }

    [Fact]
    public void TryReserveExistingAssetNameForReplacement_AllowsOnlyAssetsFromOriginalSnapshot()
    {
        var replaceableAssetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PowerForge-win-x64.zip"
        };

        Assert.True(GitHubReleasePublisher.TryReserveExistingAssetNameForReplacement(replaceableAssetNames, "powerforge-win-x64.zip"));
        Assert.False(GitHubReleasePublisher.TryReserveExistingAssetNameForReplacement(replaceableAssetNames, "PowerForge-win-x64.zip"));
    }
}
