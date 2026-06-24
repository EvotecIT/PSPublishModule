namespace PowerForge;

/// <summary>
/// App Store Connect TestFlight beta group summary.
/// </summary>
public sealed class AppStoreConnectBetaGroupInfo
{
    /// <summary>Beta group id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Beta group name.</summary>
    public string? Name { get; set; }

    /// <summary>Whether public links are enabled for the group.</summary>
    public bool? PublicLinkEnabled { get; set; }

    /// <summary>Public link limit when configured.</summary>
    public int? PublicLinkLimit { get; set; }

    /// <summary>Public invitation link when available.</summary>
    public string? PublicLink { get; set; }

    /// <summary>Whether crash feedback is enabled.</summary>
    public bool? FeedbackEnabled { get; set; }

    /// <summary>Whether this is an internal beta group when reported by App Store Connect.</summary>
    public bool? IsInternalGroup { get; set; }
}

/// <summary>
/// App Store Connect TestFlight beta tester summary.
/// </summary>
public sealed class AppStoreConnectBetaTesterInfo
{
    /// <summary>Beta tester id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Tester email address.</summary>
    public string? Email { get; set; }

    /// <summary>Tester first name.</summary>
    public string? FirstName { get; set; }

    /// <summary>Tester last name.</summary>
    public string? LastName { get; set; }

    /// <summary>Tester invitation type or state when available.</summary>
    public string? InviteType { get; set; }
}

/// <summary>
/// App Store Connect TestFlight build beta detail summary.
/// </summary>
public sealed class AppStoreConnectBuildBetaDetailInfo
{
    /// <summary>Build beta detail id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Internal TestFlight build state.</summary>
    public string? InternalBuildState { get; set; }

    /// <summary>External TestFlight build state.</summary>
    public string? ExternalBuildState { get; set; }

    /// <summary>Whether App Store Connect automatically notifies testers when available.</summary>
    public bool? AutoNotifyEnabled { get; set; }
}

/// <summary>
/// Tester specification for TestFlight distribution.
/// </summary>
public sealed class AppStoreConnectBetaTesterSpec
{
    /// <summary>Tester email address.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Optional tester first name.</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional tester last name.</summary>
    public string? LastName { get; set; }
}

/// <summary>
/// Request to distribute a TestFlight build to beta groups and testers.
/// </summary>
public sealed class AppStoreConnectTestFlightDistributionRequest
{
    /// <summary>App Store Connect API credential. Required when the request is executed by the default release runner.</summary>
    public AppStoreConnectApiCredential? Credential { get; set; }

    /// <summary>App Store Connect app id.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>App Store marketing version.</summary>
    public string VersionString { get; set; } = string.Empty;

    /// <summary>Uploaded build number.</summary>
    public string BuildNumber { get; set; } = string.Empty;

    /// <summary>Apple platform for the build.</summary>
    public ApplePlatform Platform { get; set; } = ApplePlatform.iOS;

    /// <summary>Beta group ids to receive the build.</summary>
    public string[] BetaGroupIds { get; set; } = Array.Empty<string>();

    /// <summary>Beta group names to resolve and receive the build.</summary>
    public string[] BetaGroupNames { get; set; } = Array.Empty<string>();

    /// <summary>Optional testers to create or resolve and add to the target groups.</summary>
    public AppStoreConnectBetaTesterSpec[] Testers { get; set; } = Array.Empty<AppStoreConnectBetaTesterSpec>();

    /// <summary>Create testers that do not already exist.</summary>
    public bool CreateMissingTesters { get; set; } = true;

    /// <summary>Require the build to be valid and not expired before assigning it to beta groups.</summary>
    public bool RequireValidBuild { get; set; } = true;
}

/// <summary>
/// Result of distributing a TestFlight build to beta groups and testers.
/// </summary>
public sealed class AppStoreConnectTestFlightDistributionResult
{
    /// <summary>Matched build.</summary>
    public AppStoreConnectBuildInfo Build { get; set; } = new();

    /// <summary>Target beta groups.</summary>
    public AppStoreConnectBetaGroupInfo[] BetaGroups { get; set; } = Array.Empty<AppStoreConnectBetaGroupInfo>();

    /// <summary>Created or resolved beta testers.</summary>
    public AppStoreConnectBetaTesterInfo[] Testers { get; set; } = Array.Empty<AppStoreConnectBetaTesterInfo>();

    /// <summary>Distribution messages useful for release logs.</summary>
    public string[] Messages { get; set; } = Array.Empty<string>();
}

/// <summary>
/// App Store Connect Beta App Review submission summary.
/// </summary>
public sealed class AppStoreConnectBetaAppReviewSubmissionInfo
{
    /// <summary>Beta App Review submission id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Current beta review state reported by App Store Connect.</summary>
    public string? BetaReviewState { get; set; }

    /// <summary>Date the beta submission was submitted, when reported by App Store Connect.</summary>
    public DateTimeOffset? SubmittedDate { get; set; }

    /// <summary>Build id associated with the beta submission when included.</summary>
    public string? BuildId { get; set; }
}

/// <summary>
/// Request to submit a TestFlight build to Beta App Review for external testing.
/// </summary>
public sealed class AppStoreConnectBetaAppReviewSubmissionRequest
{
    /// <summary>App Store Connect API credential. Required when the request is executed by the default release runner.</summary>
    public AppStoreConnectApiCredential? Credential { get; set; }

    /// <summary>App Store Connect app id.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>App Store marketing version.</summary>
    public string VersionString { get; set; } = string.Empty;

    /// <summary>Uploaded build number.</summary>
    public string BuildNumber { get; set; } = string.Empty;

    /// <summary>Apple platform for the build.</summary>
    public ApplePlatform Platform { get; set; } = ApplePlatform.iOS;

    /// <summary>Require the build to be valid and not expired before Beta App Review submission.</summary>
    public bool RequireValidBuild { get; set; } = true;
}

/// <summary>
/// Result of submitting a TestFlight build to Beta App Review.
/// </summary>
public sealed class AppStoreConnectBetaAppReviewSubmissionResult
{
    /// <summary>App Store Connect app id.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>App Store marketing version.</summary>
    public string VersionString { get; set; } = string.Empty;

    /// <summary>Uploaded build number.</summary>
    public string BuildNumber { get; set; } = string.Empty;

    /// <summary>Apple platform for the submission.</summary>
    public ApplePlatform Platform { get; set; } = ApplePlatform.iOS;

    /// <summary>Matched build.</summary>
    public AppStoreConnectBuildInfo Build { get; set; } = new();

    /// <summary>Beta App Review submission created or reused by the operation.</summary>
    public AppStoreConnectBetaAppReviewSubmissionInfo Submission { get; set; } = new();

    /// <summary>Submission messages useful for release logs.</summary>
    public string[] Messages { get; set; } = Array.Empty<string>();
}
