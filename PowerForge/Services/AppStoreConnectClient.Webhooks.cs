using System.Net.Http;
using System.Text.Json;

namespace PowerForge;

public sealed partial class AppStoreConnectClient
{
    /// <summary>Lists webhook configurations for an app.</summary>
    public Task<AppStoreConnectWebhookInfo[]> GetWebhooksAsync(
        string appId,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appId))
            throw new ArgumentException("App id is required.", nameof(appId));
        return GetArrayAsync(
            $"apps/{Uri.EscapeDataString(appId.Trim())}/webhooks?limit={ClampLimit(limit)}",
            ParseWebhook,
            cancellationToken,
            maxResults: ClampLimit(limit));
    }

    /// <summary>Creates a webhook configuration for an app.</summary>
    public Task<AppStoreConnectWebhookInfo> CreateWebhookAsync(
        AppStoreConnectWebhookSpec spec,
        CancellationToken cancellationToken = default)
    {
        ValidateWebhookSpec(spec, requireAppId: true);
        var body = new
        {
            data = new
            {
                type = "webhooks",
                attributes = new
                {
                    enabled = spec.Enabled,
                    eventTypes = spec.EventTypes,
                    name = spec.Name.Trim(),
                    secret = spec.Secret,
                    url = spec.Url.Trim()
                },
                relationships = new
                {
                    app = new { data = new { type = "apps", id = spec.AppId.Trim() } }
                }
            }
        };
        return PostSingleAsync("webhooks", body, ParseWebhook, cancellationToken);
    }

    /// <summary>Updates an existing webhook configuration.</summary>
    public Task<AppStoreConnectWebhookInfo> UpdateWebhookAsync(
        string webhookId,
        AppStoreConnectWebhookSpec spec,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(webhookId))
            throw new ArgumentException("Webhook id is required.", nameof(webhookId));
        ValidateWebhookSpec(spec, requireAppId: false);
        var body = new
        {
            data = new
            {
                type = "webhooks",
                id = webhookId.Trim(),
                attributes = new
                {
                    enabled = spec.Enabled,
                    eventTypes = spec.EventTypes,
                    name = spec.Name.Trim(),
                    secret = spec.Secret,
                    url = spec.Url.Trim()
                }
            }
        };
        return PatchSingleAsync($"webhooks/{Uri.EscapeDataString(webhookId.Trim())}", body, ParseWebhook, cancellationToken);
    }

    /// <summary>Sends an App Store Connect test delivery to a webhook.</summary>
    public async Task PingWebhookAsync(string webhookId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(webhookId))
            throw new ArgumentException("Webhook id is required.", nameof(webhookId));
        var body = new
        {
            data = new
            {
                type = "webhookPings",
                relationships = new
                {
                    webhook = new { data = new { type = "webhooks", id = webhookId.Trim() } }
                }
            }
        };
        using var _ = await SendJsonAsync(new HttpMethod("POST"), "webhookPings", body, cancellationToken).ConfigureAwait(false);
    }

    private static AppStoreConnectWebhookInfo ParseWebhook(JsonElement item)
    {
        var attributes = GetAttributes(item);
        var eventTypes = attributes.ValueKind == JsonValueKind.Object &&
                         attributes.TryGetProperty("eventTypes", out var events) &&
                         events.ValueKind == JsonValueKind.Array
            ? events.EnumerateArray().Select(static value => value.GetString() ?? string.Empty)
                .Where(static value => !string.IsNullOrWhiteSpace(value)).ToArray()
            : Array.Empty<string>();
        return new AppStoreConnectWebhookInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            Name = GetString(attributes, "name"),
            Url = GetString(attributes, "url"),
            Enabled = GetBool(attributes, "enabled"),
            EventTypes = eventTypes
        };
    }

    private static void ValidateWebhookSpec(AppStoreConnectWebhookSpec spec, bool requireAppId)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));
        if (requireAppId && string.IsNullOrWhiteSpace(spec.AppId))
            throw new ArgumentException("AppId is required.", nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.Name))
            throw new ArgumentException("Name is required.", nameof(spec));
        if (!Uri.TryCreate(spec.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Webhook Url must be an absolute HTTPS URL.", nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.Secret) || spec.Secret.Length < 16)
            throw new ArgumentException("Webhook Secret must contain at least 16 characters.", nameof(spec));
        if (spec.EventTypes is null || spec.EventTypes.Length == 0 || spec.EventTypes.Any(static value => string.IsNullOrWhiteSpace(value)))
            throw new ArgumentException("At least one non-empty webhook EventType is required.", nameof(spec));
    }
}
