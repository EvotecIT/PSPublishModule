namespace PowerForge.Tests;

public sealed class AppleReleaseMarketingVersionResolverTests
{
    [Fact]
    public void Resolve_AdvancesMinorTrainAfterCurrentVersionReachesTheStore()
    {
        var result = Resolve(
            pattern: "1.X.0",
            local: "1.5.0",
            store: new[] { StoreVersion("1.5.0", "READY_FOR_SALE") });

        Assert.Equal("1.6.0", result.MarketingVersion);
        Assert.Equal("1.5.0", result.HighestRemoteMarketingVersion);
        Assert.False(result.ReusedUnreleasedMarketingVersion);
    }

    [Fact]
    public void Resolve_ReusesUnreleasedLocalTrainForRepeatedTestFlightBuilds()
    {
        var result = Resolve(
            pattern: "1.X.0",
            local: "1.6.0",
            store: new[] { StoreVersion("1.5.0", "READY_FOR_SALE") },
            builds: new[] { TestFlightBuild("1.6.0", "14") });

        Assert.Equal("1.6.0", result.MarketingVersion);
        Assert.Equal("1.6.0", result.HighestRemoteMarketingVersion);
        Assert.True(result.ReusedUnreleasedMarketingVersion);
    }

    [Fact]
    public void Resolve_ResumesHigherRemoteTestFlightTrainWhenSourceIsStale()
    {
        var result = Resolve(
            pattern: "1.X.0",
            local: "1.6.0",
            store: new[] { StoreVersion("1.5.0", "READY_FOR_SALE") },
            builds: new[] { TestFlightBuild("1.7.0", "18") });

        Assert.Equal("1.7.0", result.MarketingVersion);
        Assert.True(result.ReusedUnreleasedMarketingVersion);
    }

    [Fact]
    public void Resolve_ReusesEditableAppStoreDraft()
    {
        var result = Resolve(
            pattern: "1.X.0",
            local: "1.6.0",
            store: new[]
            {
                StoreVersion("1.5.0", "READY_FOR_SALE"),
                StoreVersion("1.6.0", "PREPARE_FOR_SUBMISSION")
            });

        Assert.Equal("1.6.0", result.MarketingVersion);
        Assert.True(result.ReusedUnreleasedMarketingVersion);
    }

    [Theory]
    [InlineData("WAITING_FOR_REVIEW")]
    [InlineData("PENDING_DEVELOPER_RELEASE")]
    [InlineData("PROCESSING_FOR_DISTRIBUTION")]
    [InlineData("READY_FOR_DISTRIBUTION")]
    [InlineData(null)]
    public void Resolve_AdvancesPastNonEditableOrUnknownAppStoreTrain(string? state)
    {
        var result = Resolve(
            pattern: "1.X.0",
            local: "1.6.0",
            store: new[] { StoreVersion("1.6.0", state) },
            builds: new[] { TestFlightBuild("1.6.0", "17") });

        Assert.Equal("1.7.0", result.MarketingVersion);
        Assert.False(result.ReusedUnreleasedMarketingVersion);
    }

    [Fact]
    public void Resolve_NewMajorPatternStartsAtItsZeroMinorTrain()
    {
        var result = Resolve(
            pattern: "2.X.0",
            local: "1.8.0",
            store: new[] { StoreVersion("1.8.0", "READY_FOR_SALE") });

        Assert.Equal("2.0.0", result.MarketingVersion);
    }

    [Fact]
    public void Resolve_SharedTwoPartPatternAdvancesMinorAndPreservesItsShape()
    {
        var result = Resolve(
            pattern: "1.X",
            local: "1.5.0",
            store: new[] { StoreVersion("1.5.0", "READY_FOR_SALE") });

        Assert.Equal("1.6", result.MarketingVersion);
    }

    [Fact]
    public void Resolve_SharedMajorPatternAdvancesToTheNextMajorTrain()
    {
        var result = Resolve(
            pattern: "X.0.0",
            local: "1.5.0",
            store: new[] { StoreVersion("1.5.0", "READY_FOR_SALE") });

        Assert.Equal("2.0.0", result.MarketingVersion);
    }

