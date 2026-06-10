namespace PowerForge;

/// <summary>
/// Describes local Apple app release preparation performed by the pipeline.
/// </summary>
public sealed class AppleAppReleasePreparationResult
{
    /// <summary>Friendly app name used in configuration.</summary>
    public string? Name { get; set; }

    /// <summary>Bundle identifier for the app target.</summary>
    public string? BundleId { get; set; }

    /// <summary>Apple platform for the app target.</summary>
    public ApplePlatform Platform { get; set; }

    /// <summary>Xcode scheme name when configured.</summary>
    public string? Scheme { get; set; }

    /// <summary>App Store Connect app id when configured.</summary>
    public string? AppStoreConnectAppId { get; set; }

    /// <summary>Build number policy used for this preparation step.</summary>
    public AppleBuildNumberPolicy BuildNumberPolicy { get; set; }

    /// <summary>Resolved marketing version used for the update.</summary>
    public string MarketingVersion { get; set; } = string.Empty;

    /// <summary>Resolved build number used for the update, if any.</summary>
    public string? BuildNumber { get; set; }

    /// <summary>Xcode project version update result.</summary>
    public XcodeProjectVersionUpdateResult XcodeProjectVersionResult { get; set; } = new();
}
