using System.Text.Json;

namespace PowerForge;

public sealed partial class AppStoreConnectClient
{
    /// <summary>Creates an auto-renewable subscription group.</summary>
    public Task<AppStoreConnectSubscriptionGroupInfo> CreateSubscriptionGroupAsync(
        string appId,
        string referenceName,
        CancellationToken cancellationToken = default)
    {
        RequireValue(appId, nameof(appId), "App id");
        RequireValue(referenceName, nameof(referenceName), "Reference name");
        var body = new
        {
            data = new
            {
                type = "subscriptionGroups",
                attributes = new { referenceName = referenceName.Trim() },
                relationships = new
                {
                    app = new { data = new { type = "apps", id = appId.Trim() } }
                }
            }
        };
        return PostSingleAsync("subscriptionGroups", body, ParseSubscriptionGroup, cancellationToken);
    }

    /// <summary>Updates the internal reference name of a subscription group.</summary>
    public Task<AppStoreConnectSubscriptionGroupInfo> UpdateSubscriptionGroupAsync(
        string subscriptionGroupId,
        string referenceName,
        CancellationToken cancellationToken = default)
    {
        RequireValue(subscriptionGroupId, nameof(subscriptionGroupId), "Subscription group id");
        RequireValue(referenceName, nameof(referenceName), "Reference name");
        var body = new
        {
            data = new
            {
                type = "subscriptionGroups",
                id = subscriptionGroupId.Trim(),
                attributes = new { referenceName = referenceName.Trim() }
            }
        };
        return PatchSingleAsync(
            $"subscriptionGroups/{Uri.EscapeDataString(subscriptionGroupId.Trim())}",
            body,
            ParseSubscriptionGroup,
            cancellationToken);
    }

    /// <summary>Lists customer-facing localizations for a subscription group.</summary>
    public Task<AppStoreConnectSubscriptionGroupLocalizationInfo[]> GetSubscriptionGroupLocalizationsAsync(
        string subscriptionGroupId,
        CancellationToken cancellationToken = default)
    {
        RequireValue(subscriptionGroupId, nameof(subscriptionGroupId), "Subscription group id");
        return GetArrayAsync(
            $"subscriptionGroups/{Uri.EscapeDataString(subscriptionGroupId.Trim())}/subscriptionGroupLocalizations?limit=200",
            ParseSubscriptionGroupLocalization,
            cancellationToken);
    }

    /// <summary>Creates a customer-facing subscription group localization.</summary>
    public Task<AppStoreConnectSubscriptionGroupLocalizationInfo> CreateSubscriptionGroupLocalizationAsync(
        string subscriptionGroupId,
        AppStoreConnectSubscriptionGroupLocalizationSpec spec,
        CancellationToken cancellationToken = default)
    {
        RequireValue(subscriptionGroupId, nameof(subscriptionGroupId), "Subscription group id");
        ValidateGroupLocalization(spec);
        var body = new
        {
            data = new
            {
                type = "subscriptionGroupLocalizations",
                attributes = new
                {
                    name = spec.Name.Trim(),
                    customAppName = Normalize(spec.CustomAppName),
                    locale = spec.Locale.Trim()
                },
                relationships = new
                {
                    subscriptionGroup = new
                    {
                        data = new { type = "subscriptionGroups", id = subscriptionGroupId.Trim() }
                    }
                }
            }
        };
        return PostSingleAsync(
            "subscriptionGroupLocalizations",
            body,
            ParseSubscriptionGroupLocalization,
            cancellationToken);
    }

    /// <summary>Updates a customer-facing subscription group localization.</summary>
    public Task<AppStoreConnectSubscriptionGroupLocalizationInfo> UpdateSubscriptionGroupLocalizationAsync(
        string localizationId,
        AppStoreConnectSubscriptionGroupLocalizationSpec spec,
        CancellationToken cancellationToken = default)
    {
        RequireValue(localizationId, nameof(localizationId), "Localization id");
        ValidateGroupLocalization(spec);
        var body = new
        {
            data = new
            {
                type = "subscriptionGroupLocalizations",
                id = localizationId.Trim(),
                attributes = new
                {
                    name = spec.Name.Trim(),
                    customAppName = Normalize(spec.CustomAppName)
                }
            }
        };
        return PatchSingleAsync(
            $"subscriptionGroupLocalizations/{Uri.EscapeDataString(localizationId.Trim())}",
            body,
            ParseSubscriptionGroupLocalization,
            cancellationToken);
    }

