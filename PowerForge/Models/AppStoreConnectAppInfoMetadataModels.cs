namespace PowerForge;

/// <summary>
/// App-level App Store information that is shared across platform versions.
/// </summary>
public sealed class AppStoreConnectAppInformationInfo
{
    /// <summary>App information resource id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Current App Store state. This replaces Apple's deprecated appStoreState field.</summary>
    public string? State { get; set; }

    /// <summary>Deprecated App Store state retained for compatibility with older API responses.</summary>
    public string? AppStoreState { get; set; }
}

/// <summary>
/// Localized app-level information displayed on the App Store.
/// </summary>
public sealed class AppStoreConnectAppInfoLocalizationInfo
{
    /// <summary>App information localization resource id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Locale, for example en-US.</summary>
    public string? Locale { get; set; }

    /// <summary>Localized App Store name.</summary>
    public string? Name { get; set; }

    /// <summary>Localized App Store subtitle.</summary>
    public string? Subtitle { get; set; }

    /// <summary>Localized privacy policy URL.</summary>
    public string? PrivacyPolicyUrl { get; set; }

    /// <summary>Localized privacy choices URL.</summary>
    public string? PrivacyChoicesUrl { get; set; }

    /// <summary>Localized privacy policy text used by supported platforms such as tvOS.</summary>
    public string? PrivacyPolicyText { get; set; }
}

/// <summary>
/// Optional localized app-level metadata values.
/// Null values are left unchanged; empty strings intentionally clear the corresponding field.
/// </summary>
public sealed class AppStoreConnectAppInfoLocalizationUpdate
{
    /// <summary>Localized App Store name.</summary>
    public string? Name { get; set; }

    /// <summary>Localized App Store subtitle.</summary>
    public string? Subtitle { get; set; }

    /// <summary>Localized privacy policy URL.</summary>
    public string? PrivacyPolicyUrl { get; set; }

    /// <summary>Localized privacy choices URL.</summary>
    public string? PrivacyChoicesUrl { get; set; }

    /// <summary>Localized privacy policy text used by supported platforms such as tvOS.</summary>
    public string? PrivacyPolicyText { get; set; }
}

/// <summary>
/// JSON-friendly specification for syncing localized app-level App Store information.
/// </summary>
public sealed class AppStoreConnectAppInfoMetadataSpec
{
    /// <summary>App Store Connect app id.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>Optional App Information resource id. When omitted, the editable resource is selected.</summary>
    public string? AppInfoId { get; set; }

    /// <summary>Localization locale, for example en-US.</summary>
    public string Locale { get; set; } = "en-US";

    /// <summary>Localized app-level metadata to apply.</summary>
    public AppStoreConnectAppInfoLocalizationUpdate Metadata { get; set; } = new();
}

/// <summary>
/// Request to sync localized app-level App Store information.
/// </summary>
public sealed class AppStoreConnectAppInfoMetadataSyncRequest
{
    /// <summary>App information sync specification.</summary>
    public AppStoreConnectAppInfoMetadataSpec Spec { get; set; } = new();
}

/// <summary>
/// Result of syncing localized app-level App Store information.
/// </summary>
public sealed class AppStoreConnectAppInfoMetadataSyncResult
{
    /// <summary>Matched editable App Information resource.</summary>
    public AppStoreConnectAppInformationInfo AppInfo { get; set; } = new();

    /// <summary>Localization before the metadata update.</summary>
    public AppStoreConnectAppInfoLocalizationInfo Before { get; set; } = new();

    /// <summary>Localization after the metadata update.</summary>
    public AppStoreConnectAppInfoLocalizationInfo After { get; set; } = new();

    /// <summary>Updated field names.</summary>
    public string[] UpdatedFields { get; set; } = Array.Empty<string>();
}
