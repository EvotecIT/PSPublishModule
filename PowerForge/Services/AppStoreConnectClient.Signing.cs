using System.Globalization;
using System.Text.Json;

namespace PowerForge;

public sealed partial class AppStoreConnectClient
{
    /// <summary>
    /// Lists signing bundle identifiers, optionally filtered by identifier and platform.
    /// Follows Apple's pagination links; <paramref name="limit"/> controls page size.
    /// </summary>
    public Task<AppStoreConnectBundleIdInfo[]> GetBundleIdsAsync(
        string? identifier = null,
        AppStoreConnectBundleIdPlatform? platform = null,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["filter[identifier]"] = identifier?.Trim(),
            ["filter[platform]"] = platform.HasValue ? ToBundleIdPlatform(platform.Value) : null,
            ["limit"] = ClampLimit(limit).ToString(CultureInfo.InvariantCulture)
        };
        return GetArrayAsync("bundleIds" + BuildQuery(query), ParseBundleId, cancellationToken);
    }

    /// <summary>
    /// Registers a signing bundle identifier. Requires Apple account permission to create
    /// identifiers; does not create an app record, enable capabilities, or create profiles.
    /// Choose iOS or macOS for creation; Apple may return a universal registration.
    /// <paramref name="seedId"/> selects an existing App ID prefix for accounts that require one;
    /// null lets Apple choose the account default.
    /// </summary>
    public Task<AppStoreConnectBundleIdInfo> RegisterBundleIdAsync(
        string identifier,
        string name,
        AppStoreConnectBundleIdPlatform platform,
        string? seedId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("Bundle identifier is required.", nameof(identifier));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Bundle identifier name is required.", nameof(name));
        if (seedId is not null && string.IsNullOrWhiteSpace(seedId))
            throw new ArgumentException("An explicit App ID prefix must not be blank.", nameof(seedId));
        if (platform == AppStoreConnectBundleIdPlatform.Universal)
            throw new ArgumentException("Choose iOS or macOS for registration; Universal is an inventory scope. Apple may return a universal registration.", nameof(platform));
        var attributes = new Dictionary<string, object>
        {
            ["identifier"] = identifier.Trim(),
            ["name"] = name.Trim(),
            ["platform"] = ToBundleIdPlatform(platform)
        };
        if (seedId is not null)
            attributes["seedId"] = seedId.Trim();
        var body = new
        {
            data = new
            {
                type = "bundleIds",
                attributes
            }
        };
        return PostSingleAsync("bundleIds", body, ParseBundleId, cancellationToken);
    }

    private static string ToBundleIdPlatform(AppStoreConnectBundleIdPlatform platform)
        => platform switch
        {
            AppStoreConnectBundleIdPlatform.iOS => "IOS",
            AppStoreConnectBundleIdPlatform.macOS => "MAC_OS",
            AppStoreConnectBundleIdPlatform.Universal => "UNIVERSAL",
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported bundle identifier platform.")
        };

    private static AppStoreConnectBundleIdInfo ParseBundleId(JsonElement item)
    {
        var attributes = item.TryGetProperty("attributes", out var value) ? value : default;
        return new AppStoreConnectBundleIdInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            Identifier = GetString(attributes, "identifier"),
            Name = GetString(attributes, "name"),
            Platform = GetString(attributes, "platform"),
            SeedId = GetString(attributes, "seedId")
        };
    }
}
