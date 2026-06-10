namespace PowerForge;

/// <summary>
/// Local validation result for an App Store Connect screenshot sync configuration.
/// </summary>
public sealed class AppStoreConnectScreenshotSyncValidationResult
{
    /// <summary>Path to the validated configuration file.</summary>
    public string ConfigPath { get; set; } = string.Empty;

    /// <summary>Whether the configuration is valid for local sync preflight.</summary>
    public bool IsValid { get; set; }

    /// <summary>Validation messages.</summary>
    public string[] Messages { get; set; } = Array.Empty<string>();

    /// <summary>Per-set validation results.</summary>
    public AppStoreConnectScreenshotSetSyncValidationResult[] ScreenshotSets { get; set; } = Array.Empty<AppStoreConnectScreenshotSetSyncValidationResult>();
}

/// <summary>
/// Local validation result for one screenshot set mapping.
/// </summary>
public sealed class AppStoreConnectScreenshotSetSyncValidationResult
{
    /// <summary>Screenshot display type.</summary>
    public string ScreenshotDisplayType { get; set; } = string.Empty;

    /// <summary>Resolved local folder path.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Search filter.</summary>
    public string Filter { get; set; } = "*.png";

    /// <summary>Number of matching screenshot files.</summary>
    public int FileCount { get; set; }

    /// <summary>Files that would be uploaded.</summary>
    public string[] Files { get; set; } = Array.Empty<string>();

    /// <summary>Whether this set mapping is locally valid.</summary>
    public bool IsValid { get; set; }

    /// <summary>Validation messages for this set.</summary>
    public string[] Messages { get; set; } = Array.Empty<string>();
}
