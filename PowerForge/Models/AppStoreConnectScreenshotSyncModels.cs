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

    /// <summary>Bind this mapping to the release version selected by the unified Apple workflow.</summary>
    public bool UseReleaseVersion { get; set; }

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

    /// <summary>Require a reviewed approval manifest whose hashes match every screenshot selected for upload.</summary>
    public bool RequireApprovalManifest { get; set; }

    /// <summary>Approval manifest path relative to the screenshot configuration file.</summary>
    public string? ApprovalManifestPath { get; set; }
}

/// <summary>
/// Review receipt binding an approved screenshot set to its source, runtime, device, locale, and exact bytes.
/// </summary>
public sealed class AppStoreConnectScreenshotApprovalManifest
{
    /// <summary>Manifest schema version.</summary>
    public int SchemaVersion { get; set; } = 2;

    /// <summary>App Store Connect app id that may receive the reviewed screenshots.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>App Store Connect platform that may receive the reviewed screenshots.</summary>
    public ApplePlatform Platform { get; set; } = ApplePlatform.iOS;

    /// <summary>App marketing version represented by the screenshots.</summary>
    public string VersionString { get; set; } = string.Empty;

    /// <summary>Source commit used by the capture.</summary>
    public string SourceCommit { get; set; } = string.Empty;

    /// <summary>GitHub Actions run that produced the exact reviewed capture bytes.</summary>
    public string? CaptureRunId { get; set; }

    /// <summary>Repository that produced the exact reviewed capture bytes.</summary>
    public string? CaptureRepository { get; set; }

    /// <summary>Reusable workflow identity that produced the exact reviewed capture bytes.</summary>
    public string? CaptureWorkflowRef { get; set; }

    /// <summary>Xcode version used for capture.</summary>
    public string? XcodeVersion { get; set; }

    /// <summary>Simulator/device runtime used for capture.</summary>
    public string? Runtime { get; set; }

    /// <summary>Device or simulator model used for capture.</summary>
    public string? Device { get; set; }

    /// <summary>Locale used for capture.</summary>
    public string Locale { get; set; } = string.Empty;

    /// <summary>Appearance used for capture, such as light or dark.</summary>
    public string? Theme { get; set; }

    /// <summary>Stable capture scenario or route name.</summary>
    public string? Scenario { get; set; }

    /// <summary>UTC approval timestamp.</summary>
    public DateTimeOffset ApprovedAt { get; set; }

    /// <summary>Human reviewer or protected approval boundary that approved the exact images.</summary>
    public string ApprovedBy { get; set; } = string.Empty;

    /// <summary>Identity that initiated the approval workflow, when distinct from the reviewer.</summary>
    public string? InitiatedBy { get; set; }

    /// <summary>Durable URL or identifier for the external approval evidence.</summary>
    public string? ApprovalEvidence { get; set; }

    /// <summary>Exact approved screenshot entries.</summary>
    public AppStoreConnectScreenshotApprovalEntry[] Screenshots { get; set; } = Array.Empty<AppStoreConnectScreenshotApprovalEntry>();
}

/// <summary>Exact screenshot approved for App Store Connect upload.</summary>
public sealed class AppStoreConnectScreenshotApprovalEntry
{
    /// <summary>App Store Connect screenshot display type.</summary>
    public string ScreenshotDisplayType { get; set; } = string.Empty;

    /// <summary>File name or config-relative path.</summary>
    public string File { get; set; } = string.Empty;

    /// <summary>Upper- or lower-case SHA-256 digest of the exact file.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>PNG width.</summary>
    public int Width { get; set; }

    /// <summary>PNG height.</summary>
    public int Height { get; set; }

    /// <summary>Optional perceptual hash emitted by the capture pipeline for visual-diff tooling.</summary>
    public string? PerceptualHash { get; set; }
}

/// <summary>Request to bind reviewed screenshot files to an approval manifest.</summary>
public sealed class AppStoreConnectScreenshotApprovalRequest
{
    /// <summary>Screenshot sync configuration whose selected files were reviewed.</summary>
    public AppStoreConnectScreenshotSyncSpec Spec { get; set; } = new();

    /// <summary>
    /// Exact App Store Connect app id receiving the reviewed screenshots. Required when
    /// <see cref="Spec"/> intentionally leaves its reusable app id blank.
    /// </summary>
    public string? AppId { get; set; }

    /// <summary>Base directory used to resolve screenshot paths.</summary>
    public string BaseDirectory { get; set; } = Directory.GetCurrentDirectory();

    /// <summary>Reviewed capture root that must contain every selected screenshot.</summary>
    public string AllowedRoot { get; set; } = string.Empty;

    /// <summary>Marketing version represented by the captures.</summary>
    public string VersionString { get; set; } = string.Empty;

    /// <summary>Exact source commit used by capture automation.</summary>
    public string SourceCommit { get; set; } = string.Empty;

    /// <summary>GitHub Actions run that produced the reviewed capture bytes.</summary>
    public string? CaptureRunId { get; set; }

    /// <summary>Repository that produced the reviewed capture bytes.</summary>
    public string? CaptureRepository { get; set; }

    /// <summary>Reusable workflow identity that produced the reviewed capture bytes.</summary>
    public string? CaptureWorkflowRef { get; set; }

    /// <summary>Human reviewer or protected approval boundary.</summary>
    public string ApprovedBy { get; set; } = string.Empty;

    /// <summary>Identity that initiated the approval workflow, when distinct from the reviewer.</summary>
    public string? InitiatedBy { get; set; }

    /// <summary>Durable URL or identifier for the external approval evidence.</summary>
    public string? ApprovalEvidence { get; set; }

    /// <summary>Approval time. Defaults to the current UTC time.</summary>
    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>Xcode version used for capture.</summary>
    public string? XcodeVersion { get; set; }

    /// <summary>Simulator or device runtime used for capture.</summary>
    public string? Runtime { get; set; }

    /// <summary>Simulator or device model used for capture.</summary>
    public string? Device { get; set; }

    /// <summary>Appearance used for capture.</summary>
    public string? Theme { get; set; }

    /// <summary>Stable capture scenario or suite name.</summary>
    public string? Scenario { get; set; }
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

    /// <summary>Exact source commit whose reviewed screenshots may be uploaded.</summary>
    public string? ExpectedSourceCommit { get; set; }
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
