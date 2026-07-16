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

    [Fact]
    public void ValidateExpectedExistingRelease_RejectsUnverifiedConflictBeforeAssetReplacement()
    {
        var missing = Assert.Throws<InvalidOperationException>(() =>
            GitHubReleasePublisher.ValidateExpectedExistingRelease("v1.2.3", true, null, 99));
        var mismatch = Assert.Throws<InvalidOperationException>(() =>
            GitHubReleasePublisher.ValidateExpectedExistingRelease("v1.2.3", true, 42, 99));

        Assert.Contains("not preflight-verified", missing.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not preflight-verified", mismatch.Message, StringComparison.OrdinalIgnoreCase);
        GitHubReleasePublisher.ValidateExpectedExistingRelease("v1.2.3", true, 99, 99);
    }

    [Fact]
    public void ValidateExpectedReleaseState_RejectsReleaseOrTagChangesBeforeAssetMutation()
    {
        const string marker = "<!-- powerforge-homeassistant source-pr:42 -->";
        const string commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        Assert.Throws<InvalidOperationException>(() => GitHubReleasePublisher.ValidateExpectedReleaseState(
            "v1.2.3", 42, 99, marker, marker, commit, commit));
        Assert.Throws<InvalidOperationException>(() => GitHubReleasePublisher.ValidateExpectedReleaseState(
            "v1.2.3", 42, 42, "foreign body", marker, commit, commit));
        Assert.Throws<InvalidOperationException>(() => GitHubReleasePublisher.ValidateExpectedReleaseState(
            "v1.2.3", 42, 42, marker, marker, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", commit));

        GitHubReleasePublisher.ValidateExpectedReleaseState(
            "v1.2.3", 42, 42, marker, marker, commit, commit);
    }

    [Fact]
    public void BuildApiUri_PreservesGitHubEnterpriseApiPrefix()
    {
        var uri = GitHubReleasePublisher.BuildApiUri(
            "https://github.enterprise.example/api/v3/",
            "/repos/EvotecIT/example/releases");

        Assert.Equal("https://github.enterprise.example/api/v3/repos/EvotecIT/example/releases", uri.AbsoluteUri);
    }
}