    [Fact]
    public void Resolve_PreservesExistingTwoPartRemoteTrainIdentity()
    {
        var result = Resolve(
            pattern: "1.X",
            local: "1.5.0",
            store: new[] { StoreVersion("1.5.0", "READY_FOR_SALE") },
            builds: new[] { TestFlightBuild("1.6", "18") });

        Assert.Equal("1.6", result.MarketingVersion);
        Assert.Equal("1.6", result.HighestRemoteMarketingVersion);
        Assert.True(result.ReusedUnreleasedMarketingVersion);
    }

    [Fact]
    public void Resolve_PrefersRemoteTrainIdentityWhenLocalFormattingDiffers()
    {
        var result = Resolve(
            pattern: "1.X",
            local: "1.6.0",
            store: new[] { StoreVersion("1.5.0", "READY_FOR_SALE") },
            builds: new[] { TestFlightBuild("1.6", "18") });

        Assert.Equal("1.6", result.MarketingVersion);
        Assert.True(result.ReusedUnreleasedMarketingVersion);
    }

    [Fact]
    public void Resolve_RejectsPatternThatWouldMoveBehindExistingMajorLine()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Resolve(
            pattern: "1.X.0",
            local: "2.0.0",
            store: new[] { StoreVersion("2.0.0", "READY_FOR_SALE") }));

        Assert.Contains("fixed prefix is lower", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1.X.X")]
    [InlineData("X.X.X")]
    [InlineData("1.*.0")]
    [InlineData("1.2.3")]
    [InlineData("1.2.3.X")]
    public void ValidatePattern_RejectsIncompatiblePatterns(string pattern)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            AppleReleaseMarketingVersionResolver.ValidatePattern(pattern, "VersionPattern"));

        Assert.Contains("shared PSPublishModule X-pattern semantics", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_FailsClosedForNonnumericRemoteVersion()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Resolve(
            pattern: "1.X.0",
            local: "1.6.0",
            builds: new[] { TestFlightBuild("1.7-beta", "18") }));

        Assert.Contains("incompatible remote version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_FailsClosedForAppStoreVersionWithoutIdentity()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Resolve(
            pattern: "1.X.0",
            local: "1.6.0",
            store: new[] { StoreVersion(string.Empty, "READY_FOR_SALE") }));

        Assert.Contains("incomplete remote version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_FailsClosedForTestFlightBuildWithoutMarketingVersion()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Resolve(
            pattern: "1.X.0",
            local: "1.6.0",
            builds: new[] { TestFlightBuild(string.Empty, "18") }));

        Assert.Contains("incomplete remote build", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null, "incomplete remote evidence")]
    [InlineData("", "incomplete remote evidence")]
    [InlineData("NEW_OS", "incompatible remote evidence")]
    public void Resolve_FailsClosedForUnknownTestFlightPlatform(string? platform, string expectedMessage)
    {
        var build = TestFlightBuild("1.6.0", "18");
        build.Platform = platform;

        var exception = Assert.Throws<InvalidOperationException>(() => Resolve(
            pattern: "1.X.0",
            local: "1.6.0",
            builds: new[] { build }));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null, "incomplete remote evidence")]
    [InlineData("NEW_OS", "incompatible remote evidence")]
    public void Resolve_FailsClosedForUnknownAppStorePlatform(string? platform, string expectedMessage)
    {
        var version = StoreVersion("1.6.0", "READY_FOR_SALE");
        version.Platform = platform;

        var exception = Assert.Throws<InvalidOperationException>(() => Resolve(
            pattern: "1.X.0",
            local: "1.6.0",
            store: new[] { version }));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static AppleReleaseMarketingVersionResolution Resolve(
        string pattern,
        string local,
        AppStoreConnectVersionInfo[]? store = null,
        AppStoreConnectBuildInfo[]? builds = null)
        => AppleReleaseMarketingVersionResolver.Resolve(
            pattern,
            local,
            store ?? Array.Empty<AppStoreConnectVersionInfo>(),
            builds ?? Array.Empty<AppStoreConnectBuildInfo>());

    private static AppStoreConnectVersionInfo StoreVersion(string version, string? state)
        => new()
        {
            VersionString = version,
            AppStoreState = state,
            Platform = "IOS"
        };

    private static AppStoreConnectBuildInfo TestFlightBuild(string version, string build)
        => new()
        {
            MarketingVersion = version,
            Version = build,
            Platform = "IOS"
        };
}
