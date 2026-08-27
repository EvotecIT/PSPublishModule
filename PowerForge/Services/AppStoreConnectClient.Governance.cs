using System.Net.Http;
using System.Text.Json;

namespace PowerForge;

public sealed partial class AppStoreConnectClient
{
    /// <summary>Reads the app price schedule and its configured manual prices.</summary>
    public async Task<AppStoreConnectAppPriceScheduleInfo?> GetAppPriceScheduleAsync(
        string appId,
        CancellationToken cancellationToken = default)
    {
        RequireValue(appId, nameof(appId), "App id");
        using var document = await GetJsonAsync(
            $"apps/{Uri.EscapeDataString(appId.Trim())}/appPriceSchedule?include=baseTerritory",
            cancellationToken,
            returnNullOnNotFound: true).ConfigureAwait(false);
        if (document is null ||
            !document.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var scheduleId = GetString(data, "id") ?? string.Empty;
        var prices = string.IsNullOrWhiteSpace(scheduleId)
            ? Array.Empty<AppStoreConnectAppPriceInfo>()
            : await GetArrayAsync(
                $"appPriceSchedules/{Uri.EscapeDataString(scheduleId)}/manualPrices?include=appPricePoint,territory&limit=200",
                ParseAppPrice,
                cancellationToken,
                returnEmptyOnNotFound: true).ConfigureAwait(false);
        return new AppStoreConnectAppPriceScheduleInfo
        {
            Id = scheduleId,
            BaseTerritoryId = GetRelationshipDataId(data, "baseTerritory"),
            Prices = prices
        };
    }

    /// <summary>Creates a base schedule or adds explicit scheduled prices to an app.</summary>
    public Task<AppStoreConnectAppPriceScheduleInfo> CreateAppPriceScheduleAsync(
        string appId,
        AppStoreConnectAppPricingSpec spec,
        CancellationToken cancellationToken = default)
    {
        RequireValue(appId, nameof(appId), "App id");
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));
        RequireValue(spec.BaseTerritoryId, nameof(spec), "BaseTerritoryId");
        ValidateAppPrices(spec.Prices);

        var prices = spec.Prices.Select((price, index) => new
        {
            Id = $"price-{index + 1}",
            Spec = price
        }).ToArray();
        var body = new
        {
            data = new
            {
                type = "appPriceSchedules",
                relationships = new
                {
                    app = new { data = new { type = "apps", id = appId.Trim() } },
                    baseTerritory = new { data = new { type = "territories", id = spec.BaseTerritoryId.Trim() } },
                    manualPrices = new
                    {
                        data = prices.Select(price => new { type = "appPrices", id = price.Id }).ToArray()
                    }
                }
            },
            included = prices.Select(price => new
            {
                type = "appPrices",
                id = price.Id,
                attributes = new
                {
                    startDate = Normalize(price.Spec.StartDate),
                    endDate = Normalize(price.Spec.EndDate)
                },
                relationships = new
                {
                    appPricePoint = new
                    {
                        data = new { type = "appPricePoints", id = price.Spec.AppPricePointId.Trim() }
                    }
                }
            }).ToArray()
        };

