namespace PowerForge;

/// <summary>
/// Request to prepare an App Store Connect distribution version for a processed build.
/// </summary>
public sealed class AppStoreConnectReleasePreparationRequest
{
    /// <summary>App Store Connect API credential. Required when the request is executed by the default release runner.</summary>
    public AppStoreConnectApiCredential? Credential { get; set; }

    /// <summary>App Store Connect app id.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>App Store marketing version, for example 1.0.1.</summary>
    public string VersionString { get; set; } = string.Empty;

    /// <summary>Uploaded build number to select for the version.</summary>
    public string BuildNumber { get; set; } = string.Empty;

    /// <summary>Apple platform for the App Store version and uploaded build.</summary>
    public ApplePlatform Platform { get; set; } = ApplePlatform.iOS;

    /// <summary>Create the App Store version when it does not already exist.</summary>
    public bool CreateVersion { get; set; } = true;

    /// <summary>Select the matched build for the App Store version.</summary>
    public bool SelectBuild { get; set; } = true;

    /// <summary>Require the matched build to be valid and not expired before selecting it.</summary>
    public bool RequireValidBuild { get; set; } = true;

    /// <summary>Optional screenshot sync configuration to run after the version exists.</summary>
    public AppStoreConnectScreenshotSyncSpec? ScreenshotSpec { get; set; }

    /// <summary>Deletes existing screenshots before uploading screenshots from the local spec.</summary>
    public bool ReplaceScreenshots { get; set; }

    /// <summary>Base directory for resolving relative screenshot paths.</summary>
    public string BaseDirectory { get; set; } = Directory.GetCurrentDirectory();
}

/// <summary>
/// Result of preparing an App Store Connect distribution version.
/// </summary>
public sealed class AppStoreConnectReleasePreparationResult
{
    /// <summary>App Store Connect app id.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>Prepared marketing version.</summary>
    public string VersionString { get; set; } = string.Empty;

    /// <summary>Prepared build number.</summary>
    public string BuildNumber { get; set; } = string.Empty;

    /// <summary>Prepared Apple platform.</summary>
    public ApplePlatform Platform { get; set; } = ApplePlatform.iOS;

    /// <summary>Matched or created App Store version.</summary>
    public AppStoreConnectVersionInfo Version { get; set; } = new();

    /// <summary>Matched App Store Connect build.</summary>
    public AppStoreConnectBuildInfo? Build { get; set; }

    /// <summary>Whether the App Store version was created by this run.</summary>
    public bool CreatedVersion { get; set; }

    /// <summary>Whether the requested build was selected by this run.</summary>
    public bool SelectedBuild { get; set; }

    /// <summary>Build id that was selected before this run, if any.</summary>
    public string? PreviousBuildId { get; set; }

    /// <summary>Screenshot sync result when screenshots were configured.</summary>
    public AppStoreConnectScreenshotSyncResult? Screenshots { get; set; }

    /// <summary>Preparation messages useful for release logs.</summary>
    public string[] Messages { get; set; } = Array.Empty<string>();
}
