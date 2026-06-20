namespace PowerForge;

/// <summary>
/// App Store Connect manual version release request summary.
/// </summary>
public sealed class AppStoreConnectVersionReleaseRequestInfo
{
    /// <summary>Release request id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>App Store version id associated with the release request.</summary>
    public string? AppStoreVersionId { get; set; }
}

/// <summary>
/// Request to manually release an approved App Store version.
/// </summary>
public sealed class AppStoreConnectVersionReleaseRequest
{
    /// <summary>App Store Connect API credential. Required when the request is executed by the default release runner.</summary>
    public AppStoreConnectApiCredential? Credential { get; set; }

    /// <summary>App Store Connect app id.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>App Store marketing version.</summary>
    public string VersionString { get; set; } = string.Empty;

    /// <summary>Apple platform for the App Store version.</summary>
    public ApplePlatform Platform { get; set; } = ApplePlatform.iOS;

    /// <summary>Require the version to be in Pending Developer Release before requesting release.</summary>
    public bool RequirePendingDeveloperRelease { get; set; } = true;
}

/// <summary>
/// Result of manually releasing an approved App Store version.
/// </summary>
public sealed class AppStoreConnectVersionReleaseResult
{
    /// <summary>App Store Connect app id.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>App Store marketing version.</summary>
    public string VersionString { get; set; } = string.Empty;

    /// <summary>Apple platform for the App Store version.</summary>
    public ApplePlatform Platform { get; set; } = ApplePlatform.iOS;

    /// <summary>Matched App Store version.</summary>
    public AppStoreConnectVersionInfo Version { get; set; } = new();

    /// <summary>Created release request.</summary>
    public AppStoreConnectVersionReleaseRequestInfo ReleaseRequest { get; set; } = new();

    /// <summary>Release messages useful for release logs.</summary>
    public string[] Messages { get; set; } = Array.Empty<string>();
}
