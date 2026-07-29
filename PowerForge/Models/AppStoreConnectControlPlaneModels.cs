namespace PowerForge;

/// <summary>
/// Compact App Store Connect control-plane inventory used by deep release diagnostics.
/// </summary>
public sealed class AppStoreConnectControlPlaneState
{
    /// <summary>App Store Connect app identifier.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>App Store Connect version identifier, when a version was selected.</summary>
    public string? VersionId { get; set; }

    /// <summary>Whether all required review contact and conditional sign-in details are complete for the selected version.</summary>
    public bool ReviewDetailsConfigured { get; set; }

    /// <summary>Whether an App Review Details resource exists for the selected version.</summary>
    public bool ReviewDetailsExist { get; set; }

    /// <summary>Whether all four required review contact fields are populated.</summary>
    public bool ReviewContactConfigured { get; set; }

    /// <summary>Whether the API explicitly declares if a demo account is required.</summary>
    public bool ReviewDemoAccountRequirementDeclared { get; set; }

    /// <summary>Whether App Review requires a demo account, when declared.</summary>
    public bool? ReviewDemoAccountRequired { get; set; }

    /// <summary>Whether required demo credentials are populated; true when a demo account is explicitly not required.</summary>
    public bool ReviewDemoCredentialsConfigured { get; set; }

    /// <summary>Whether an age-rating declaration exists for an app information record.</summary>
    public bool AgeRatingDeclared { get; set; }

    /// <summary>Number of encryption declarations registered for the app.</summary>
    public int EncryptionDeclarationCount { get; set; }

    /// <summary>Number of accessibility declarations registered for the app.</summary>
    public int AccessibilityDeclarationCount { get; set; }

    /// <summary>Whether an app price schedule exists.</summary>
    public bool PriceScheduleConfigured { get; set; }

    /// <summary>Whether app territory availability exists.</summary>
    public bool AvailabilityConfigured { get; set; }

    /// <summary>Phased-release state for the selected version, when configured.</summary>
    public string? PhasedReleaseState { get; set; }

    /// <summary>Number of in-app purchases discovered for the app.</summary>
    public int InAppPurchaseCount { get; set; }

    /// <summary>Number of auto-renewable subscriptions discovered for the app.</summary>
    public int SubscriptionCount { get; set; }

    /// <summary>Number of enabled App Store Connect webhooks configured for the app.</summary>
    public int WebhookCount { get; set; }

    /// <summary>Number of recent TestFlight crash-feedback records visible to the API.</summary>
    public int BetaCrashFeedbackCount { get; set; }

    /// <summary>Number of recent TestFlight screenshot-feedback records visible to the API.</summary>
    public int BetaScreenshotFeedbackCount { get; set; }

    /// <summary>Newest compact TestFlight crash-feedback items, without tester email addresses or crash-log bodies.</summary>
    public AppStoreConnectBetaFeedbackItem[] RecentCrashFeedback { get; set; } = Array.Empty<AppStoreConnectBetaFeedbackItem>();

    /// <summary>Newest compact TestFlight screenshot-feedback items, without image bodies or tester email addresses.</summary>
    public AppStoreConnectBetaFeedbackItem[] RecentScreenshotFeedback { get; set; } = Array.Empty<AppStoreConnectBetaFeedbackItem>();

    /// <summary>Number of customer reviews visible to the API.</summary>
    public int CustomerReviewCount { get; set; }
}

/// <summary>Privacy-conscious compact TestFlight feedback summary for proactive release triage.</summary>
public sealed class AppStoreConnectBetaFeedbackItem
{
    /// <summary>Opaque App Store Connect feedback identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Feedback kind: crash or screenshot.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Feedback creation time.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Tester comment, compacted for a release receipt.</summary>
    public string? Comment { get; set; }

    /// <summary>Device model reported by TestFlight.</summary>
    public string? DeviceModel { get; set; }

    /// <summary>Operating system version reported by TestFlight.</summary>
    public string? OsVersion { get; set; }

    /// <summary>App platform reported by TestFlight.</summary>
    public string? AppPlatform { get; set; }

    /// <summary>Build bundle identifier reported by TestFlight.</summary>
    public string? BuildBundleId { get; set; }
}
