namespace PowerForge;

/// <summary>
/// App Store Connect app summary.
/// </summary>
public sealed class AppStoreConnectAppInfo
{
    /// <summary>App Store Connect app id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>App name.</summary>
    public string? Name { get; set; }

    /// <summary>Bundle identifier.</summary>
    public string? BundleId { get; set; }

    /// <summary>SKU.</summary>
    public string? Sku { get; set; }

    /// <summary>Primary locale.</summary>
    public string? PrimaryLocale { get; set; }
}

/// <summary>
/// App Store Connect App Store version summary.
/// </summary>
public sealed class AppStoreConnectVersionInfo
{
    /// <summary>App Store version id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>App Store version string.</summary>
    public string? VersionString { get; set; }

    /// <summary>App Store state.</summary>
    public string? AppStoreState { get; set; }

    /// <summary>App version state.</summary>
    public string? AppVersionState { get; set; }

    /// <summary>Platform value returned by App Store Connect.</summary>
    public string? Platform { get; set; }
}

/// <summary>
/// App Store Connect build summary.
/// </summary>
public sealed class AppStoreConnectBuildInfo
{
    /// <summary>Build id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Build number/version.</summary>
    public string? Version { get; set; }

    /// <summary>Build processing state.</summary>
    public string? ProcessingState { get; set; }

    /// <summary>Uploaded date when available.</summary>
    public DateTimeOffset? UploadedDate { get; set; }

    /// <summary>Expired flag when available.</summary>
    public bool? Expired { get; set; }

    /// <summary>Minimum OS version when available.</summary>
    public string? MinOsVersion { get; set; }

    /// <summary>Marketing version from the related pre-release version when included.</summary>
    public string? MarketingVersion { get; set; }

    /// <summary>Platform from the related pre-release version when included.</summary>
    public string? Platform { get; set; }
}

/// <summary>
/// App Store Connect state for a package accepted by the upload transport before it becomes a build.
/// </summary>
public sealed class AppStoreConnectBuildUploadInfo
{
    /// <summary>Build upload id returned by Xcode delivery.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Marketing version declared by the uploaded bundle.</summary>
    public string? MarketingVersion { get; set; }

    /// <summary>Build number declared by the uploaded bundle.</summary>
    public string? BuildNumber { get; set; }

    /// <summary>Platform value returned by App Store Connect.</summary>
    public string? Platform { get; set; }

    /// <summary>Upload processing state such as AWAITING_PROCESSING or FAILED.</summary>
    public string? State { get; set; }

    /// <summary>Terminal validation errors reported for the uploaded package.</summary>
    public AppStoreConnectBuildUploadIssue[] Errors { get; set; } = Array.Empty<AppStoreConnectBuildUploadIssue>();

    /// <summary>Non-terminal validation warnings reported for the uploaded package.</summary>
    public AppStoreConnectBuildUploadIssue[] Warnings { get; set; } = Array.Empty<AppStoreConnectBuildUploadIssue>();

    /// <summary>Upload date when available.</summary>
    public DateTimeOffset? UploadedDate { get; set; }
}

/// <summary>
/// Validation issue attached to an App Store Connect build upload.
/// </summary>
public sealed class AppStoreConnectBuildUploadIssue
{
    /// <summary>Apple validation code.</summary>
    public string? Code { get; set; }

    /// <summary>Human-readable validation description.</summary>
    public string? Description { get; set; }
}

/// <summary>
/// App Store Connect subscription group summary.
/// </summary>
public sealed class AppStoreConnectSubscriptionGroupInfo
{
    /// <summary>Subscription group id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Reference name configured in App Store Connect.</summary>
    public string? ReferenceName { get; set; }
}

/// <summary>
/// App Store Connect auto-renewable subscription summary.
/// </summary>
public sealed class AppStoreConnectSubscriptionInfo
{
    /// <summary>Subscription id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Product id used by StoreKit.</summary>
    public string? ProductId { get; set; }

    /// <summary>Reference name configured in App Store Connect.</summary>
    public string? Name { get; set; }

    /// <summary>Subscription state returned by App Store Connect.</summary>
    public string? State { get; set; }

    /// <summary>Subscription period returned by App Store Connect.</summary>
    public string? SubscriptionPeriod { get; set; }

    /// <summary>Family sharing flag when available.</summary>
    public bool? FamilySharable { get; set; }

    /// <summary>Subscription group id that owns the product.</summary>
    public string? SubscriptionGroupId { get; set; }

    /// <summary>Subscription group reference name when available.</summary>
    public string? SubscriptionGroupReferenceName { get; set; }
}

/// <summary>
/// Local-vs-App Store Connect release drift report.
/// </summary>
public sealed class AppleAppReleaseDriftReport
{
    /// <summary>Local Xcode version information.</summary>
    public XcodeProjectVersionInfo Local { get; set; } = new();

    /// <summary>Matched App Store Connect app when found.</summary>
    public AppStoreConnectAppInfo? App { get; set; }

    /// <summary>Matched App Store Connect version when found.</summary>
    public AppStoreConnectVersionInfo? RemoteVersion { get; set; }

    /// <summary>Matched App Store Connect build when found.</summary>
    public AppStoreConnectBuildInfo? RemoteBuild { get; set; }

    /// <summary>Whether local version/build matches remote data.</summary>
    public bool IsMatch { get; set; }

    /// <summary>Drift or lookup messages.</summary>
    public string[] Messages { get; set; } = Array.Empty<string>();
}
