namespace PowerForge.Tests;

public sealed class GitHubReleaseVersionAvailabilityServiceTests
{
    [Fact]
    public void EnsureAvailable_StepsAcrossOccupiedReleaseAndTag()
    {
        var probedTags = new List<string>();
        var service = new GitHubReleaseVersionAvailabilityService(
            new NullLogger(),
            (_, _, _, tag) =>
            {
                probedTags.Add(tag);
                return tag switch
                {
                    "v3.0.77" => new GitHubReleaseVersionOccupancy { ReleaseExists = true },
                    "v3.0.78" => new GitHubReleaseVersionOccupancy { TagExists = true },
                    _ => new GitHubReleaseVersionOccupancy()
                };
            });

        var version = service.EnsureAvailable(
            "3.0.X",
            "3.0.77",
            "EvotecIT",
            "PSPublishModule",
            "token",
            candidate => "v" + candidate,
            reuseExistingRelease: false);

        Assert.Equal("3.0.79", version);
        Assert.Equal(["v3.0.77", "v3.0.78", "v3.0.79"], probedTags);
    }

    [Fact]
    public void EnsureAvailable_RejectsOccupiedExactVersionWithoutRecoveryMode()
    {
        var service = new GitHubReleaseVersionAvailabilityService(
            new NullLogger(),
            (_, _, _, _) => new GitHubReleaseVersionOccupancy { ReleaseExists = true });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            service.EnsureAvailable(
                "3.0.77",
                "3.0.77",
                "EvotecIT",
                "PSPublishModule",
                "token",
                candidate => "v" + candidate,
                reuseExistingRelease: false));

        Assert.Contains("exact release version", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ReuseExistingRelease", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureAvailable_AllowsExplicitRecoveryOfOccupiedVersion()
    {
        var service = new GitHubReleaseVersionAvailabilityService(
            new NullLogger(),
            (_, _, _, _) => new GitHubReleaseVersionOccupancy { ReleaseExists = true });

        var version = service.EnsureAvailable(
            "3.0.X",
            "3.0.77",
            "EvotecIT",
            "PSPublishModule",
            "token",
            candidate => "v" + candidate,
            reuseExistingRelease: true);

        Assert.Equal("3.0.77", version);
    }
}
