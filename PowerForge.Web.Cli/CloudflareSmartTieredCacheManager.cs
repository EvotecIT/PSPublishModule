using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerForge.Web.Cli;

internal sealed class CloudflareSmartTieredCacheResult
{
    internal bool Success { get; init; }
    internal bool DryRun { get; init; }
    internal bool ChangesRequired { get; init; }
    internal bool Changed { get; init; }
    internal bool Enabled { get; init; }
    internal bool? PreviousEnabled { get; init; }
    internal string Message { get; init; } = string.Empty;
}

/// <summary>Reconciles Smart Tiered Cache through Cloudflare's current Smart Shield API.</summary>
internal static class CloudflareSmartTieredCacheManager
{
    internal static CloudflareSmartTieredCacheResult Apply(
        string zoneId, string apiToken, bool enabled, bool dryRun, HttpClient? httpClient = null) =>
        Reconcile(zoneId, apiToken, enabled, dryRun, restoring: false, httpClient);

    private static CloudflareSmartTieredCacheResult Reconcile(
        string zoneId, string apiToken, bool enabled, bool dryRun, bool restoring, HttpClient? httpClient)
    {
        var normalizedZoneId = (zoneId ?? string.Empty).Trim();
        if (normalizedZoneId.Length != 32 || normalizedZoneId.Any(character => !Uri.IsHexDigit(character)))
            return Result(false, false, null, "Cloudflare zoneId must be a 32-character hexadecimal identifier.");
        if (string.IsNullOrWhiteSpace(apiToken))
            return Result(false, false, null, "Missing apiToken.");

        var ownsHttpClient = httpClient is null;
        httpClient ??= new HttpClient { BaseAddress = new Uri("https://api.cloudflare.com/client/v4/") };
        try
        {
            var path = $"zones/{normalizedZoneId}/smart_shield";
            var current = Read(httpClient, path, apiToken);
            if (current.Setting is null)
                return Result(false, false, null, current.Error);
            var previous = current.Setting.Enabled;
            if (previous == enabled)
                return Result(true, false, previous, $"Smart Tiered Cache is already {(enabled ? "enabled" : "disabled")}.");
            if (!current.Setting.Editable)
                return Result(false, false, previous, "Cloudflare reports that Smart Tiered Cache is not editable for this zone.");
            if (dryRun)
                return Result(true, false, previous, $"Smart Tiered Cache would be {(enabled ? "enabled" : "disabled")}.");

            var write = WriteAndVerify(httpClient, path, enabled, previous, apiToken);
            if (write.Success)
                return Result(true, true, previous, write.Ambiguous
                    ? "Smart Tiered Cache reached the requested state after an ambiguous API response."
                    : $"Smart Tiered Cache was {(enabled ? "enabled" : "disabled")}.");
            // A rollback must remain directed toward the original snapshot. A failed
            // verification cannot make the failed transaction's value a recovery target.
            if (restoring)
                return Result(false, false, previous, $"Smart Tiered Cache recovery was incomplete: {write.Error}");
            if (!write.MayHaveChanged)
                return Result(false, false, previous, $"Smart Tiered Cache update failed: {write.Error} The previous state remained active.");

            var recovery = WriteAndVerify(httpClient, path, previous, previous, apiToken);
            return Result(false, false, previous, recovery.Success
                ? $"Smart Tiered Cache update failed: {write.Error} The previous Smart Tiered Cache state was restored."
                : $"Smart Tiered Cache update failed: {write.Error} Recovery was incomplete: {recovery.Error}");
        }
        finally
        {
            if (ownsHttpClient)
                httpClient.Dispose();
        }

        CloudflareSmartTieredCacheResult Result(bool success, bool changed, bool? previous, string message) => new()
        {
            Success = success,
            DryRun = dryRun,
            ChangesRequired = previous.HasValue && previous.Value != enabled,
            Changed = changed,
            Enabled = enabled,
            PreviousEnabled = previous,
            Message = message
        };
    }

    /// <summary>Restores only the Smart Tiered Cache value changed by this site-policy transaction.</summary>
    internal static CloudflareSmartTieredCacheResult Restore(
        string zoneId, string apiToken, CloudflareSmartTieredCacheResult applied, HttpClient httpClient) =>
        applied.PreviousEnabled.HasValue
            ? Reconcile(zoneId, apiToken, applied.PreviousEnabled.Value, dryRun: false, restoring: true, httpClient)
            : new CloudflareSmartTieredCacheResult { Message = "The previous Smart Tiered Cache snapshot is unavailable." };

    private static (Setting? Setting, string Error) Read(HttpClient client, string path, string token)
    {
        var response = CloudflareApiClient.Send(client, HttpMethod.Get, path, token, body: null);
        if (!response.Success)
            return (null, $"Smart Tiered Cache: {response.ErrorMessage}");
        if (response.Result is not { ValueKind: JsonValueKind.Object } result ||
            !result.TryGetProperty("smart_tiered_cache", out var setting) || setting.ValueKind != JsonValueKind.Object ||
            !setting.TryGetProperty("value", out var valueElement) || valueElement.ValueKind != JsonValueKind.String)
            return (null, "Cloudflare Smart Shield response omitted its Smart Tiered Cache value.");
        var value = valueElement.GetString();
        if (value is not ("on" or "off"))
            return (null, $"Cloudflare Smart Tiered Cache returned an unsupported value '{value}'.");
        var editable = !setting.TryGetProperty("editable", out var editableElement) || editableElement.ValueKind != JsonValueKind.False;
        return (new Setting(value == "on", editable), string.Empty);
    }

    private static (bool Success, bool MayHaveChanged, bool Ambiguous, string Error) WriteAndVerify(
        HttpClient client, string path, bool enabled, bool previous, string token)
    {
        var write = CloudflareApiClient.Send(client, HttpMethod.Patch, path, token,
            new JsonObject { ["smart_tiered_cache"] = new JsonObject { ["value"] = enabled ? "on" : "off" } });
        var ambiguous = !write.Success && (write.TransportError is not null || (int)write.StatusCode >= 500);
        if (!write.Success && !ambiguous)
            return (false, false, false, write.ErrorMessage);

        var verified = Read(client, path, token);
        if (verified.Setting?.Enabled == enabled)
            return (true, true, ambiguous, string.Empty);
        var mayHaveChanged = verified.Setting is null || verified.Setting.Enabled != previous;
        var error = verified.Setting is null ? verified.Error : "the requested state was not observed";
        return (false, mayHaveChanged, ambiguous, write.Success ? error : write.ErrorMessage);
    }

    private sealed record Setting(bool Enabled, bool Editable);
}
