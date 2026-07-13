using System.Globalization;
using System.Text.Json;

namespace PowerForge;

public sealed partial class AppStoreConnectClient
{
    /// <summary>
    /// Lists prices configured for an auto-renewable subscription by territory.
    /// </summary>
    public Task<AppStoreConnectSubscriptionPriceInfo[]> GetSubscriptionPricesAsync(
        string subscriptionId,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId))
            throw new ArgumentException("Subscription id is required.", nameof(subscriptionId));

        var query = new Dictionary<string, string?>
        {
            ["include"] = "territory,subscriptionPricePoint",
            ["limit"] = ClampLimit(limit).ToString(CultureInfo.InvariantCulture)
        };

        return GetArrayAsync(
            $"subscriptions/{Uri.EscapeDataString(subscriptionId.Trim())}/prices" + BuildQuery(query),
            ParseSubscriptionPrice,
            cancellationToken);
    }

    /// <summary>
    /// Lists introductory offers for an auto-renewable subscription.
    /// </summary>
    public Task<AppStoreConnectSubscriptionIntroductoryOfferInfo[]> GetSubscriptionIntroductoryOffersAsync(
        string subscriptionId,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId))
            throw new ArgumentException("Subscription id is required.", nameof(subscriptionId));

        var query = new Dictionary<string, string?>
        {
            ["include"] = "subscription,territory,subscriptionPricePoint",
            ["limit"] = ClampLimit(limit).ToString(CultureInfo.InvariantCulture)
        };

        return GetArrayAsync(
            $"subscriptions/{Uri.EscapeDataString(subscriptionId.Trim())}/introductoryOffers" + BuildQuery(query),
            ParseSubscriptionIntroductoryOffer,
            cancellationToken);
    }

    /// <summary>
    /// Creates an introductory offer for an auto-renewable subscription.
    /// </summary>
    public Task<AppStoreConnectSubscriptionIntroductoryOfferInfo> CreateSubscriptionIntroductoryOfferAsync(
        string subscriptionId,
        AppStoreConnectSubscriptionOfferDuration duration,
        AppStoreConnectSubscriptionOfferMode offerMode,
        string territoryId,
        int numberOfPeriods = 1,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? subscriptionPricePointId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId))
            throw new ArgumentException("Subscription id is required.", nameof(subscriptionId));
        if (numberOfPeriods < 1)
            throw new ArgumentOutOfRangeException(nameof(numberOfPeriods), "Number of periods must be at least one.");
        if (string.IsNullOrWhiteSpace(territoryId))
            throw new ArgumentException("Territory id is required.", nameof(territoryId));
        if (startDate.HasValue && endDate.HasValue && startDate.Value.Date > endDate.Value.Date)
            throw new ArgumentException("Offer end date must be on or after the start date.", nameof(endDate));
        if (offerMode != AppStoreConnectSubscriptionOfferMode.FreeTrial &&
            string.IsNullOrWhiteSpace(subscriptionPricePointId))
            throw new ArgumentException("A subscription price point id is required for paid introductory offers.", nameof(subscriptionPricePointId));

        var attributes = new Dictionary<string, object?>
        {
            ["duration"] = ToAppStoreConnectOfferDuration(duration),
            ["offerMode"] = ToAppStoreConnectOfferMode(offerMode),
            ["numberOfPeriods"] = numberOfPeriods
        };
        if (startDate.HasValue)
            attributes["startDate"] = startDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (endDate.HasValue)
            attributes["endDate"] = endDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var relationships = new Dictionary<string, object>
        {
            ["subscription"] = new
            {
                data = new { type = "subscriptions", id = subscriptionId.Trim() }
            }
        };
        if (!string.IsNullOrWhiteSpace(subscriptionPricePointId))
        {
            relationships["subscriptionPricePoint"] = new
            {
                data = new { type = "subscriptionPricePoints", id = subscriptionPricePointId!.Trim() }
            };
        }
        relationships["territory"] = new
        {
            data = new { type = "territories", id = territoryId.Trim() }
        };

        var body = new
        {
            data = new
            {
                type = "subscriptionIntroductoryOffers",
                attributes,
                relationships
            }
        };

        return PostSingleAsync(
            "subscriptionIntroductoryOffers",
            body,
            ParseSubscriptionIntroductoryOffer,
            cancellationToken);
    }

    private static AppStoreConnectSubscriptionIntroductoryOfferInfo ParseSubscriptionIntroductoryOffer(JsonElement item)
    {
        var attrs = GetAttributes(item);
        return new AppStoreConnectSubscriptionIntroductoryOfferInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            Duration = GetString(attrs, "duration"),
            OfferMode = GetString(attrs, "offerMode"),
            NumberOfPeriods = GetInt32(attrs, "numberOfPeriods"),
            StartDate = GetString(attrs, "startDate"),
            EndDate = GetString(attrs, "endDate"),
            SubscriptionId = GetRelationshipDataId(item, "subscription"),
            SubscriptionPricePointId = GetRelationshipDataId(item, "subscriptionPricePoint"),
            TerritoryId = GetRelationshipDataId(item, "territory")
        };
    }

    private static AppStoreConnectSubscriptionPriceInfo ParseSubscriptionPrice(JsonElement item)
    {
        var attrs = GetAttributes(item);
        return new AppStoreConnectSubscriptionPriceInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            StartDate = GetString(attrs, "startDate"),
            Preserved = GetBool(attrs, "preserved"),
            PlanType = GetString(attrs, "planType"),
            TerritoryId = GetRelationshipDataId(item, "territory"),
            SubscriptionPricePointId = GetRelationshipDataId(item, "subscriptionPricePoint")
        };
    }

    private static string ToAppStoreConnectOfferDuration(AppStoreConnectSubscriptionOfferDuration duration)
    {
        return duration switch
        {
            AppStoreConnectSubscriptionOfferDuration.ThreeDays => "THREE_DAYS",
            AppStoreConnectSubscriptionOfferDuration.OneWeek => "ONE_WEEK",
            AppStoreConnectSubscriptionOfferDuration.TwoWeeks => "TWO_WEEKS",
            AppStoreConnectSubscriptionOfferDuration.OneMonth => "ONE_MONTH",
            AppStoreConnectSubscriptionOfferDuration.TwoMonths => "TWO_MONTHS",
            AppStoreConnectSubscriptionOfferDuration.ThreeMonths => "THREE_MONTHS",
            AppStoreConnectSubscriptionOfferDuration.SixMonths => "SIX_MONTHS",
            AppStoreConnectSubscriptionOfferDuration.OneYear => "ONE_YEAR",
            _ => throw new ArgumentOutOfRangeException(nameof(duration), duration, "Unsupported introductory-offer duration.")
        };
    }

    private static string ToAppStoreConnectOfferMode(AppStoreConnectSubscriptionOfferMode offerMode)
    {
        return offerMode switch
        {
            AppStoreConnectSubscriptionOfferMode.FreeTrial => "FREE_TRIAL",
            AppStoreConnectSubscriptionOfferMode.PayAsYouGo => "PAY_AS_YOU_GO",
            AppStoreConnectSubscriptionOfferMode.PayUpFront => "PAY_UP_FRONT",
            _ => throw new ArgumentOutOfRangeException(nameof(offerMode), offerMode, "Unsupported introductory-offer mode.")
        };
    }
}
