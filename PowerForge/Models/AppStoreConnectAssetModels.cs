namespace PowerForge;

/// <summary>
/// App Store Connect version localization summary.
/// </summary>
public sealed class AppStoreConnectVersionLocalizationInfo
{
    /// <summary>Localization id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Locale, for example en-US.</summary>
    public string? Locale { get; set; }

    /// <summary>Localized app name.</summary>
    public string? Name { get; set; }
}

/// <summary>
/// App Store Connect screenshot set summary.
/// </summary>
public sealed class AppStoreConnectScreenshotSetInfo
{
    /// <summary>Screenshot set id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Screenshot display type, for example APP_IPHONE_65.</summary>
    public string? ScreenshotDisplayType { get; set; }
}

/// <summary>
/// App Store Connect screenshot summary.
/// </summary>
public sealed class AppStoreConnectScreenshotInfo
{
    /// <summary>Screenshot id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Screenshot file name.</summary>
    public string? FileName { get; set; }

    /// <summary>Screenshot file size.</summary>
    public long? FileSize { get; set; }

    /// <summary>Source file checksum when available.</summary>
    public string? SourceFileChecksum { get; set; }

    /// <summary>Asset token returned by App Store Connect when available.</summary>
    public string? AssetToken { get; set; }

    /// <summary>Asset delivery state value when available.</summary>
    public string? AssetDeliveryState { get; set; }

    /// <summary>Upload operations returned by the asset reservation.</summary>
    public AppStoreConnectUploadOperation[] UploadOperations { get; set; } = Array.Empty<AppStoreConnectUploadOperation>();
}

/// <summary>
/// App Store Connect asset upload operation.
/// </summary>
public sealed class AppStoreConnectUploadOperation
{
    /// <summary>HTTP method for this upload part.</summary>
    public string Method { get; set; } = "PUT";

    /// <summary>Upload URL.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Byte offset in the source file.</summary>
    public long Offset { get; set; }

    /// <summary>Byte length to upload.</summary>
    public long Length { get; set; }

    /// <summary>Headers required for the upload operation.</summary>
    public Dictionary<string, string> RequestHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Result of uploading and committing an App Store Connect screenshot.
/// </summary>
public sealed class AppStoreConnectScreenshotUploadResult
{
    /// <summary>Screenshot returned by the reservation request.</summary>
    public AppStoreConnectScreenshotInfo Screenshot { get; set; } = new();

    /// <summary>Source file path.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Checksum committed to App Store Connect.</summary>
    public string SourceFileChecksum { get; set; } = string.Empty;

    /// <summary>Number of upload operations completed.</summary>
    public int UploadOperationCount { get; set; }
}
