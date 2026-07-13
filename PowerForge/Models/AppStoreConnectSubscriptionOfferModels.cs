namespace PowerForge;

/// <summary>
/// Supported App Store Connect subscription introductory-offer durations.
/// </summary>
public enum AppStoreConnectSubscriptionOfferDuration
{
    /// <summary>Three days.</summary>
    ThreeDays,

    /// <summary>One week.</summary>
    OneWeek,

    /// <summary>Two weeks.</summary>
    TwoWeeks,

    /// <summary>One month.</summary>
    OneMonth,

    /// <summary>Two months.</summary>
    TwoMonths,

    /// <summary>Three months.</summary>
    ThreeMonths,

    /// <summary>Six months.</summary>
    SixMonths,

    /// <summary>One year.</summary>
    OneYear
}

/// <summary>
/// Supported App Store Connect subscription introductory-offer payment modes.
/// </summary>
public enum AppStoreConnectSubscriptionOfferMode
{
    /// <summary>A free trial.</summary>
    FreeTrial,

    /// <summary>A discounted price billed across multiple periods.</summary>
    PayAsYouGo,

    /// <summary>A discounted price paid once at the start.</summary>
    PayUpFront
}

/// <summary>
/// App Store Connect price point available for an auto-renewable subscription in a territory.
/// </summary>
public sealed class AppStoreConnectSubscriptionPricePointInfo
{
    /// <summary>App Store Connect resource id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Customer-facing price returned by App Store Connect.</summary>
    public string? CustomerPrice { get; set; }

    /// <summary>Developer proceeds returned by App Store Connect.</summary>
    public string? Proceeds { get; set; }

    /// <summary>Developer proceeds after the first year, when supplied by App Store Connect.</summary>
    public string? ProceedsYear2 { get; set; }

    /// <summary>Territory resource id for the available price point.</summary>
    public string? TerritoryId { get; set; }
}

/// <summary>
/// App Store Connect subscription introductory-offer summary.
/// </summary>
public sealed class AppStoreConnectSubscriptionIntroductoryOfferInfo
{
    /// <summary>App Store Connect resource id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Offer duration returned by App Store Connect.</summary>
    public string? Duration { get; set; }

    /// <summary>Offer payment mode returned by App Store Connect.</summary>
    public string? OfferMode { get; set; }

    /// <summary>Number of offer periods.</summary>
    public int? NumberOfPeriods { get; set; }

    /// <summary>Optional offer start date.</summary>
    public string? StartDate { get; set; }

    /// <summary>Optional offer end date.</summary>
    public string? EndDate { get; set; }

    /// <summary>Subscription resource id related to the offer.</summary>
    public string? SubscriptionId { get; set; }

    /// <summary>Optional subscription price point resource id.</summary>
    public string? SubscriptionPricePointId { get; set; }

    /// <summary>Territory resource id for the offer.</summary>
    public string? TerritoryId { get; set; }
}
