using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace PowerForge;

public sealed partial class AppStoreConnectClient
{
    /// <summary>
    /// Lists App Information resources for an app.
    /// </summary>
    public Task<AppStoreConnectAppInformationInfo[]> GetAppInfosAsync(
        string appId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appId))
            throw new ArgumentException("App id is required.", nameof(appId));

        var query = new Dictionary<string, string?>
        {
            ["limit"] = ClampLimit(limit).ToString(CultureInfo.InvariantCulture)
        };

        return GetArrayAsync(
            $"apps/{Uri.EscapeDataString(appId.Trim())}/appInfos" + BuildQuery(query),
            ParseAppInformation,
            cancellationToken);
    }

    /// <summary>
    /// Lists localized app-level information for an App Information resource.
    /// </summary>
    public Task<AppStoreConnectAppInfoLocalizationInfo[]> GetAppInfoLocalizationsAsync(
        string appInfoId,
        string? locale = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appInfoId))
            throw new ArgumentException("App information id is required.", nameof(appInfoId));

        var query = new Dictionary<string, string?>
        {
            ["limit"] = ClampLimit(limit).ToString(CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(locale))
            query["filter[locale]"] = locale.Trim();

        return GetArrayAsync(
            $"appInfos/{Uri.EscapeDataString(appInfoId.Trim())}/appInfoLocalizations" + BuildQuery(query),
            ParseAppInfoLocalization,
            cancellationToken);
    }

    /// <summary>
    /// Updates localized app-level App Store information.
    /// Null values are ignored; empty strings are sent to App Store Connect.
    /// </summary>
    public async Task<AppStoreConnectAppInfoLocalizationInfo> UpdateAppInfoLocalizationAsync(
        string appInfoLocalizationId,
        AppStoreConnectAppInfoLocalizationUpdate update,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appInfoLocalizationId))
            throw new ArgumentException("App information localization id is required.", nameof(appInfoLocalizationId));
        if (update is null)
            throw new ArgumentNullException(nameof(update));

        var attributes = BuildAppInfoLocalizationAttributes(update);
        if (attributes.Count == 0)
            throw new ArgumentException("At least one app information metadata field must be supplied.", nameof(update));

        var localizationId = appInfoLocalizationId.Trim();
        var body = new
        {
            data = new
            {
                type = "appInfoLocalizations",
                id = localizationId,
                attributes
            }
        };

        using var doc = await SendJsonAsync(
            new HttpMethod("PATCH"),
            $"appInfoLocalizations/{Uri.EscapeDataString(localizationId)}",
            body,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("App Store Connect API request returned no response body.");
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
            throw new InvalidOperationException("App Store Connect API request returned no data.");

        return ParseAppInfoLocalization(data);
    }

    internal static string[] GetSuppliedAppInfoLocalizationFields(AppStoreConnectAppInfoLocalizationUpdate update)
        => BuildAppInfoLocalizationAttributes(update).Keys.ToArray();

    private static Dictionary<string, object?> BuildAppInfoLocalizationAttributes(AppStoreConnectAppInfoLocalizationUpdate update)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        AddIfNotNull(attributes, "name", update.Name);
        AddIfNotNull(attributes, "subtitle", update.Subtitle);
        AddIfNotNull(attributes, "privacyPolicyUrl", update.PrivacyPolicyUrl);
        AddIfNotNull(attributes, "privacyChoicesUrl", update.PrivacyChoicesUrl);
        AddIfNotNull(attributes, "privacyPolicyText", update.PrivacyPolicyText);
        return attributes;
    }

    private static AppStoreConnectAppInformationInfo ParseAppInformation(JsonElement item)
    {
        var attrs = GetAttributes(item);
        return new AppStoreConnectAppInformationInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            State = GetString(attrs, "state"),
            AppStoreState = GetString(attrs, "appStoreState")
        };
    }

    private static AppStoreConnectAppInfoLocalizationInfo ParseAppInfoLocalization(JsonElement item)
    {
        var attrs = GetAttributes(item);
        return new AppStoreConnectAppInfoLocalizationInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            Locale = GetString(attrs, "locale"),
            Name = GetString(attrs, "name"),
            Subtitle = GetString(attrs, "subtitle"),
            PrivacyPolicyUrl = GetString(attrs, "privacyPolicyUrl"),
            PrivacyChoicesUrl = GetString(attrs, "privacyChoicesUrl"),
            PrivacyPolicyText = GetString(attrs, "privacyPolicyText")
        };
    }
}
