using System.Net.Http;
using System.Text.Json;

namespace PowerForge;

public sealed partial class AppStoreConnectClient
{
    /// <summary>
    /// Updates localized App Store version metadata.
    /// Null values are ignored; empty strings are sent to App Store Connect.
    /// </summary>
    public async Task<AppStoreConnectVersionLocalizationInfo> UpdateVersionLocalizationAsync(
        string versionLocalizationId,
        AppStoreConnectVersionLocalizationUpdate update,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(versionLocalizationId))
            throw new ArgumentException("Version localization id is required.", nameof(versionLocalizationId));
        if (update is null)
            throw new ArgumentNullException(nameof(update));

        var attributes = BuildLocalizationAttributes(update);
        if (attributes.Count == 0)
            throw new ArgumentException("At least one metadata field must be supplied.", nameof(update));

        var body = new
        {
            data = new
            {
                type = "appStoreVersionLocalizations",
                id = versionLocalizationId.Trim(),
                attributes
            }
        };

        using var doc = await SendJsonAsync(
            new HttpMethod("PATCH"),
            $"appStoreVersionLocalizations/{Uri.EscapeDataString(versionLocalizationId.Trim())}",
            body,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("App Store Connect API request returned no response body.");
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
            throw new InvalidOperationException("App Store Connect API request returned no data.");

        return ParseVersionLocalization(data);
    }

    /// <summary>
    /// Creates a localization under the specified App Store version resource.
    /// Only supplied metadata fields are sent; the locale identifies the new localization.
    /// </summary>
    public async Task<AppStoreConnectVersionLocalizationInfo> CreateVersionLocalizationAsync(
        string parentId,
        string locale,
        AppStoreConnectVersionLocalizationUpdate metadata,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(parentId))
            throw new ArgumentException("Parent resource id is required.", nameof(parentId));
        if (string.IsNullOrWhiteSpace(locale))
            throw new ArgumentException("Locale is required.", nameof(locale));
        if (metadata is null)
            throw new ArgumentNullException(nameof(metadata));

        var attributes = BuildLocalizationAttributes(metadata);
        attributes.Add("locale", locale.Trim());
        var body = new
        {
            data = new
            {
                type = "appStoreVersionLocalizations",
                attributes,
                relationships = new
                {
                    appStoreVersion = new { data = new { type = "appStoreVersions", id = parentId.Trim() } }
                }
            }
        };
        using var doc = await SendJsonAsync(HttpMethod.Post, "appStoreVersionLocalizations", body, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("App Store Connect API request returned no response body.");
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
            throw new InvalidOperationException("App Store Connect API request returned no data.");
        return ParseVersionLocalization(data);
    }

    internal static string[] GetSuppliedLocalizationFields(AppStoreConnectVersionLocalizationUpdate update)
        => BuildLocalizationAttributes(update).Keys.ToArray();

    private static Dictionary<string, object?> BuildLocalizationAttributes(AppStoreConnectVersionLocalizationUpdate update)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        AddIfNotNull(attributes, "description", update.Description);
        AddIfNotNull(attributes, "keywords", update.Keywords);
        AddIfNotNull(attributes, "marketingUrl", update.MarketingUrl);
        AddIfNotNull(attributes, "promotionalText", update.PromotionalText);
        AddIfNotNull(attributes, "supportUrl", update.SupportUrl);
        AddIfNotNull(attributes, "whatsNew", update.WhatsNew);
        return attributes;
    }

    private static void AddIfNotNull(Dictionary<string, object?> attributes, string name, string? value)
    {
        if (value is not null)
            attributes[name] = value;
    }
}
