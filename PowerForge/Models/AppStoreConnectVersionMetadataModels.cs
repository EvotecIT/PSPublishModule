namespace PowerForge;

/// <summary>
/// Optional App Store Connect localized metadata values for an App Store version localization.
/// Null values are left unchanged; empty strings intentionally clear the corresponding field.
/// </summary>
public sealed class AppStoreConnectVersionLocalizationUpdate
{
    /// <summary>Localized App Store description.</summary>
    public string? Description { get; set; }

    /// <summary>Localized App Store keywords.</summary>
    public string? Keywords { get; set; }

    /// <summary>Localized marketing URL.</summary>
    public string? MarketingUrl { get; set; }

    /// <summary>Localized promotional text.</summary>
    public string? PromotionalText { get; set; }

    /// <summary>Localized support URL.</summary>
    public string? SupportUrl { get; set; }

    /// <summary>Localized release notes / what's new text.</summary>
    public string? WhatsNew { get; set; }
}

/// <summary>
/// JSON-friendly specification for syncing localized App Store version metadata.
/// </summary>
public sealed class AppStoreConnectVersionMetadataSpec
{
    /// <summary>App Store Connect app id.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>App Store version string.</summary>
    public string? VersionString { get; set; }

    /// <summary>Optional App Store version id. When provided, version lookup by string is skipped.</summary>
    public string? VersionId { get; set; }

    /// <summary>Apple platform for the App Store version.</summary>
    public ApplePlatform Platform { get; set; } = ApplePlatform.iOS;

    /// <summary>Localization locale, for example en-US.</summary>
    public string Locale { get; set; } = "en-US";

    /// <summary>Localized metadata to apply.</summary>
    public AppStoreConnectVersionLocalizationUpdate Metadata { get; set; } = new();
}

/// <summary>
/// Request to sync localized App Store version metadata.
/// </summary>
public sealed class AppStoreConnectVersionMetadataSyncRequest
{
    /// <summary>Metadata sync specification.</summary>
    public AppStoreConnectVersionMetadataSpec Spec { get; set; } = new();
}

/// <summary>
/// Result of syncing localized App Store version metadata.
/// </summary>
public sealed class AppStoreConnectVersionMetadataSyncResult
{
    /// <summary>Matched App Store version.</summary>
    public AppStoreConnectVersionInfo Version { get; set; } = new();

    /// <summary>Localization before the metadata update.</summary>
    public AppStoreConnectVersionLocalizationInfo Before { get; set; } = new();

    /// <summary>Localization after the metadata update.</summary>
    public AppStoreConnectVersionLocalizationInfo After { get; set; } = new();

    /// <summary>Updated field names.</summary>
    public string[] UpdatedFields { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Request to check whether an App Store Connect version is ready for submission.
/// </summary>
public sealed class AppStoreConnectReleaseReadinessRequest
{
    /// <summary>App Store Connect app id.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>App Store marketing version.</summary>
    public string VersionString { get; set; } = string.Empty;

    /// <summary>Build number expected to be selected for Distribution.</summary>
    public string? BuildNumber { get; set; }

    /// <summary>Apple platform for the App Store version and build.</summary>
    public ApplePlatform Platform { get; set; } = ApplePlatform.iOS;

    /// <summary>Localization locale to check.</summary>
    public string Locale { get; set; } = "en-US";

    /// <summary>Require the Distribution version to have the expected selected build.</summary>
    public bool RequireSelectedBuild { get; set; } = true;

    /// <summary>Require the matched build to be valid and not expired.</summary>
    public bool RequireValidBuild { get; set; } = true;

    /// <summary>Require localized App Store description.</summary>
    public bool RequireDescription { get; set; } = true;

    /// <summary>Require localized App Store keywords.</summary>
    public bool RequireKeywords { get; set; } = true;

    /// <summary>Require localized support URL.</summary>
    public bool RequireSupportUrl { get; set; } = true;

    /// <summary>Require localized marketing URL.</summary>
    public bool RequireMarketingUrl { get; set; }

    /// <summary>Require localized promotional text.</summary>
    public bool RequirePromotionalText { get; set; }

    /// <summary>Require localized what's new / release notes text.</summary>
    public bool RequireWhatsNew { get; set; }

    /// <summary>Require screenshots in the configured screenshot display types.</summary>
    public bool RequireScreenshots { get; set; } = true;

    /// <summary>Require every checked screenshot asset to have COMPLETE delivery state.</summary>
    public bool RequireCompleteScreenshots { get; set; } = true;

    /// <summary>Minimum screenshot count for each required display type.</summary>
    public int MinimumScreenshotsPerSet { get; set; } = 1;

    /// <summary>Screenshot display types that must be present for this platform version.</summary>
    public string[] RequiredScreenshotDisplayTypes { get; set; } = Array.Empty<string>();

    /// <summary>Optional screenshot spec used to derive required display types.</summary>
    public AppStoreConnectScreenshotSyncSpec? ScreenshotSpec { get; set; }
}

/// <summary>
/// Result of checking App Store Connect release readiness.
/// </summary>
public sealed class AppStoreConnectReleaseReadinessResult
{
    /// <summary>App Store Connect app id.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>Checked marketing version.</summary>
    public string VersionString { get; set; } = string.Empty;

    /// <summary>Checked build number.</summary>
    public string? BuildNumber { get; set; }

    /// <summary>Checked platform.</summary>
    public ApplePlatform Platform { get; set; } = ApplePlatform.iOS;

    /// <summary>Whether every required readiness check passed.</summary>
    public bool IsReady { get; set; }

    /// <summary>Matched App Store version when found.</summary>
    public AppStoreConnectVersionInfo? Version { get; set; }

    /// <summary>Matched build when requested and found.</summary>
    public AppStoreConnectBuildInfo? Build { get; set; }

    /// <summary>Selected build relationship id when available.</summary>
    public string? SelectedBuildId { get; set; }

    /// <summary>Matched localization when found.</summary>
    public AppStoreConnectVersionLocalizationInfo? Localization { get; set; }

    /// <summary>Screenshot set readiness details.</summary>
    public AppStoreConnectReleaseScreenshotSetReadiness[] ScreenshotSets { get; set; } = Array.Empty<AppStoreConnectReleaseScreenshotSetReadiness>();

    /// <summary>Individual checks that compose readiness.</summary>
    public AppStoreConnectReleaseReadinessCheck[] Checks { get; set; } = Array.Empty<AppStoreConnectReleaseReadinessCheck>();
}

/// <summary>
/// One release readiness check.
/// </summary>
public sealed class AppStoreConnectReleaseReadinessCheck
{
    /// <summary>Machine-readable check name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>True when the check passed.</summary>
    public bool Passed { get; set; }

    /// <summary>Human-readable check message.</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Screenshot set details included in release readiness results.
/// </summary>
public sealed class AppStoreConnectReleaseScreenshotSetReadiness
{
    /// <summary>Screenshot display type.</summary>
    public string ScreenshotDisplayType { get; set; } = string.Empty;

    /// <summary>Screenshot set id when found.</summary>
    public string? ScreenshotSetId { get; set; }

    /// <summary>Number of screenshots currently in the set.</summary>
    public int Count { get; set; }

    /// <summary>Distinct asset delivery states reported for screenshots.</summary>
    public string[] AssetDeliveryStates { get; set; } = Array.Empty<string>();

    /// <summary>Screenshot file names currently in the set.</summary>
    public string[] FileNames { get; set; } = Array.Empty<string>();
}
