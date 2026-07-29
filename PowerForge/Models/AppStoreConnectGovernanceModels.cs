#pragma warning disable CS1591 // Schema DTO property names are the public JSON contract.

using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>Declarative, human-approved App Store commercial and compliance state.</summary>
public sealed class AppStoreConnectGovernanceSpec
{
    /// <summary>Optional JSON Schema hint used by editors.</summary>
    [JsonPropertyName("$schema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Schema { get; set; }

    /// <summary>Configuration schema version.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Exact App Store Connect app id.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>Optional app pricing schedule.</summary>
    public AppStoreConnectAppPricingSpec? Pricing { get; set; }

    /// <summary>Optional territory availability policy.</summary>
    public AppStoreConnectAppAvailabilitySpec? Availability { get; set; }

    /// <summary>Accessibility declarations keyed by Apple device family.</summary>
    public AppStoreConnectAccessibilityDeclarationSpec[] Accessibility { get; set; } = Array.Empty<AppStoreConnectAccessibilityDeclarationSpec>();

    /// <summary>Export-compliance declarations. PowerForge never invents these legal facts.</summary>
    public AppStoreConnectEncryptionDeclarationSpec[] EncryptionDeclarations { get; set; } = Array.Empty<AppStoreConnectEncryptionDeclarationSpec>();

    /// <summary>Auto-renewable subscription groups and products.</summary>
    public AppStoreConnectSubscriptionGroupSpec[] SubscriptionGroups { get; set; } = Array.Empty<AppStoreConnectSubscriptionGroupSpec>();
}

/// <summary>App price schedule expressed with App Store Connect price-point ids.</summary>
public sealed class AppStoreConnectAppPricingSpec
{
    /// <summary>ISO-style Apple territory resource id used as the base territory.</summary>
    public string BaseTerritoryId { get; set; } = string.Empty;

    /// <summary>Manual or scheduled prices. Price-point ids remain explicit commercial choices.</summary>
    public AppStoreConnectAppPriceSpec[] Prices { get; set; } = Array.Empty<AppStoreConnectAppPriceSpec>();
}

/// <summary>One app price in a territory and optional date range.</summary>
public sealed class AppStoreConnectAppPriceSpec
{
    /// <summary>App price-point resource id chosen in App Store Connect.</summary>
    public string AppPricePointId { get; set; } = string.Empty;

    /// <summary>Territory resource id represented by the price point.</summary>
    public string TerritoryId { get; set; } = string.Empty;

    /// <summary>Optional first sale date in yyyy-MM-dd form.</summary>
    public string? StartDate { get; set; }

    /// <summary>Optional final sale date in yyyy-MM-dd form.</summary>
    public string? EndDate { get; set; }
}

/// <summary>App-wide territory availability.</summary>
public sealed class AppStoreConnectAppAvailabilitySpec
{
    /// <summary>Whether Apple should make the app available in territories introduced later.</summary>
    [JsonRequired]
    public bool AvailableInNewTerritories { get; set; }

    /// <summary>Explicit territory availability entries.</summary>
    public AppStoreConnectTerritoryAvailabilitySpec[] Territories { get; set; } = Array.Empty<AppStoreConnectTerritoryAvailabilitySpec>();
}

/// <summary>Availability intent for one App Store territory.</summary>
public sealed class AppStoreConnectTerritoryAvailabilitySpec
{
    /// <summary>Territory resource id.</summary>
    public string TerritoryId { get; set; } = string.Empty;

    /// <summary>Whether the app is available in this territory.</summary>
    [JsonRequired]
    public bool Available { get; set; }

    /// <summary>Optional release date in yyyy-MM-dd form.</summary>
    public string? ReleaseDate { get; set; }

    /// <summary>Optional preorder flag.</summary>
    public bool? PreOrderEnabled { get; set; }
}

/// <summary>Accessibility facts for one Apple device family.</summary>
public sealed class AppStoreConnectAccessibilityDeclarationSpec
{
    /// <summary>Apple device family: IPHONE, IPAD, APPLE_TV, APPLE_WATCH, MAC, or VISION.</summary>
    public string DeviceFamily { get; set; } = string.Empty;

    /// <summary>Publish the declaration after applying its reviewed facts.</summary>
    public bool Publish { get; set; }

    public bool? SupportsAudioDescriptions { get; set; }
    public bool? SupportsCaptions { get; set; }
    public bool? SupportsDarkInterface { get; set; }
    public bool? SupportsDifferentiateWithoutColorAlone { get; set; }
    public bool? SupportsLargerText { get; set; }
    public bool? SupportsReducedMotion { get; set; }
    public bool? SupportsSufficientContrast { get; set; }
    public bool? SupportsVoiceControl { get; set; }
    public bool? SupportsVoiceover { get; set; }
}

/// <summary>Reviewed export-compliance declaration facts.</summary>
public sealed class AppStoreConnectEncryptionDeclarationSpec
{
    public string AppDescription { get; set; } = string.Empty;
    [JsonRequired]
    public bool ContainsProprietaryCryptography { get; set; }
    [JsonRequired]
    public bool ContainsThirdPartyCryptography { get; set; }
    [JsonRequired]
    public bool AvailableOnFrenchStore { get; set; }
}

/// <summary>Auto-renewable subscription group.</summary>
public sealed class AppStoreConnectSubscriptionGroupSpec
{
    /// <summary>Optional stable App Store Connect id, recommended when renaming an existing group.</summary>
    public string? Id { get; set; }

    /// <summary>Stable internal reference name used to match or create the group.</summary>
    public string ReferenceName { get; set; } = string.Empty;

    public AppStoreConnectSubscriptionGroupLocalizationSpec[] Localizations { get; set; } = Array.Empty<AppStoreConnectSubscriptionGroupLocalizationSpec>();
    public AppStoreConnectSubscriptionSpec[] Subscriptions { get; set; } = Array.Empty<AppStoreConnectSubscriptionSpec>();
}

/// <summary>Localized customer-facing subscription group name.</summary>
public sealed class AppStoreConnectSubscriptionGroupLocalizationSpec
{
    public string Locale { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? CustomAppName { get; set; }
}

/// <summary>One auto-renewable subscription product.</summary>
public sealed class AppStoreConnectSubscriptionSpec
{
    /// <summary>Stable StoreKit product identifier.</summary>
    public string ProductId { get; set; } = string.Empty;

    /// <summary>Internal App Store Connect reference name.</summary>
    public string Name { get; set; } = string.Empty;

    public bool? FamilySharable { get; set; }

    /// <summary>ONE_WEEK, ONE_MONTH, TWO_MONTHS, THREE_MONTHS, SIX_MONTHS, or ONE_YEAR.</summary>
    public string SubscriptionPeriod { get; set; } = string.Empty;

    public string? ReviewNote { get; set; }
    public int? GroupLevel { get; set; }
    public AppStoreConnectSubscriptionLocalizationSpec[] Localizations { get; set; } = Array.Empty<AppStoreConnectSubscriptionLocalizationSpec>();
    public AppStoreConnectSubscriptionPriceSpec[] Prices { get; set; } = Array.Empty<AppStoreConnectSubscriptionPriceSpec>();
    public AppStoreConnectSubscriptionIntroductoryOfferSpec[] IntroductoryOffers { get; set; } = Array.Empty<AppStoreConnectSubscriptionIntroductoryOfferSpec>();
    public AppStoreConnectSubscriptionAvailabilitySpec[] Availabilities { get; set; } = Array.Empty<AppStoreConnectSubscriptionAvailabilitySpec>();
}

/// <summary>Localized customer-facing subscription product metadata.</summary>
public sealed class AppStoreConnectSubscriptionLocalizationSpec
{
    public string Locale { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>One scheduled subscription price.</summary>
public sealed class AppStoreConnectSubscriptionPriceSpec
{
    public string TerritoryId { get; set; } = string.Empty;
    public string SubscriptionPricePointId { get; set; } = string.Empty;
    public string? StartDate { get; set; }
    public bool? PreserveCurrentPrice { get; set; }

    /// <summary>MONTHLY or UPFRONT.</summary>
    public string? PlanType { get; set; }
}

/// <summary>One introductory offer for an auto-renewable subscription.</summary>
public sealed class AppStoreConnectSubscriptionIntroductoryOfferSpec
{
    /// <summary>THREE_DAYS, ONE_WEEK, TWO_WEEKS, ONE_MONTH, TWO_MONTHS, THREE_MONTHS, SIX_MONTHS, or ONE_YEAR.</summary>
    public string Duration { get; set; } = string.Empty;

    /// <summary>FREE_TRIAL, PAY_AS_YOU_GO, or PAY_UP_FRONT.</summary>
    public string OfferMode { get; set; } = string.Empty;

    [JsonRequired]
    public int NumberOfPeriods { get; set; } = 1;
    /// <summary>Explicit offer territories. Set this or TerritoriesFromPlanType, never both.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? TerritoryIds { get; set; }

    /// <summary>Reuse the reviewed territory ids from the subscription's MONTHLY or UPFRONT availability declaration.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TerritoriesFromPlanType { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }

    /// <summary>Required for paid offers and omitted for free trials.</summary>
    public string? SubscriptionPricePointId { get; set; }
}

/// <summary>Subscription plan availability by territory.</summary>
public sealed class AppStoreConnectSubscriptionAvailabilitySpec
{
    /// <summary>MONTHLY or UPFRONT.</summary>
    [JsonRequired]
    public string PlanType { get; set; } = "MONTHLY";

    [JsonRequired]
    public bool AvailableInNewTerritories { get; set; }

    [JsonRequired]
    public string[] TerritoryIds { get; set; } = Array.Empty<string>();
}

/// <summary>App pricing state returned by App Store Connect.</summary>
public sealed class AppStoreConnectAppPriceScheduleInfo
{
    public string Id { get; set; } = string.Empty;
    public string? BaseTerritoryId { get; set; }
    public AppStoreConnectAppPriceInfo[] Prices { get; set; } = Array.Empty<AppStoreConnectAppPriceInfo>();
}

public sealed class AppStoreConnectAppPriceInfo
{
    public string Id { get; set; } = string.Empty;
    public string? AppPricePointId { get; set; }
    public string? TerritoryId { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
}

public sealed class AppStoreConnectAppAvailabilityInfo
{
    public string Id { get; set; } = string.Empty;
    public bool? AvailableInNewTerritories { get; set; }
    public AppStoreConnectTerritoryAvailabilityInfo[] Territories { get; set; } = Array.Empty<AppStoreConnectTerritoryAvailabilityInfo>();
}

public sealed class AppStoreConnectTerritoryAvailabilityInfo
{
    public string Id { get; set; } = string.Empty;
    public string? TerritoryId { get; set; }
    public bool? Available { get; set; }
    public string? ReleaseDate { get; set; }
    public bool? PreOrderEnabled { get; set; }
}

public sealed class AppStoreConnectAccessibilityDeclarationInfo
{
    public string Id { get; set; } = string.Empty;
    public string? DeviceFamily { get; set; }
    public string? State { get; set; }
    public bool? SupportsAudioDescriptions { get; set; }
    public bool? SupportsCaptions { get; set; }
    public bool? SupportsDarkInterface { get; set; }
    public bool? SupportsDifferentiateWithoutColorAlone { get; set; }
    public bool? SupportsLargerText { get; set; }
    public bool? SupportsReducedMotion { get; set; }
    public bool? SupportsSufficientContrast { get; set; }
    public bool? SupportsVoiceControl { get; set; }
    public bool? SupportsVoiceover { get; set; }
}

public sealed class AppStoreConnectEncryptionDeclarationInfo
{
    public string Id { get; set; } = string.Empty;
    public string? AppDescription { get; set; }
    public bool? Exempt { get; set; }
    public bool? ContainsProprietaryCryptography { get; set; }
    public bool? ContainsThirdPartyCryptography { get; set; }
    public bool? AvailableOnFrenchStore { get; set; }
    public string? State { get; set; }
}

public sealed class AppStoreConnectSubscriptionLocalizationInfo
{
    public string Id { get; set; } = string.Empty;
    public string? Locale { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
}

public sealed class AppStoreConnectSubscriptionGroupLocalizationInfo
{
    public string Id { get; set; } = string.Empty;
    public string? Locale { get; set; }
    public string? Name { get; set; }
    public string? CustomAppName { get; set; }
}

public sealed class AppStoreConnectSubscriptionPriceInfo
{
    public string Id { get; set; } = string.Empty;
    public string? TerritoryId { get; set; }
    public string? SubscriptionPricePointId { get; set; }
    public string? StartDate { get; set; }
    public bool? Preserved { get; set; }
    public string? PlanType { get; set; }
}

public sealed class AppStoreConnectSubscriptionAvailabilityInfo
{
    public string Id { get; set; } = string.Empty;
    public string? PlanType { get; set; }
    public bool? AvailableInNewTerritories { get; set; }
    public string[] TerritoryIds { get; set; } = Array.Empty<string>();
}

/// <summary>Mutation kind emitted by a governance plan.</summary>
public enum AppStoreConnectGovernanceChangeAction
{
    Create,
    Update,
    Publish,
    Blocked
}

/// <summary>One compact, non-secret difference between declared and observed state.</summary>
public sealed class AppStoreConnectGovernanceChange
{
    public string Section { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string? ResourceId { get; set; }
    public string? ParentId { get; set; }
    public AppStoreConnectGovernanceChangeAction Action { get; set; }
    public string Summary { get; set; } = string.Empty;
}

/// <summary>Validation finding for a governance declaration.</summary>
public sealed class AppStoreConnectGovernanceFinding
{
    public string Code { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsError { get; set; }
}

/// <summary>Plan and drift receipt produced without changing App Store Connect.</summary>
public sealed class AppStoreConnectGovernancePlan
{
    public string AppId { get; set; } = string.Empty;
    /// <summary>SHA-256 of the exact desired governance declaration represented by this plan.</summary>
    public string SpecSha256 { get; set; } = string.Empty;
    public DateTimeOffset CheckedAtUtc { get; set; }
    public AppStoreConnectGovernanceChange[] Changes { get; set; } = Array.Empty<AppStoreConnectGovernanceChange>();
    public AppStoreConnectGovernanceFinding[] Findings { get; set; } = Array.Empty<AppStoreConnectGovernanceFinding>();
    public int DriftCount => Changes.Count(change => change.Action != AppStoreConnectGovernanceChangeAction.Blocked);
    public int BlockedCount => Changes.Count(change => change.Action == AppStoreConnectGovernanceChangeAction.Blocked);
    public bool IsConverged => DriftCount == 0 && BlockedCount == 0 && Findings.All(finding => !finding.IsError);
    public bool CanApply => Findings.All(finding => !finding.IsError) && BlockedCount == 0;
}

/// <summary>Explicit request to apply a governance declaration.</summary>
public sealed class AppStoreConnectGovernanceApplyRequest
{
    public AppStoreConnectGovernanceSpec Spec { get; set; } = new();
    public bool ConfirmApply { get; set; }
    /// <summary>Maximum reviewed change effects that may be applied, including effects combined into one Apple request.</summary>
    public int MaximumChanges { get; set; } = 500;

    /// <summary>
    /// Optional previously generated plan that must still match current Apple state before the first mutation.
    /// </summary>
    public AppStoreConnectGovernancePlan? ReviewedPlan { get; set; }
}

/// <summary>Compact receipt for an approved governance convergence run.</summary>
public sealed class AppStoreConnectGovernanceApplyResult
{
    public string AppId { get; set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }
    public bool Success { get; set; }
    public AppStoreConnectGovernanceChange[] AppliedChanges { get; set; } = Array.Empty<AppStoreConnectGovernanceChange>();
    public AppStoreConnectGovernancePlan FinalPlan { get; set; } = new();
    public string[] NextActions { get; set; } = Array.Empty<string>();
}
