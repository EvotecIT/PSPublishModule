using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>Loads and validates declarative App Store commercial and compliance state.</summary>
public sealed class AppStoreConnectGovernanceConfiguration
{
    private static readonly HashSet<string> DeviceFamilies = new(StringComparer.OrdinalIgnoreCase)
    {
        "IPHONE", "IPAD", "APPLE_TV", "APPLE_WATCH", "MAC", "VISION"
    };

    private static readonly HashSet<string> SubscriptionPeriods = new(StringComparer.OrdinalIgnoreCase)
    {
        "ONE_WEEK", "ONE_MONTH", "TWO_MONTHS", "THREE_MONTHS", "SIX_MONTHS", "ONE_YEAR"
    };

    private static readonly HashSet<string> PlanTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "MONTHLY", "UPFRONT"
    };

    private static readonly HashSet<string> IntroductoryOfferDurations = new(StringComparer.OrdinalIgnoreCase)
    {
        "THREE_DAYS", "ONE_WEEK", "TWO_WEEKS", "ONE_MONTH", "TWO_MONTHS", "THREE_MONTHS", "SIX_MONTHS", "ONE_YEAR"
    };

    private static readonly HashSet<string> IntroductoryOfferModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "FREE_TRIAL", "PAY_AS_YOU_GO", "PAY_UP_FRONT"
    };

    /// <summary>Loads one JSON declaration and rejects malformed input.</summary>
    public AppStoreConnectGovernanceSpec Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Governance config path is required.", nameof(path));
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Governance config was not found.", fullPath);
        try
        {
            return JsonSerializer.Deserialize<AppStoreConnectGovernanceSpec>(
                       File.ReadAllText(fullPath),
                       new JsonSerializerOptions
                       {
                           PropertyNameCaseInsensitive = true,
                           ReadCommentHandling = JsonCommentHandling.Skip,
                           AllowTrailingCommas = true,
                           UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
                       })
                   ?? throw new InvalidOperationException("Governance config is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Governance config '{fullPath}' is not valid JSON: {ex.Message}", ex);
        }
    }

    /// <summary>Returns all schema and safety findings without contacting Apple.</summary>
    public AppStoreConnectGovernanceFinding[] Validate(AppStoreConnectGovernanceSpec spec)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        NormalizeCollections(spec);
        var findings = new List<AppStoreConnectGovernanceFinding>();
        if (spec.SchemaVersion != 1) Error(findings, "Governance.SchemaVersion", "schemaVersion", "Only schemaVersion 1 is supported.");
        Required(findings, spec.AppId, "appId", "Governance.AppId", "The exact App Store Connect app id is required.");

        if (spec.Pricing is null && spec.Availability is null && spec.Accessibility.Length == 0 &&
            spec.EncryptionDeclarations.Length == 0 && spec.SubscriptionGroups.Length == 0)
        {
            Error(findings, "Governance.Empty", "$", "Declare at least one managed governance section.");
        }

        ValidatePricing(spec.Pricing, findings);
        ValidateAvailability(spec.Availability, findings);
        ValidateAccessibility(spec.Accessibility, findings);
        ValidateEncryption(spec.EncryptionDeclarations, findings);
        ValidateSubscriptions(spec.SubscriptionGroups, findings);
        return findings.ToArray();
    }

    private static void ValidatePricing(AppStoreConnectAppPricingSpec? pricing, List<AppStoreConnectGovernanceFinding> findings)
    {
        if (pricing is null) return;
        Required(findings, pricing.BaseTerritoryId, "pricing.baseTerritoryId", "Governance.Pricing.BaseTerritory", "Pricing requires an explicit base territory id.");
        Duplicate(findings, pricing.Prices, price => Key(price.TerritoryId, price.StartDate, price.EndDate), "pricing.prices", "Governance.Pricing.Duplicate");
        for (var index = 0; index < pricing.Prices.Length; index++)
        {
            var price = pricing.Prices[index];
            var path = $"pricing.prices[{index}]";
            if (price is null) { Error(findings, "Governance.Pricing.Null", path, "Price entry must not be null."); continue; }
            Required(findings, price.TerritoryId, path + ".territoryId", "Governance.Pricing.Territory", "Territory id is required.");
            Required(findings, price.AppPricePointId, path + ".appPricePointId", "Governance.Pricing.PricePoint", "App price-point id is required.");
            Date(findings, price.StartDate, path + ".startDate");
            Date(findings, price.EndDate, path + ".endDate");
            DateOrder(findings, price.StartDate, price.EndDate, path);
        }
    }

    private static void ValidateAvailability(AppStoreConnectAppAvailabilitySpec? availability, List<AppStoreConnectGovernanceFinding> findings)
    {
        if (availability is null) return;
        Duplicate(findings, availability.Territories, item => item.TerritoryId, "availability.territories", "Governance.Availability.Duplicate");
        for (var index = 0; index < availability.Territories.Length; index++)
        {
            var item = availability.Territories[index];
            var path = $"availability.territories[{index}]";
            if (item is null) { Error(findings, "Governance.Availability.Null", path, "Territory entry must not be null."); continue; }
            Required(findings, item.TerritoryId, path + ".territoryId", "Governance.Availability.Territory", "Territory id is required.");
            Date(findings, item.ReleaseDate, path + ".releaseDate");
        }
    }

    private static void ValidateAccessibility(AppStoreConnectAccessibilityDeclarationSpec[] declarations, List<AppStoreConnectGovernanceFinding> findings)
    {
        Duplicate(findings, declarations, item => item.DeviceFamily, "accessibility", "Governance.Accessibility.Duplicate");
        for (var index = 0; index < declarations.Length; index++)
        {
            var item = declarations[index];
            var path = $"accessibility[{index}]";
            if (item is null) { Error(findings, "Governance.Accessibility.Null", path, "Accessibility entry must not be null."); continue; }
            Required(findings, item.DeviceFamily, path + ".deviceFamily", "Governance.Accessibility.DeviceFamily", "Device family is required.");
            if (!string.IsNullOrWhiteSpace(item.DeviceFamily) && !DeviceFamilies.Contains(item.DeviceFamily.Trim()))
                Error(findings, "Governance.Accessibility.DeviceFamily", path + ".deviceFamily", "Use IPHONE, IPAD, APPLE_TV, APPLE_WATCH, MAC, or VISION.");
            if (AccessibilityFacts(item).All(value => value is null))
                Error(findings, "Governance.Accessibility.Empty", path, "At least one reviewed accessibility fact is required.");
        }
    }

    private static void ValidateEncryption(AppStoreConnectEncryptionDeclarationSpec[] declarations, List<AppStoreConnectGovernanceFinding> findings)
    {
        Duplicate(findings, declarations, item => Key(item.AppDescription, item.ContainsProprietaryCryptography, item.ContainsThirdPartyCryptography, item.AvailableOnFrenchStore), "encryptionDeclarations", "Governance.Encryption.Duplicate");
        for (var index = 0; index < declarations.Length; index++)
        {
            var item = declarations[index];
            var path = $"encryptionDeclarations[{index}]";
            if (item is null) { Error(findings, "Governance.Encryption.Null", path, "Encryption declaration must not be null."); continue; }
            Required(findings, item.AppDescription, path + ".appDescription", "Governance.Encryption.Description", "A human-reviewed app description is required.");
        }
    }

    private static void ValidateSubscriptions(AppStoreConnectSubscriptionGroupSpec[] groups, List<AppStoreConnectGovernanceFinding> findings)
    {
        Duplicate(findings, groups, item => string.IsNullOrWhiteSpace(item.Id) ? item.ReferenceName : item.Id!, "subscriptionGroups", "Governance.Subscriptions.DuplicateGroup");
        var products = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            var group = groups[groupIndex];
            var path = $"subscriptionGroups[{groupIndex}]";
            if (group is null) { Error(findings, "Governance.Subscriptions.NullGroup", path, "Subscription group must not be null."); continue; }
            Required(findings, group.ReferenceName, path + ".referenceName", "Governance.Subscriptions.GroupName", "Subscription group reference name is required.");
            ValidateGroupLocalizations(group.Localizations, path, findings);
            Duplicate(findings, group.Subscriptions, item => item.ProductId, path + ".subscriptions", "Governance.Subscriptions.DuplicateProduct");
            for (var subscriptionIndex = 0; subscriptionIndex < group.Subscriptions.Length; subscriptionIndex++)
            {
                var subscription = group.Subscriptions[subscriptionIndex];
                var subscriptionPath = $"{path}.subscriptions[{subscriptionIndex}]";
                if (subscription is null) { Error(findings, "Governance.Subscriptions.NullProduct", subscriptionPath, "Subscription must not be null."); continue; }
                Required(findings, subscription.ProductId, subscriptionPath + ".productId", "Governance.Subscriptions.ProductId", "Product id is required.");
                Required(findings, subscription.Name, subscriptionPath + ".name", "Governance.Subscriptions.Name", "Reference name is required.");
                Required(findings, subscription.SubscriptionPeriod, subscriptionPath + ".subscriptionPeriod", "Governance.Subscriptions.Period", "Subscription period is required.");
                if (!string.IsNullOrWhiteSpace(subscription.ProductId) && !products.Add(subscription.ProductId.Trim()))
                    Error(findings, "Governance.Subscriptions.DuplicateProduct", subscriptionPath + ".productId", "Product ids must be unique across every group.");
                if (!string.IsNullOrWhiteSpace(subscription.SubscriptionPeriod) && !SubscriptionPeriods.Contains(subscription.SubscriptionPeriod.Trim()))
                    Error(findings, "Governance.Subscriptions.Period", subscriptionPath + ".subscriptionPeriod", "Unsupported subscription period.");
                if (subscription.GroupLevel is < 1)
                    Error(findings, "Governance.Subscriptions.GroupLevel", subscriptionPath + ".groupLevel", "Group level must be at least one.");
                ValidateSubscriptionLocalizations(subscription.Localizations, subscriptionPath, findings);
                ValidateSubscriptionPrices(subscription.Prices, subscriptionPath, findings);
                ValidateSubscriptionIntroductoryOffers(subscription, subscriptionPath, findings);
                ValidateSubscriptionAvailabilities(subscription.Availabilities, subscriptionPath, findings);
            }
        }
    }

    private static void ValidateGroupLocalizations(AppStoreConnectSubscriptionGroupLocalizationSpec[] items, string parent, List<AppStoreConnectGovernanceFinding> findings)
    {
        Duplicate(findings, items, item => item.Locale, parent + ".localizations", "Governance.Subscriptions.DuplicateLocale");
        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            var path = $"{parent}.localizations[{index}]";
            if (item is null) { Error(findings, "Governance.Subscriptions.NullLocalization", path, "Localization must not be null."); continue; }
            Required(findings, item.Locale, path + ".locale", "Governance.Subscriptions.Locale", "Locale is required.");
            Required(findings, item.Name, path + ".name", "Governance.Subscriptions.LocalizationName", "Localized name is required.");
        }
    }

    private static void ValidateSubscriptionLocalizations(AppStoreConnectSubscriptionLocalizationSpec[] items, string parent, List<AppStoreConnectGovernanceFinding> findings)
    {
        Duplicate(findings, items, item => item.Locale, parent + ".localizations", "Governance.Subscriptions.DuplicateLocale");
        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            var path = $"{parent}.localizations[{index}]";
            if (item is null) { Error(findings, "Governance.Subscriptions.NullLocalization", path, "Localization must not be null."); continue; }
            Required(findings, item.Locale, path + ".locale", "Governance.Subscriptions.Locale", "Locale is required.");
            Required(findings, item.Name, path + ".name", "Governance.Subscriptions.LocalizationName", "Localized name is required.");
        }
    }

    private static void ValidateSubscriptionPrices(AppStoreConnectSubscriptionPriceSpec[] items, string parent, List<AppStoreConnectGovernanceFinding> findings)
    {
        Duplicate(findings, items, item => Key(item.TerritoryId, item.StartDate, item.PlanType), parent + ".prices", "Governance.Subscriptions.DuplicatePrice");
        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            var path = $"{parent}.prices[{index}]";
            if (item is null) { Error(findings, "Governance.Subscriptions.NullPrice", path, "Price must not be null."); continue; }
            Required(findings, item.TerritoryId, path + ".territoryId", "Governance.Subscriptions.PriceTerritory", "Territory id is required.");
            Required(findings, item.SubscriptionPricePointId, path + ".subscriptionPricePointId", "Governance.Subscriptions.PricePoint", "Subscription price-point id is required.");
            Date(findings, item.StartDate, path + ".startDate");
            PlanType(findings, item.PlanType, path + ".planType", optional: true);
        }
    }

    private static void ValidateSubscriptionAvailabilities(AppStoreConnectSubscriptionAvailabilitySpec[] items, string parent, List<AppStoreConnectGovernanceFinding> findings)
    {
        Duplicate(findings, items, item => item.PlanType, parent + ".availabilities", "Governance.Subscriptions.DuplicatePlanAvailability");
        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            var path = $"{parent}.availabilities[{index}]";
            if (item is null) { Error(findings, "Governance.Subscriptions.NullAvailability", path, "Plan availability must not be null."); continue; }
            PlanType(findings, item.PlanType, path + ".planType", optional: false);
            if (item.TerritoryIds.Any(string.IsNullOrWhiteSpace))
                Error(findings, "Governance.Subscriptions.AvailabilityTerritory", path + ".territoryIds", "Territory ids must not contain empty values.");
            if (item.TerritoryIds.GroupBy(value => value, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
                Error(findings, "Governance.Subscriptions.DuplicateAvailabilityTerritory", path + ".territoryIds", "Territory ids must be unique.");
        }
    }

    private static void ValidateSubscriptionIntroductoryOffers(AppStoreConnectSubscriptionSpec subscription, string parent, List<AppStoreConnectGovernanceFinding> findings)
    {
        var items = subscription.IntroductoryOffers;
        Duplicate(findings, items, item => Key(item.TerritoriesFromPlanType ?? string.Join(",", (item.TerritoryIds ?? Array.Empty<string>()).OrderBy(value => value, StringComparer.OrdinalIgnoreCase)), item.StartDate, item.EndDate, item.Duration, item.OfferMode, item.NumberOfPeriods, item.SubscriptionPricePointId),
            parent + ".introductoryOffers", "Governance.Subscriptions.DuplicateIntroductoryOffer");
        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            var path = $"{parent}.introductoryOffers[{index}]";
            if (item is null) { Error(findings, "Governance.Subscriptions.NullIntroductoryOffer", path, "Introductory offer must not be null."); continue; }
            Required(findings, item.Duration, path + ".duration", "Governance.Subscriptions.IntroductoryOfferDuration", "Introductory-offer duration is required.");
            Required(findings, item.OfferMode, path + ".offerMode", "Governance.Subscriptions.IntroductoryOfferMode", "Introductory-offer mode is required.");
            var hasExplicitTerritories = item.TerritoryIds is { Length: > 0 };
            var hasPlanTerritories = !string.IsNullOrWhiteSpace(item.TerritoriesFromPlanType);
            if (hasExplicitTerritories == hasPlanTerritories)
                Error(findings, "Governance.Subscriptions.IntroductoryOfferTerritory", path, "Set exactly one of territoryIds or territoriesFromPlanType.");
            if (item.TerritoryIds?.Any(string.IsNullOrWhiteSpace) == true)
                Error(findings, "Governance.Subscriptions.IntroductoryOfferTerritory", path + ".territoryIds", "Territory ids must not contain empty values.");
            if (item.TerritoryIds?.GroupBy(value => value, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1) == true)
                Error(findings, "Governance.Subscriptions.DuplicateIntroductoryOfferTerritory", path + ".territoryIds", "Territory ids must be unique.");
            if (hasPlanTerritories)
            {
                PlanType(findings, item.TerritoriesFromPlanType, path + ".territoriesFromPlanType", optional: false);
                if (!subscription.Availabilities.Any(availability => availability is not null && string.Equals(availability.PlanType?.Trim(), item.TerritoriesFromPlanType?.Trim(), StringComparison.OrdinalIgnoreCase)))
                    Error(findings, "Governance.Subscriptions.IntroductoryOfferTerritorySource", path + ".territoriesFromPlanType", "The referenced subscription plan availability must be declared on the same subscription.");
            }
            if (!string.IsNullOrWhiteSpace(item.Duration) && !IntroductoryOfferDurations.Contains(item.Duration.Trim()))
                Error(findings, "Governance.Subscriptions.IntroductoryOfferDuration", path + ".duration", "Unsupported introductory-offer duration.");
            if (!string.IsNullOrWhiteSpace(item.OfferMode) && !IntroductoryOfferModes.Contains(item.OfferMode.Trim()))
                Error(findings, "Governance.Subscriptions.IntroductoryOfferMode", path + ".offerMode", "Unsupported introductory-offer mode.");
            if (item.NumberOfPeriods < 1)
                Error(findings, "Governance.Subscriptions.IntroductoryOfferPeriods", path + ".numberOfPeriods", "Number of periods must be at least one.");
            if (!string.Equals(item.OfferMode?.Trim(), "FREE_TRIAL", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(item.SubscriptionPricePointId))
                Error(findings, "Governance.Subscriptions.IntroductoryOfferPricePoint", path + ".subscriptionPricePointId", "Paid introductory offers require a subscription price-point id.");
            Date(findings, item.StartDate, path + ".startDate");
            Date(findings, item.EndDate, path + ".endDate");
            DateOrder(findings, item.StartDate, item.EndDate, path);
        }
    }

    private static bool?[] AccessibilityFacts(AppStoreConnectAccessibilityDeclarationSpec value) =>
    [
        value.SupportsAudioDescriptions, value.SupportsCaptions, value.SupportsDarkInterface,
        value.SupportsDifferentiateWithoutColorAlone, value.SupportsLargerText, value.SupportsReducedMotion,
        value.SupportsSufficientContrast, value.SupportsVoiceControl, value.SupportsVoiceover
    ];

    private static void Required(List<AppStoreConnectGovernanceFinding> findings, string? value, string path, string code, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) Error(findings, code, path, message);
    }

    private static void Date(List<AppStoreConnectGovernanceFinding> findings, string? value, string path)
    {
        if (!string.IsNullOrWhiteSpace(value) && !DateTime.TryParseExact(value!.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            Error(findings, "Governance.Date", path, "Date must use yyyy-MM-dd.");
    }

    private static void DateOrder(List<AppStoreConnectGovernanceFinding> findings, string? start, string? end, string path)
    {
        if (DateTime.TryParseExact(start, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDate) &&
            DateTime.TryParseExact(end, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDate) && startDate > endDate)
            Error(findings, "Governance.DateOrder", path, "End date must be on or after start date.");
    }

    private static void PlanType(List<AppStoreConnectGovernanceFinding> findings, string? value, string path, bool optional)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (!optional) Error(findings, "Governance.Subscriptions.PlanType", path, "Plan type is required.");
            return;
        }
        if (!PlanTypes.Contains(value!.Trim())) Error(findings, "Governance.Subscriptions.PlanType", path, "Plan type must be MONTHLY or UPFRONT.");
    }

    private static void Duplicate<T>(List<AppStoreConnectGovernanceFinding> findings, T[] items, Func<T, string> key, string path, string code)
    {
        var duplicate = items.Where(item => item is not null).Select(key).Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) Error(findings, code, path, $"Duplicate declaration key '{duplicate.Key}'.");
    }

    private static string Key(params object?[] parts) => string.Join("|", parts.Select(part => Convert.ToString(part, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty));

    private static void Error(List<AppStoreConnectGovernanceFinding> findings, string code, string path, string message) =>
        findings.Add(new AppStoreConnectGovernanceFinding { Code = code, Path = path, Message = message, IsError = true });

    private static void NormalizeCollections(AppStoreConnectGovernanceSpec spec)
    {
        spec.Accessibility ??= Array.Empty<AppStoreConnectAccessibilityDeclarationSpec>();
        spec.EncryptionDeclarations ??= Array.Empty<AppStoreConnectEncryptionDeclarationSpec>();
        spec.SubscriptionGroups ??= Array.Empty<AppStoreConnectSubscriptionGroupSpec>();
        if (spec.Pricing is not null) spec.Pricing.Prices ??= Array.Empty<AppStoreConnectAppPriceSpec>();
        if (spec.Availability is not null) spec.Availability.Territories ??= Array.Empty<AppStoreConnectTerritoryAvailabilitySpec>();
        foreach (var group in spec.SubscriptionGroups.Where(group => group is not null))
        {
            group.Localizations ??= Array.Empty<AppStoreConnectSubscriptionGroupLocalizationSpec>();
            group.Subscriptions ??= Array.Empty<AppStoreConnectSubscriptionSpec>();
            foreach (var subscription in group.Subscriptions.Where(subscription => subscription is not null))
            {
                subscription.Localizations ??= Array.Empty<AppStoreConnectSubscriptionLocalizationSpec>();
                subscription.Prices ??= Array.Empty<AppStoreConnectSubscriptionPriceSpec>();
                subscription.IntroductoryOffers ??= Array.Empty<AppStoreConnectSubscriptionIntroductoryOfferSpec>();
                subscription.Availabilities ??= Array.Empty<AppStoreConnectSubscriptionAvailabilitySpec>();
                foreach (var availability in subscription.Availabilities.Where(availability => availability is not null))
                    availability.TerritoryIds ??= Array.Empty<string>();
            }
        }
    }
}