    /// <summary>Creates an auto-renewable subscription product.</summary>
    public Task<AppStoreConnectSubscriptionInfo> CreateSubscriptionAsync(
        string subscriptionGroupId,
        AppStoreConnectSubscriptionSpec spec,
        CancellationToken cancellationToken = default)
    {
        RequireValue(subscriptionGroupId, nameof(subscriptionGroupId), "Subscription group id");
        ValidateSubscription(spec);
        var body = new
        {
            data = new
            {
                type = "subscriptions",
                attributes = BuildSubscriptionAttributes(spec, includeCreateOnlyAttributes: true),
                relationships = new
                {
                    group = new { data = new { type = "subscriptionGroups", id = subscriptionGroupId.Trim() } }
                }
            }
        };
        return PostSingleAsync("subscriptions", body, ParseSubscription, cancellationToken);
    }

    /// <summary>Updates mutable subscription product facts.</summary>
    public Task<AppStoreConnectSubscriptionInfo> UpdateSubscriptionAsync(
        string subscriptionId,
        AppStoreConnectSubscriptionSpec spec,
        CancellationToken cancellationToken = default)
    {
        RequireValue(subscriptionId, nameof(subscriptionId), "Subscription id");
        ValidateSubscription(spec);
        var body = new
        {
            data = new
            {
                type = "subscriptions",
                id = subscriptionId.Trim(),
                attributes = BuildSubscriptionAttributes(spec, includeCreateOnlyAttributes: false)
            }
        };
        return PatchSingleAsync(
            $"subscriptions/{Uri.EscapeDataString(subscriptionId.Trim())}",
            body,
            ParseSubscription,
            cancellationToken);
    }

