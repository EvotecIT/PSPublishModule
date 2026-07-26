namespace PowerForge;

/// <summary>
/// Request to create an Apple app archive with xcodebuild.
/// </summary>
public sealed class AppleAppArchiveRequest
{
    /// <summary>Path to the Xcode project or workspace.</summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>When true, ProjectPath points to a workspace instead of a project.</summary>
    public bool IsWorkspace { get; set; }

    /// <summary>Xcode scheme to archive.</summary>
    public string Scheme { get; set; } = string.Empty;

    /// <summary>Build configuration, typically Release.</summary>
    public string Configuration { get; set; } = "Release";

    /// <summary>Apple platform used to resolve the generic archive destination.</summary>
    public ApplePlatform Platform { get; set; } = ApplePlatform.iOS;

    /// <summary>Optional archive destination variant used with the selected platform.</summary>
    public AppleArchiveVariant ArchiveVariant { get; set; } = AppleArchiveVariant.Default;

    /// <summary>Explicit xcodebuild destination. When omitted, a generic destination is derived from Platform.</summary>
    public string? Destination { get; set; }

    /// <summary>Output .xcarchive path. When omitted, ArchiveRoot and Scheme are used.</summary>
    public string? ArchivePath { get; set; }

    /// <summary>Directory used for generated archive paths.</summary>
    public string? ArchiveRoot { get; set; }

    /// <summary>xcodebuild executable name or path.</summary>
    public string XcodeBuildExecutable { get; set; } = "xcodebuild";

    /// <summary>Allows Xcode to create or update signing assets during archive.</summary>
    public bool AllowProvisioningUpdates { get; set; } = true;

    /// <summary>Path to an App Store Connect API private key used by xcodebuild archive authentication.</summary>
    public string? AppStoreConnectApiKeyPath { get; set; }

    /// <summary>App Store Connect API key identifier used by xcodebuild archive authentication.</summary>
    public string? AppStoreConnectApiKeyId { get; set; }

    /// <summary>App Store Connect API issuer identifier used by xcodebuild archive authentication.</summary>
    public string? AppStoreConnectApiIssuerId { get; set; }

    /// <summary>Additional structured arguments appended to the archive command.</summary>
    public string[] AdditionalArguments { get; set; } = Array.Empty<string>();

    /// <summary>Maximum archive runtime.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>
/// Result of creating an Apple app archive.
/// </summary>
public sealed class AppleAppArchiveResult
{
    /// <summary>Resolved archive path.</summary>
    public string ArchivePath { get; set; } = string.Empty;

    /// <summary>Resolved xcodebuild destination.</summary>
    public string Destination { get; set; } = string.Empty;

    /// <summary>xcodebuild process result.</summary>
    public ProcessRunResult ProcessResult { get; set; } = new(0, string.Empty, string.Empty, "xcodebuild", TimeSpan.Zero, false);

    /// <summary>True when xcodebuild completed successfully.</summary>
    public bool Succeeded => ProcessResult.Succeeded;
}

/// <summary>
/// Request to upload an Apple app archive to App Store Connect.
/// </summary>
public sealed class AppleAppArchiveUploadRequest
{
    /// <summary>Path to the .xcarchive to upload.</summary>
    public string ArchivePath { get; set; } = string.Empty;

    /// <summary>Temporary export path used by xcodebuild.</summary>
    public string? ExportPath { get; set; }

    /// <summary>Path to write the generated export options plist.</summary>
    public string? ExportOptionsPlistPath { get; set; }

    /// <summary>Apple developer team identifier.</summary>
    public string? TeamId { get; set; }

    /// <summary>Export destination. App Store Connect upload uses upload.</summary>
    public string Destination { get; set; } = "upload";

    /// <summary>Export method. App Store Connect upload uses app-store-connect.</summary>
    public string Method { get; set; } = "app-store-connect";

    /// <summary>Signing style passed to xcodebuild exportArchive.</summary>
    public string SigningStyle { get; set; } = "automatic";

    /// <summary>Controls whether App Store Connect manages app version and build numbers during upload.</summary>
    public bool ManageAppVersionAndBuildNumber { get; set; }

    /// <summary>Controls whether xcodebuild may update provisioning profiles during export/upload.</summary>
    public bool AllowProvisioningUpdates { get; set; } = true;

    /// <summary>Controls whether debug symbols are uploaded.</summary>
    public bool UploadSymbols { get; set; } = true;

    /// <summary>Controls whether xcodebuild generates App Store information.</summary>
    public bool GenerateAppStoreInformation { get; set; } = true;

    /// <summary>Path to an App Store Connect API private key used by xcodebuild upload authentication.</summary>
    public string? AppStoreConnectApiKeyPath { get; set; }

    /// <summary>App Store Connect API key identifier used by xcodebuild upload authentication.</summary>
    public string? AppStoreConnectApiKeyId { get; set; }

    /// <summary>App Store Connect API issuer identifier used by xcodebuild upload authentication.</summary>
    public string? AppStoreConnectApiIssuerId { get; set; }

    /// <summary>xcodebuild executable name or path.</summary>
    public string XcodeBuildExecutable { get; set; } = "xcodebuild";

    /// <summary>Additional structured arguments appended to the export command.</summary>
    public string[] AdditionalArguments { get; set; } = Array.Empty<string>();

    /// <summary>Maximum upload runtime.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>
/// Result of uploading an Apple app archive.
/// </summary>
public sealed class AppleAppArchiveUploadResult
{
    /// <summary>Archive path supplied to xcodebuild.</summary>
    public string ArchivePath { get; set; } = string.Empty;

    /// <summary>Export path supplied to xcodebuild.</summary>
    public string ExportPath { get; set; } = string.Empty;

    /// <summary>Generated export options plist path.</summary>
    public string ExportOptionsPlistPath { get; set; } = string.Empty;

    /// <summary>Xcode distribution log bundle associated with this upload, when reported.</summary>
    public string? DistributionLogPath { get; set; }

    /// <summary>Build-upload id accepted by App Store Connect, when reported by Xcode delivery.</summary>
    public string? BuildUploadId { get; set; }

    /// <summary>xcodebuild process result.</summary>
    public ProcessRunResult ProcessResult { get; set; } = new(0, string.Empty, string.Empty, "xcodebuild", TimeSpan.Zero, false);

    /// <summary>True when xcodebuild completed successfully.</summary>
    public bool Succeeded => ProcessResult.Succeeded;
}
