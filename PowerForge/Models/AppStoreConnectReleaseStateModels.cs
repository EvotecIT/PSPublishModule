namespace PowerForge;

/// <summary>
/// Request to summarize App Store Connect release state for one app/version/build.
/// </summary>
public sealed class AppStoreConnectReleaseStateRequest
{
    /// <summary>App Store Connect API credential. Required when executed by the default cmdlet runner.</summary>
    public AppStoreConnectApiCredential? Credential { get; set; }

    /// <summary>App Store Connect app id.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>Optional App Store marketing version to summarize.</summary>
    public string? VersionString { get; set; }

    /// <summary>Optional uploaded build number expected for the version.</summary>
    public string? BuildNumber { get; set; }

    /// <summary>Platforms to summarize.</summary>
    public ApplePlatform[] Platforms { get; set; } = new[] { ApplePlatform.iOS };

    /// <summary>Beta group ids to include in the summary.</summary>
    public string[] BetaGroupIds { get; set; } = Array.Empty<string>();

    /// <summary>Beta group names to resolve and include in the summary.</summary>
    public string[] BetaGroupNames { get; set; } = Array.Empty<string>();

    /// <summary>Include all beta groups when no group filters are supplied.</summary>
    public bool IncludeAllBetaGroups { get; set; }
}

/// <summary>
/// App Store Connect release state summary for an app.
/// </summary>
public sealed class AppStoreConnectReleaseStateResult
{
    /// <summary>App Store Connect app id.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>Version string used for the summary.</summary>
    public string? VersionString { get; set; }

    /// <summary>Build number used for the summary.</summary>
    public string? BuildNumber { get; set; }

    /// <summary>UTC time when the summary was gathered.</summary>
    public DateTimeOffset CheckedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Per-platform App Store and TestFlight state.</summary>
    public AppStoreConnectPlatformReleaseState[] Platforms { get; set; } = Array.Empty<AppStoreConnectPlatformReleaseState>();

    /// <summary>Included beta group state.</summary>
    public AppStoreConnectBetaGroupReleaseState[] BetaGroups { get; set; } = Array.Empty<AppStoreConnectBetaGroupReleaseState>();

    /// <summary>Flattened next actions across platforms and beta groups.</summary>
    public string[] NextActions { get; set; } = Array.Empty<string>();

    /// <summary>Messages useful for release logs.</summary>
    public string[] Messages { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Per-platform App Store Connect release state.
/// </summary>
public sealed class AppStoreConnectPlatformReleaseState
{
    /// <summary>Apple platform.</summary>
    public ApplePlatform Platform { get; set; } = ApplePlatform.iOS;

    /// <summary>Matched App Store version, when present.</summary>
    public AppStoreConnectVersionInfo? Version { get; set; }

    /// <summary>Build selected on the App Store version, when present.</summary>
    public AppStoreConnectBuildInfo? SelectedBuild { get; set; }

    /// <summary>Build matching the requested build number/version/platform, when present.</summary>
    public AppStoreConnectBuildInfo? MatchedBuild { get; set; }

    /// <summary>Build used for TestFlight state checks.</summary>
    public AppStoreConnectBuildInfo? TestFlightBuild { get; set; }

    /// <summary>Whether the matched build is selected on the App Store version.</summary>
    public bool? MatchedBuildSelected { get; set; }

    /// <summary>Recent/current production App Review submissions for this app/platform.</summary>
    public AppStoreConnectReviewSubmissionInfo[] ReviewSubmissions { get; set; } = Array.Empty<AppStoreConnectReviewSubmissionInfo>();

    /// <summary>TestFlight beta build detail for the matched or selected build.</summary>
    public AppStoreConnectBuildBetaDetailInfo? BetaDetail { get; set; }

    /// <summary>Beta App Review submission for the matched or selected build.</summary>
    public AppStoreConnectBetaAppReviewSubmissionInfo? BetaReviewSubmission { get; set; }

    /// <summary>Next actions inferred from this platform state.</summary>
    public string[] NextActions { get; set; } = Array.Empty<string>();

    /// <summary>Messages useful for release logs.</summary>
    public string[] Messages { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Beta group release state useful for public TestFlight links.
/// </summary>
public sealed class AppStoreConnectBetaGroupReleaseState
{
    /// <summary>Beta group id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Beta group name.</summary>
    public string? Name { get; set; }

    /// <summary>Whether public links are enabled for the group.</summary>
    public bool? PublicLinkEnabled { get; set; }

    /// <summary>Public invitation link when available.</summary>
    public string? PublicLink { get; set; }

    /// <summary>Configured public link tester limit.</summary>
    public int? PublicLinkLimit { get; set; }

    /// <summary>Current tester count read from the group.</summary>
    public int? TesterCount { get; set; }

    /// <summary>Whether the group is full according to the public-link limit and tester count.</summary>
    public bool? IsFull { get; set; }

    /// <summary>Whether this is an internal beta group when reported by App Store Connect.</summary>
    public bool? IsInternalGroup { get; set; }

    /// <summary>Next actions inferred from this beta group state.</summary>
    public string[] NextActions { get; set; } = Array.Empty<string>();
}
