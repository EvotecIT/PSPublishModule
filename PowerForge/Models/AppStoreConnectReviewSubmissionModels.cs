namespace PowerForge;

/// <summary>
/// App Store Connect review submission summary.
/// </summary>
public sealed class AppStoreConnectReviewSubmissionInfo
{
    /// <summary>Review submission id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Submission platform.</summary>
    public string? Platform { get; set; }

    /// <summary>Whether the submission has been sent to App Review.</summary>
    public bool? IsSubmitted { get; set; }

    /// <summary>Submission state when reported by App Store Connect.</summary>
    public string? State { get; set; }
}

/// <summary>
/// App Store Connect review submission item summary.
/// </summary>
public sealed class AppStoreConnectReviewSubmissionItemInfo
{
    /// <summary>Review submission item id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Review submission id associated with the item.</summary>
    public string? ReviewSubmissionId { get; set; }

    /// <summary>App Store version id associated with the item.</summary>
    public string? AppStoreVersionId { get; set; }
}

/// <summary>
/// Request to submit an App Store version to App Review.
/// </summary>
public sealed class AppStoreConnectReviewSubmissionRequest
{
    /// <summary>App Store Connect API credential. Required when the request is executed by the default release runner.</summary>
    public AppStoreConnectApiCredential? Credential { get; set; }

    /// <summary>App Store Connect app id.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>App Store marketing version.</summary>
    public string VersionString { get; set; } = string.Empty;

    /// <summary>Uploaded build number expected on the Distribution version.</summary>
    public string BuildNumber { get; set; } = string.Empty;

    /// <summary>Apple platform for the Distribution version.</summary>
    public ApplePlatform Platform { get; set; } = ApplePlatform.iOS;

    /// <summary>Require the Distribution version to already have the requested build selected.</summary>
    public bool RequireSelectedBuild { get; set; } = true;

    /// <summary>Require the selected build to be valid and not expired before submission.</summary>
    public bool RequireValidBuild { get; set; } = true;

    /// <summary>Run release readiness checks before creating the review submission.</summary>
    public bool CheckReadiness { get; set; } = true;

    /// <summary>Fail when readiness checks do not pass.</summary>
    public bool RequireReady { get; set; } = true;

    /// <summary>Optional readiness request overrides.</summary>
    public AppStoreConnectReleaseReadinessRequest? ReadinessRequest { get; set; }
}

/// <summary>
/// Result of submitting an App Store version to App Review.
/// </summary>
public sealed class AppStoreConnectReviewSubmissionResult
{
    /// <summary>App Store Connect app id.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>App Store marketing version.</summary>
    public string VersionString { get; set; } = string.Empty;

    /// <summary>Uploaded build number.</summary>
    public string BuildNumber { get; set; } = string.Empty;

    /// <summary>Apple platform for the submission.</summary>
    public ApplePlatform Platform { get; set; } = ApplePlatform.iOS;

    /// <summary>Matched App Store version.</summary>
    public AppStoreConnectVersionInfo Version { get; set; } = new();

    /// <summary>Matched selected build.</summary>
    public AppStoreConnectBuildInfo? Build { get; set; }

    /// <summary>Review submission created or reused by the operation.</summary>
    public AppStoreConnectReviewSubmissionInfo ReviewSubmission { get; set; } = new();

    /// <summary>Review submission item created for the App Store version.</summary>
    public AppStoreConnectReviewSubmissionItemInfo? ReviewSubmissionItem { get; set; }

    /// <summary>Release readiness result when requested.</summary>
    public AppStoreConnectReleaseReadinessResult? Readiness { get; set; }

    /// <summary>Submission messages useful for release logs.</summary>
    public string[] Messages { get; set; } = Array.Empty<string>();
}