    /// <summary>Lists customer-facing localizations for a subscription product.</summary>
    public Task<AppStoreConnectSubscriptionLocalizationInfo[]> GetSubscriptionLocalizationsAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        RequireValue(subscriptionId, nameof(subscriptionId), "Subscription id");
        return GetArrayAsync(
            $"subscriptions/{Uri.EscapeDataString(subscriptionId.Trim())}/subscriptionLocalizations?limit=200",
            ParseSubscriptionLocalization,
            cancellationToken);
    }

    /// <summary>Creates a customer-facing subscription localization.</summary>
    public Task<AppStoreConnectSubscriptionLocalizationInfo> CreateSubscriptionLocalizationAsync(
        string subscriptionId,
        AppStoreConnectSubscriptionLocalizationSpec spec,
        CancellationToken cancellationToken = default)
    {
        RequireValue(subscriptionId, nameof(subscriptionId), "Subscription id");
        ValidateSubscriptionLocalization(spec);
        var body = new
        {
            data = new
            {
                type = "subscriptionLocalizations",
                attributes = new
                {
                    name = spec.Name.Trim(),
                    locale = spec.Locale.Trim(),
                    description = Normalize(spec.Description)
                },
                relationships = new
                {
                    subscription = new { data = new { type = "subscriptions", id = subscriptionId.Trim() } }
                }
            }
        };
        return PostSingleAsync("subscriptionLocalizations", body, ParseSubscriptionLocalization, cancellationToken);
    }

    /// <summary>Updates a customer-facing subscription localization.</summary>
    public Task<AppStoreConnectSubscriptionLocalizationInfo> UpdateSubscriptionLocalizationAsync(
        string localizationId,
        AppStoreConnectSubscriptionLocalizationSpec spec,
        CancellationToken cancellationToken = default)
    {
        RequireValue(localizationId, nameof(localizationId), "Localization id");
        ValidateSubscriptionLocalization(spec);
        var body = new
        {
            data = new
            {
                type = "subscriptionLocalizations",
                id = localizationId.Trim(),
                attributes = new
                {
                    name = spec.Name.Trim(),
                    description = Normalize(spec.Description)
                }
            }
        };
        return PatchSingleAsync(
            $"subscriptionLocalizations/{Uri.EscapeDataString(localizationId.Trim())}",
            body,
            ParseSubscriptionLocalization,
            cancellationToken);
    }

    /// <summary>Lists scheduled subscription prices.</summary>
    public Task<AppStoreConnectSubscriptionPriceInfo[]> GetSubscriptionPricesAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        RequireValue(subscriptionId, nameof(subscriptionId), "Subscription id");
        return GetArrayAsync(
            $"subscriptions/{Uri.EscapeDataString(subscriptionId.Trim())}/prices?include=territory,subscriptionPricePoint&limit=200",
            ParseSubscriptionPrice,
            cancellationToken);
    }

    /// <summary>Creates a scheduled subscription price.</summary>
    public Task<AppStoreConnectSubscriptionPriceInfo> CreateSubscriptionPriceAsync(
        string subscriptionId,
        AppStoreConnectSubscriptionPriceSpec spec,
        CancellationToken cancellationToken = default)
    {
        RequireValue(subscriptionId, nameof(subscriptionId), "Subscription id");
        ValidateSubscriptionPrice(spec);
        var relationships = new Dictionary<string, object>
        {
            ["subscription"] = new { data = new { type = "subscriptions", id = subscriptionId.Trim() } },
            ["subscriptionPricePoint"] = new
            {
                data = new { type = "subscriptionPricePoints", id = spec.SubscriptionPricePointId.Trim() }
            }
        };
        if (!string.IsNullOrWhiteSpace(spec.TerritoryId))
        {
            relationships["territory"] = new
            {
                data = new { type = "territories", id = spec.TerritoryId.Trim() }
            };
        }
        var body = new
        {
            data = new
            {
                type = "subscriptionPrices",
                attributes = new
                {
                    startDate = Normalize(spec.StartDate),
                    preserveCurrentPrice = spec.PreserveCurrentPrice,
                    planType = NormalizeUpper(spec.PlanType)
                },
                relationships
            }
        };
        return PostSingleAsync("subscriptionPrices", body, ParseSubscriptionPrice, cancellationToken);
    }

    /// <summary>Lists current subscription plan availability resources.</summary>
    public async Task<AppStoreConnectSubscriptionAvailabilityInfo[]> GetSubscriptionPlanAvailabilitiesAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        RequireValue(subscriptionId, nameof(subscriptionId), "Subscription id");
        var availabilities = await GetArrayAsync(
            $"subscriptions/{Uri.EscapeDataString(subscriptionId.Trim())}/planAvailabilities?limit=200",
            ParseSubscriptionPlanAvailability,
            cancellationToken).ConfigureAwait(false);
        foreach (var availability in availabilities)
        {
            availability.TerritoryIds = await GetArrayAsync(
                $"subscriptionPlanAvailabilities/{Uri.EscapeDataString(availability.Id)}/availableTerritories?limit=200",
                item => GetString(item, "id") ?? string.Empty,
                cancellationToken).ConfigureAwait(false);
        }
        return availabilities;
    }

    /// <summary>Creates subscription plan availability for explicit territories.</summary>
    public Task<AppStoreConnectSubscriptionAvailabilityInfo> CreateSubscriptionPlanAvailabilityAsync(
        string subscriptionId,
        AppStoreConnectSubscriptionAvailabilitySpec spec,
        CancellationToken cancellationToken = default)
    {
        RequireValue(subscriptionId, nameof(subscriptionId), "Subscription id");
        ValidateSubscriptionAvailability(spec);
        var body = new
        {
            data = new
            {
                type = "subscriptionPlanAvailabilities",
                attributes = new
                {
                    availableInNewTerritories = (bool?)spec.AvailableInNewTerritories,
                    planType = spec.PlanType.Trim().ToUpperInvariant()
                },
                relationships = new
                {
                    availableTerritories = new
                    {
                        data = spec.TerritoryIds.Select(id => new { type = "territories", id = id.Trim() }).ToArray()
                    },
                    subscription = new { data = new { type = "subscriptions", id = subscriptionId.Trim() } }
                }
            }
        };
        return PostSingleAsync(
            "subscriptionPlanAvailabilities",
            body,
            ParseSubscriptionPlanAvailability,
            cancellationToken);
    }

    /// <summary>Updates the mutable territory set for a subscription plan.</summary>
    public Task<AppStoreConnectSubscriptionAvailabilityInfo> UpdateSubscriptionPlanAvailabilityAsync(
        string availabilityId,
        AppStoreConnectSubscriptionAvailabilitySpec spec,
        CancellationToken cancellationToken = default)
    {
        RequireValue(availabilityId, nameof(availabilityId), "Subscription plan availability id");
        ValidateSubscriptionAvailability(spec);
        var body = new
        {
            data = new
            {
                type = "subscriptionPlanAvailabilities",
                id = availabilityId.Trim(),
                attributes = new { availableInNewTerritories = (bool?)spec.AvailableInNewTerritories },
                relationships = new
                {
                    availableTerritories = new
                    {
                        data = spec.TerritoryIds.Select(id => new { type = "territories", id = id.Trim() }).ToArray()
                    }
                }
            }
        };
        return PatchSingleAsync(
            $"subscriptionPlanAvailabilities/{Uri.EscapeDataString(availabilityId.Trim())}",
            body,
            ParseSubscriptionPlanAvailability,
            cancellationToken);
    }

    private static object BuildSubscriptionAttributes(AppStoreConnectSubscriptionSpec spec, bool includeCreateOnlyAttributes)
    {
        var attributes = new Dictionary<string, object?>
        {
            ["name"] = spec.Name.Trim(),
            ["familySharable"] = spec.FamilySharable,
            ["groupLevel"] = spec.GroupLevel
        };
        if (spec.ReviewNote is not null)
            attributes["reviewNote"] = spec.ReviewNote.Trim();
        if (includeCreateOnlyAttributes)
        {
            attributes["productId"] = spec.ProductId.Trim();
            attributes["subscriptionPeriod"] = spec.SubscriptionPeriod.Trim().ToUpperInvariant();
        }
        return attributes.Where(static pair => pair.Value is not null)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value);
    }

    private static AppStoreConnectSubscriptionGroupLocalizationInfo ParseSubscriptionGroupLocalization(JsonElement item)
    {
        var attributes = GetAttributes(item);
        return new AppStoreConnectSubscriptionGroupLocalizationInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            Locale = GetString(attributes, "locale"),
            Name = GetString(attributes, "name"),
            CustomAppName = GetString(attributes, "customAppName")
        };
    }

    private static AppStoreConnectSubscriptionLocalizationInfo ParseSubscriptionLocalization(JsonElement item)
    {
        var attributes = GetAttributes(item);
        return new AppStoreConnectSubscriptionLocalizationInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            Locale = GetString(attributes, "locale"),
            Name = GetString(attributes, "name"),
            Description = GetString(attributes, "description")
        };
    }

    private static AppStoreConnectSubscriptionPriceInfo ParseSubscriptionPrice(JsonElement item)
    {
        var attributes = GetAttributes(item);
        return new AppStoreConnectSubscriptionPriceInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            TerritoryId = GetRelationshipDataId(item, "territory"),
            SubscriptionPricePointId = GetRelationshipDataId(item, "subscriptionPricePoint"),
            StartDate = GetString(attributes, "startDate"),
            Preserved = GetBool(attributes, "preserved"),
            PlanType = GetString(attributes, "planType")
        };
    }

    private static AppStoreConnectSubscriptionAvailabilityInfo ParseSubscriptionPlanAvailability(JsonElement item)
    {
        var attributes = GetAttributes(item);
        return new AppStoreConnectSubscriptionAvailabilityInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            PlanType = GetString(attributes, "planType"),
            AvailableInNewTerritories = GetBool(attributes, "availableInNewTerritories"),
            TerritoryIds = GetRelationshipDataIds(item, "availableTerritories")
        };
    }

    private static string[] GetRelationshipDataIds(JsonElement item, string relationshipName)
    {
        if (!item.TryGetProperty("relationships", out var relationships) ||
            relationships.ValueKind != JsonValueKind.Object ||
            !relationships.TryGetProperty(relationshipName, out var relationship) ||
            relationship.ValueKind != JsonValueKind.Object ||
            !relationship.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }
        return data.EnumerateArray()
            .Select(value => GetString(value, "id"))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .ToArray();
    }

    private static void ValidateGroupLocalization(AppStoreConnectSubscriptionGroupLocalizationSpec spec)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        RequireValue(spec.Locale, nameof(spec), "Locale");
        RequireValue(spec.Name, nameof(spec), "Name");
    }

    private static void ValidateSubscriptionLocalization(AppStoreConnectSubscriptionLocalizationSpec spec)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        RequireValue(spec.Locale, nameof(spec), "Locale");
        RequireValue(spec.Name, nameof(spec), "Name");
    }

    private static void ValidateSubscription(AppStoreConnectSubscriptionSpec spec)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        RequireValue(spec.ProductId, nameof(spec), "ProductId");
        RequireValue(spec.Name, nameof(spec), "Name");
        RequireValue(spec.SubscriptionPeriod, nameof(spec), "SubscriptionPeriod");
        var allowed = new[] { "ONE_WEEK", "ONE_MONTH", "TWO_MONTHS", "THREE_MONTHS", "SIX_MONTHS", "ONE_YEAR" };
        if (!allowed.Contains(spec.SubscriptionPeriod.Trim(), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Unsupported SubscriptionPeriod '{spec.SubscriptionPeriod}'.", nameof(spec));
        if (spec.GroupLevel is < 1)
            throw new ArgumentException("GroupLevel must be at least one.", nameof(spec));
    }

    private static void ValidateSubscriptionPrice(AppStoreConnectSubscriptionPriceSpec spec)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        RequireValue(spec.SubscriptionPricePointId, nameof(spec), "SubscriptionPricePointId");
        ValidateDate(spec.StartDate, nameof(spec.StartDate));
        ValidatePlanType(spec.PlanType, nameof(spec.PlanType), optional: true);
    }

    private static void ValidateSubscriptionAvailability(AppStoreConnectSubscriptionAvailabilitySpec spec)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        ValidatePlanType(spec.PlanType, nameof(spec.PlanType), optional: false);
        if (spec.TerritoryIds.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("TerritoryIds must not contain empty values.", nameof(spec));
    }

    private static void ValidatePlanType(string? value, string parameterName, bool optional)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (!optional) throw new ArgumentException("PlanType is required.", parameterName);
            return;
        }
        if (!string.Equals(value!.Trim(), "MONTHLY", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(value.Trim(), "UPFRONT", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("PlanType must be MONTHLY or UPFRONT.", parameterName);
        }
    }

    private static string? NormalizeUpper(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim().ToUpperInvariant();
}
