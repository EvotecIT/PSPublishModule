namespace PowerForge;

/// <summary>
/// Configuration for syncing App Store Connect screenshots from local folders.
/// </summary>
public sealed class AppStoreConnectScreenshotSyncSpec
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

    /// <summary>Screenshot set folder mappings.</summary>
    public AppStoreConnectScreenshotSetSyncSpec[] ScreenshotSets { get; set; } = Array.Empty<AppStoreConnectScreenshotSetSyncSpec>();

    /// <summary>Optional local screenshot quality gates applied before upload.</summary>
    public AppStoreConnectScreenshotQualitySpec Quality { get; set; } = new();
}

/// <summary>
/// Local folder mapping for one App Store Connect screenshot display type.
/// </summary>
public sealed class AppStoreConnectScreenshotSetSyncSpec
{
    /// <summary>Screenshot display type, for example APP_IPHONE_65.</summary>
    public string ScreenshotDisplayType { get; set; } = string.Empty;

    /// <summary>Local folder containing screenshots.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>File search pattern.</summary>
    public string Filter { get; set; } = "*.png";

    /// <summary>Maximum screenshots to upload from this folder.</summary>
    public int MaxCount { get; set; } = 10;

    /// <summary>Optional allowed PNG dimensions in WIDTHxHEIGHT form.</summary>
    public string[] AllowedDimensions { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Configurable, deterministic screenshot quality checks.
/// </summary>
public sealed class AppStoreConnectScreenshotQualitySpec
{
    /// <summary>Enables screenshot quality validation.</summary>
    public bool Enabled { get; set; }

    /// <summary>Rejects byte-identical images inside one screenshot set.</summary>
    public bool RejectDuplicates { get; set; } = true;

    /// <summary>Requires every PNG in a screenshot set to use the same dimensions.</summary>
    public bool RequireConsistentDimensions { get; set; } = true;

    /// <summary>Minimum accepted screenshot file size.</summary>
    public long MinimumFileBytes { get; set; } = 4096;

    /// <summary>
    /// Minimum compressed kilobytes per megapixel. This catches near-empty or blank captures
    /// without depending on platform image frameworks. Set to zero to disable the heuristic.
    /// </summary>
    public double MinimumKilobytesPerMegapixel { get; set; } = 12;
}

/// <summary>
/// Request to sync App Store Connect screenshots from local folders.
/// </summary>
public sealed class AppStoreConnectScreenshotSyncRequest
{
    /// <summary>Sync configuration.</summary>
    public AppStoreConnectScreenshotSyncSpec Spec { get; set; } = new();

    /// <summary>When true, existing screenshots in each matched set are deleted before upload.</summary>
    public bool ReplaceExisting { get; set; }

    /// <summary>Base directory for resolving relative screenshot paths.</summary>
    public string BaseDirectory { get; set; } = Directory.GetCurrentDirectory();
}

/// <summary>
/// Result of syncing App Store Connect screenshots from local folders.
/// </summary>
public sealed class AppStoreConnectScreenshotSyncResult
{
    /// <summary>Matched App Store version.</summary>
    public AppStoreConnectVersionInfo Version { get; set; } = new();

    /// <summary>Matched App Store version localization.</summary>
    public AppStoreConnectVersionLocalizationInfo Localization { get; set; } = new();

    /// <summary>Per-set sync results.</summary>
    public AppStoreConnectScreenshotSetSyncResult[] ScreenshotSets { get; set; } = Array.Empty<AppStoreConnectScreenshotSetSyncResult>();
}

/// <summary>
/// Result of syncing one App Store Connect screenshot set.
/// </summary>
public sealed class AppStoreConnectScreenshotSetSyncResult
{
    /// <summary>Screenshot display type.</summary>
    public string ScreenshotDisplayType { get; set; } = string.Empty;

    /// <summary>Screenshot set id.</summary>
    public string ScreenshotSetId { get; set; } = string.Empty;

    /// <summary>Local folder used for upload.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Number of existing screenshots deleted.</summary>
    public int DeletedCount { get; set; }

    /// <summary>Uploaded screenshot results.</summary>
    public AppStoreConnectScreenshotUploadResult[] Uploaded { get; set; } = Array.Empty<AppStoreConnectScreenshotUploadResult>();
}
