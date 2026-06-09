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