        return PostSingleAsync(
            "appPriceSchedules",
            body,
            item => new AppStoreConnectAppPriceScheduleInfo { Id = GetString(item, "id") ?? string.Empty },
            cancellationToken);
    }

    /// <summary>Reads app-wide territory availability.</summary>
    public async Task<AppStoreConnectAppAvailabilityInfo?> GetAppAvailabilityAsync(
        string appId,
        CancellationToken cancellationToken = default)
    {
        RequireValue(appId, nameof(appId), "App id");
        using var document = await GetJsonAsync(
            $"apps/{Uri.EscapeDataString(appId.Trim())}/appAvailabilityV2",
            cancellationToken,
            returnNullOnNotFound: true).ConfigureAwait(false);
        if (document is null ||
            !document.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var attributes = GetAttributes(data);
        var availabilityId = GetString(data, "id") ?? string.Empty;
        var territories = string.IsNullOrWhiteSpace(availabilityId)
            ? Array.Empty<AppStoreConnectTerritoryAvailabilityInfo>()
            : await GetArrayAsync(
                $"../v2/appAvailabilities/{Uri.EscapeDataString(availabilityId)}/territoryAvailabilities?include=territory&limit=200",
                ParseTerritoryAvailability,
                cancellationToken).ConfigureAwait(false);
        return new AppStoreConnectAppAvailabilityInfo
        {
            Id = availabilityId,
            AvailableInNewTerritories = GetBool(attributes, "availableInNewTerritories"),
            Territories = territories
        };
    }

    /// <summary>Creates the app-wide territory availability resource.</summary>
    public Task<AppStoreConnectAppAvailabilityInfo> CreateAppAvailabilityAsync(
        string appId,
        AppStoreConnectAppAvailabilitySpec spec,
        CancellationToken cancellationToken = default)
    {
        RequireValue(appId, nameof(appId), "App id");
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));
        ValidateTerritories(spec.Territories);
        var territories = spec.Territories.Select((territory, index) => new
        {
            Id = $"territory-availability-{index + 1}",
            Spec = territory
        }).ToArray();
        var body = new
        {
            data = new
            {
                type = "appAvailabilities",
                attributes = new { availableInNewTerritories = spec.AvailableInNewTerritories },
                relationships = new
                {
                    app = new { data = new { type = "apps", id = appId.Trim() } },
                    territoryAvailabilities = new
                    {
                        data = territories.Select(territory => new { type = "territoryAvailabilities", id = territory.Id }).ToArray()
                    }
                }
            },
            included = territories.Select(territory => new
            {
                type = "territoryAvailabilities",
                id = territory.Id,
                attributes = BuildTerritoryAvailabilityAttributes(territory.Spec),
                relationships = new
                {
                    territory = new { data = new { type = "territories", id = territory.Spec.TerritoryId.Trim() } }
                }
            }).ToArray()
        };

        return PostSingleAsync(
            "../v2/appAvailabilities",
            body,
            ParseAppAvailability,
            cancellationToken);
    }

    /// <summary>Updates one existing territory availability entry.</summary>
    public Task<AppStoreConnectTerritoryAvailabilityInfo> UpdateTerritoryAvailabilityAsync(
        string territoryAvailabilityId,
        AppStoreConnectTerritoryAvailabilitySpec spec,
        CancellationToken cancellationToken = default)
    {
        RequireValue(territoryAvailabilityId, nameof(territoryAvailabilityId), "Territory availability id");
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));
        RequireValue(spec.TerritoryId, nameof(spec), "TerritoryId");
        ValidateDate(spec.ReleaseDate, nameof(spec.ReleaseDate));
        var body = new
        {
            data = new
            {
                type = "territoryAvailabilities",
                id = territoryAvailabilityId.Trim(),
                attributes = BuildTerritoryAvailabilityAttributes(spec)
            }
        };
        return PatchSingleAsync(
            $"territoryAvailabilities/{Uri.EscapeDataString(territoryAvailabilityId.Trim())}",
            body,
            ParseTerritoryAvailability,
            cancellationToken);
    }

    private static object BuildTerritoryAvailabilityAttributes(AppStoreConnectTerritoryAvailabilitySpec spec)
    {
        var attributes = new Dictionary<string, object?>
        {
            ["available"] = spec.Available,
            ["releaseDate"] = Normalize(spec.ReleaseDate)
        };
        if (spec.PreOrderEnabled.HasValue)
            attributes["preOrderEnabled"] = spec.PreOrderEnabled.Value;
        return attributes;
    }

    /// <summary>Lists accessibility declarations for an app.</summary>
    public Task<AppStoreConnectAccessibilityDeclarationInfo[]> GetAccessibilityDeclarationsAsync(
        string appId,
        CancellationToken cancellationToken = default)
    {
        RequireValue(appId, nameof(appId), "App id");
        return GetArrayAsync(
            $"apps/{Uri.EscapeDataString(appId.Trim())}/accessibilityDeclarations?limit=200",
            ParseAccessibilityDeclaration,
            cancellationToken);
    }

    /// <summary>Creates reviewed accessibility facts for one device family.</summary>
    public Task<AppStoreConnectAccessibilityDeclarationInfo> CreateAccessibilityDeclarationAsync(
        string appId,
        AppStoreConnectAccessibilityDeclarationSpec spec,
        CancellationToken cancellationToken = default)
    {
        RequireValue(appId, nameof(appId), "App id");
        ValidateAccessibility(spec);
        var body = new
        {
            data = new
            {
                type = "accessibilityDeclarations",
                attributes = BuildAccessibilityAttributes(spec, includeDeviceFamily: true),
                relationships = new
                {
                    app = new { data = new { type = "apps", id = appId.Trim() } }
                }
            }
        };
        return PostSingleAsync("accessibilityDeclarations", body, ParseAccessibilityDeclaration, cancellationToken);
    }

    /// <summary>Updates and optionally publishes reviewed accessibility facts.</summary>
    public Task<AppStoreConnectAccessibilityDeclarationInfo> UpdateAccessibilityDeclarationAsync(
        string declarationId,
        AppStoreConnectAccessibilityDeclarationSpec spec,
        CancellationToken cancellationToken = default)
    {
        RequireValue(declarationId, nameof(declarationId), "Accessibility declaration id");
        ValidateAccessibility(spec);
        var body = new
        {
            data = new
            {
                type = "accessibilityDeclarations",
                id = declarationId.Trim(),
                attributes = BuildAccessibilityAttributes(spec, includeDeviceFamily: false)
            }
        };
        return PatchSingleAsync(
            $"accessibilityDeclarations/{Uri.EscapeDataString(declarationId.Trim())}",
            body,
            ParseAccessibilityDeclaration,
            cancellationToken);
    }

    /// <summary>Lists export-compliance declarations for an app.</summary>
    public Task<AppStoreConnectEncryptionDeclarationInfo[]> GetEncryptionDeclarationsAsync(
        string appId,
        CancellationToken cancellationToken = default)
    {
        RequireValue(appId, nameof(appId), "App id");
        return GetArrayAsync(
            $"appEncryptionDeclarations?filter%5Bapp%5D={Uri.EscapeDataString(appId.Trim())}&limit=200",
            ParseEncryptionDeclaration,
            cancellationToken);
    }

    /// <summary>Creates an explicit, human-reviewed export-compliance declaration.</summary>
    public Task<AppStoreConnectEncryptionDeclarationInfo> CreateEncryptionDeclarationAsync(
        string appId,
        AppStoreConnectEncryptionDeclarationSpec spec,
        CancellationToken cancellationToken = default)
    {
        RequireValue(appId, nameof(appId), "App id");
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));
        RequireValue(spec.AppDescription, nameof(spec), "AppDescription");
        var body = new
        {
            data = new
            {
                type = "appEncryptionDeclarations",
                attributes = new
                {
                    appDescription = spec.AppDescription.Trim(),
                    containsProprietaryCryptography = spec.ContainsProprietaryCryptography,
                    containsThirdPartyCryptography = spec.ContainsThirdPartyCryptography,
                    availableOnFrenchStore = spec.AvailableOnFrenchStore
                },
                relationships = new
                {
                    app = new { data = new { type = "apps", id = appId.Trim() } }
                }
            }
        };
        return PostSingleAsync("appEncryptionDeclarations", body, ParseEncryptionDeclaration, cancellationToken);
    }

    private static object BuildAccessibilityAttributes(
        AppStoreConnectAccessibilityDeclarationSpec spec,
        bool includeDeviceFamily)
    {
        var values = new Dictionary<string, object?>
        {
            ["supportsAudioDescriptions"] = spec.SupportsAudioDescriptions,
            ["supportsCaptions"] = spec.SupportsCaptions,
            ["supportsDarkInterface"] = spec.SupportsDarkInterface,
            ["supportsDifferentiateWithoutColorAlone"] = spec.SupportsDifferentiateWithoutColorAlone,
            ["supportsLargerText"] = spec.SupportsLargerText,
            ["supportsReducedMotion"] = spec.SupportsReducedMotion,
            ["supportsSufficientContrast"] = spec.SupportsSufficientContrast,
            ["supportsVoiceControl"] = spec.SupportsVoiceControl,
            ["supportsVoiceover"] = spec.SupportsVoiceover
        };
        if (includeDeviceFamily)
            values["deviceFamily"] = spec.DeviceFamily.Trim().ToUpperInvariant();
        else if (spec.Publish)
            values["publish"] = true;
        return values.Where(static pair => pair.Value is not null)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value);
    }

    private static AppStoreConnectAppAvailabilityInfo ParseAppAvailability(JsonElement item)
    {
        var attributes = GetAttributes(item);
        return new AppStoreConnectAppAvailabilityInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            AvailableInNewTerritories = GetBool(attributes, "availableInNewTerritories") == true
        };
    }

    private static AppStoreConnectAppPriceInfo ParseAppPrice(JsonElement item)
    {
        var attributes = GetAttributes(item);
        return new AppStoreConnectAppPriceInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            AppPricePointId = GetRelationshipDataId(item, "appPricePoint"),
            TerritoryId = GetRelationshipDataId(item, "territory"),
            StartDate = GetString(attributes, "startDate"),
            EndDate = GetString(attributes, "endDate")
        };
    }

    private static AppStoreConnectTerritoryAvailabilityInfo ParseTerritoryAvailability(JsonElement item)
    {
        var attributes = GetAttributes(item);
        return new AppStoreConnectTerritoryAvailabilityInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            TerritoryId = GetRelationshipDataId(item, "territory"),
            Available = GetBool(attributes, "available"),
            ReleaseDate = GetString(attributes, "releaseDate"),
            PreOrderEnabled = GetBool(attributes, "preOrderEnabled")
        };
    }

    private static AppStoreConnectAccessibilityDeclarationInfo ParseAccessibilityDeclaration(JsonElement item)
    {
        var attributes = GetAttributes(item);
        return new AppStoreConnectAccessibilityDeclarationInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            DeviceFamily = GetString(attributes, "deviceFamily"),
            State = GetString(attributes, "state"),
            SupportsAudioDescriptions = GetBool(attributes, "supportsAudioDescriptions"),
            SupportsCaptions = GetBool(attributes, "supportsCaptions"),
            SupportsDarkInterface = GetBool(attributes, "supportsDarkInterface"),
            SupportsDifferentiateWithoutColorAlone = GetBool(attributes, "supportsDifferentiateWithoutColorAlone"),
            SupportsLargerText = GetBool(attributes, "supportsLargerText"),
            SupportsReducedMotion = GetBool(attributes, "supportsReducedMotion"),
            SupportsSufficientContrast = GetBool(attributes, "supportsSufficientContrast"),
            SupportsVoiceControl = GetBool(attributes, "supportsVoiceControl"),
            SupportsVoiceover = GetBool(attributes, "supportsVoiceover")
        };
    }

    private static AppStoreConnectEncryptionDeclarationInfo ParseEncryptionDeclaration(JsonElement item)
    {
        var attributes = GetAttributes(item);
        return new AppStoreConnectEncryptionDeclarationInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            AppDescription = GetString(attributes, "appDescription"),
            Exempt = GetBool(attributes, "exempt"),
            ContainsProprietaryCryptography = GetBool(attributes, "containsProprietaryCryptography"),
            ContainsThirdPartyCryptography = GetBool(attributes, "containsThirdPartyCryptography"),
            AvailableOnFrenchStore = GetBool(attributes, "availableOnFrenchStore"),
            State = GetString(attributes, "appEncryptionDeclarationState")
        };
    }

    private static T[] ParseIncluded<T>(JsonElement root, string type, Func<JsonElement, T> parser)
    {
        if (!root.TryGetProperty("included", out var included) || included.ValueKind != JsonValueKind.Array)
            return Array.Empty<T>();
        return included.EnumerateArray()
            .Where(item => string.Equals(GetString(item, "type"), type, StringComparison.Ordinal))
            .Select(parser)
            .ToArray();
    }

    private static void ValidateAppPrices(IEnumerable<AppStoreConnectAppPriceSpec>? prices)
    {
        foreach (var price in prices ?? Array.Empty<AppStoreConnectAppPriceSpec>())
        {
            if (price is null)
                throw new ArgumentException("Pricing entries must not be null.", nameof(prices));
            RequireValue(price.AppPricePointId, nameof(prices), "AppPricePointId");
            RequireValue(price.TerritoryId, nameof(prices), "TerritoryId");
            ValidateDate(price.StartDate, nameof(price.StartDate));
            ValidateDate(price.EndDate, nameof(price.EndDate));
        }
    }

    private static void ValidateTerritories(IEnumerable<AppStoreConnectTerritoryAvailabilitySpec>? territories)
    {
        foreach (var territory in territories ?? Array.Empty<AppStoreConnectTerritoryAvailabilitySpec>())
        {
            if (territory is null)
                throw new ArgumentException("Territory entries must not be null.", nameof(territories));
            RequireValue(territory.TerritoryId, nameof(territories), "TerritoryId");
            ValidateDate(territory.ReleaseDate, nameof(territory.ReleaseDate));
        }
    }

    private static void ValidateAccessibility(AppStoreConnectAccessibilityDeclarationSpec spec)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));
        RequireValue(spec.DeviceFamily, nameof(spec), "DeviceFamily");
        var allowed = new[] { "IPHONE", "IPAD", "APPLE_TV", "APPLE_WATCH", "MAC", "VISION" };
        if (!allowed.Contains(spec.DeviceFamily.Trim(), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Unsupported accessibility DeviceFamily '{spec.DeviceFamily}'.", nameof(spec));
    }

    private static void RequireValue(string? value, string parameterName, string displayName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{displayName} is required.", parameterName);
    }

    private static void ValidateDate(string? value, string parameterName)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            (!DateTime.TryParseExact(value!.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _)))
        {
            throw new ArgumentException("Date must use yyyy-MM-dd.", parameterName);
        }
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
