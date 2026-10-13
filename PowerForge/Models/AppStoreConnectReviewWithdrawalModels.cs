namespace PowerForge;

/// <summary>Identifies one submitted version and build to remove from App Review.</summary>
public sealed class AppStoreConnectReviewWithdrawalRequest
{
    /// <summary>App Store Connect app id.</summary>
    public string AppId { get; set; } = string.Empty;
    /// <summary>Marketing version expected on the submitted version.</summary>
    public string VersionString { get; set; } = string.Empty;
    /// <summary>Build number expected to remain selected.</summary>
    public string BuildNumber { get; set; } = string.Empty;
    /// <summary>Store platform for the submitted version.</summary>
    public ApplePlatform Platform { get; set; } = ApplePlatform.iOS;
    /// <summary>Exact review submission to cancel; never selected implicitly.</summary>
    public string ReviewSubmissionId { get; set; } = string.Empty;
}

/// <summary>Read-only identity and state binding for a single-item review withdrawal.</summary>
public sealed class AppStoreConnectReviewWithdrawalPlan
{
    /// <summary>Bound app id.</summary>
    public string AppId { get; set; } = string.Empty;
    /// <summary>Bound marketing version.</summary>
    public string VersionString { get; set; } = string.Empty;
    /// <summary>Bound build number.</summary>
    public string BuildNumber { get; set; } = string.Empty;
    /// <summary>Bound store platform.</summary>
    public ApplePlatform Platform { get; set; }
    /// <summary>Bound App Store version id.</summary>
    public string AppStoreVersionId { get; set; } = string.Empty;
    /// <summary>Bound selected build id.</summary>
    public string BuildId { get; set; } = string.Empty;
    /// <summary>Exact review submission id.</summary>
    public string ReviewSubmissionId { get; set; } = string.Empty;
    /// <summary>Sole item in the submission.</summary>
    public string ReviewSubmissionItemId { get; set; } = string.Empty;
    /// <summary>App Store version state before withdrawal.</summary>
    public string? AppStoreState { get; set; }
    /// <summary>App version state before withdrawal.</summary>
    public string? AppVersionState { get; set; }
    /// <summary>Submission state before withdrawal.</summary>
    public string? SubmissionState { get; set; }
    /// <summary>Item state before withdrawal.</summary>
    public string? ItemState { get; set; }
}

/// <summary>Distinguishes an accepted cancellation request from observed withdrawal.</summary>
public sealed class AppStoreConnectReviewWithdrawalResult
{
    /// <summary>Exact target and state revalidated before the mutation.</summary>
    public AppStoreConnectReviewWithdrawalPlan Plan { get; set; } = new();
    /// <summary>Whether Apple accepted the cancellation request.</summary>
    public bool CancellationRequested { get; set; }
    /// <summary>Whether readback observed Developer Rejected for the bound version.</summary>
    public bool WithdrawalObserved { get; set; }
    /// <summary>Submission state from the cancellation response.</summary>
    public string? ResponseSubmissionState { get; set; }
    /// <summary>Submission state from the subsequent readback.</summary>
    public string? ObservedSubmissionState { get; set; }
    /// <summary>App Store version state from the subsequent readback.</summary>
    public string? ObservedVersionState { get; set; }
}
