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

/// <summary>Reads and reconciles the zone-level Smart Tiered Cache setting.</summary>
internal static class CloudflareSmartTieredCacheManager
{
    private const string RelativePathSuffix = "cache/tiered_cache_smart_topology_enable";

    internal static CloudflareSmartTieredCacheResult Apply(
        string zoneId,
        string apiToken,
        bool enabled,
        bool dryRun,
        HttpClient? httpClient = null)
    {
        var normalizedZoneId = (zoneId ?? string.Empty).Trim();
        if (normalizedZoneId.Length != 32 || normalizedZoneId.Any(character => !Uri.IsHexDigit(character)))
            return Failure(dryRun, enabled, "Cloudflare zoneId must be a 32-character hexadecimal identifier.");
        if (string.IsNullOrWhiteSpace(apiToken))
            return Failure(dryRun, enabled, "Missing apiToken.");

        var ownsHttpClient = httpClient is null;
        httpClient ??= new HttpClient { BaseAddress = new Uri("https://api.cloudflare.com/client/v4/") };
        try
        {
            var relativePath = $"zones/{normalizedZoneId}/{RelativePathSuffix}";
            var current = ReadCurrent(httpClient, relativePath, apiToken, dryRun, enabled);
            if (!current.Success || current.PreviousEnabled is null)
                return current;

            if (current.PreviousEnabled.Value == enabled)
            {
                return new CloudflareSmartTieredCacheResult
                {
                    Success = true,
                    DryRun = dryRun,
                    Enabled = enabled,
                    PreviousEnabled = enabled,
                    Message = $"Smart Tiered Cache is already {(enabled ? "enabled" : "disabled")}."
                };
            }

            if (dryRun)
            {
                return new CloudflareSmartTieredCacheResult
                {
                    Success = true,
                    DryRun = true,
                    ChangesRequired = true,
                    Enabled = enabled,
                    PreviousEnabled = current.PreviousEnabled,
                    Message = $"Smart Tiered Cache would be {(enabled ? "enabled" : "disabled")}."
                };
            }

            var payload = new JsonObject { ["value"] = enabled ? "on" : "off" };
            var write = CloudflareApiClient.Send(httpClient, HttpMethod.Patch, relativePath, apiToken, payload);
            var writeWasAmbiguous = IsAmbiguousWriteFailure(write);
            if (!write.Success && !writeWasAmbiguous)
            {
                return Failure(
                    false,
                    enabled,
                    $"Smart Tiered Cache update failed: {write.ErrorMessage}",
                    current.PreviousEnabled);
            }

            var verified = ReadCurrent(httpClient, relativePath, apiToken, dryRun: false, enabled);
            if (verified.Success && verified.PreviousEnabled == enabled)
            {
                return new CloudflareSmartTieredCacheResult
                {
                    Success = true,
                    ChangesRequired = true,
                    Changed = true,
                    Enabled = enabled,
                    PreviousEnabled = current.PreviousEnabled,
                    Message = !writeWasAmbiguous
                        ? $"Smart Tiered Cache was {(enabled ? "enabled" : "disabled")}."
                        : $"Smart Tiered Cache reached the requested state after an ambiguous API response."
                };
            }

            if (verified.Success)
            {
                var writeError = write.Success ? "the requested state was not observed" : write.ErrorMessage;
                return Failure(
                    false,
                    enabled,
                    $"Smart Tiered Cache update failed: {writeError}. The previous state remained active.",
                    current.PreviousEnabled);
            }

            var error = write.Success
                ? verified.Message
                : write.ErrorMessage;
            var recovery = RestorePreviousState(
                httpClient,
                relativePath,
                apiToken,
                current.PreviousEnabled.Value);
            return Failure(
                false,
                enabled,
                $"Smart Tiered Cache update failed: {error} {recovery}",
                current.PreviousEnabled);
        }
        finally
        {
            if (ownsHttpClient)
                httpClient.Dispose();
        }
    }

    private static CloudflareSmartTieredCacheResult ReadCurrent(
        HttpClient httpClient,
        string relativePath,
        string apiToken,
        bool dryRun,
        bool desiredEnabled)
    {
        var response = CloudflareApiClient.Send(httpClient, HttpMethod.Get, relativePath, apiToken, body: null);
        if (!response.Success)
            return Failure(dryRun, desiredEnabled, response.ErrorMessage);
        if (response.Result is not { ValueKind: JsonValueKind.Object } result ||
            !result.TryGetProperty("value", out var valueElement) ||
            valueElement.ValueKind != JsonValueKind.String)
            return Failure(dryRun, desiredEnabled, "Cloudflare Smart Tiered Cache response omitted its value.");

        var value = valueElement.GetString();
        if (value is not ("on" or "off"))
            return Failure(dryRun, desiredEnabled, $"Cloudflare Smart Tiered Cache returned an unsupported value '{value}'.");

        var currentEnabled = value == "on";
        if (currentEnabled != desiredEnabled &&
            result.TryGetProperty("editable", out var editableElement) &&
            editableElement.ValueKind == JsonValueKind.False)
        {
            return Failure(
                dryRun,
                desiredEnabled,
                "Cloudflare reports that Smart Tiered Cache is not editable for this zone.",
                currentEnabled);
        }

        return new CloudflareSmartTieredCacheResult
        {
            Success = true,
            DryRun = dryRun,
            Enabled = desiredEnabled,
            PreviousEnabled = currentEnabled,
            Message = $"Smart Tiered Cache is {value}."
        };
    }

    private static CloudflareSmartTieredCacheResult Failure(
        bool dryRun,
        bool enabled,
        string message,
        bool? previousEnabled = null) => new()
    {
        DryRun = dryRun,
        Enabled = enabled,
        PreviousEnabled = previousEnabled,
        Message = message
    };

    private static string RestorePreviousState(
        HttpClient httpClient,
        string relativePath,
        string apiToken,
        bool previousEnabled)
    {
        var payload = new JsonObject { ["value"] = previousEnabled ? "on" : "off" };
        var write = CloudflareApiClient.Send(httpClient, HttpMethod.Patch, relativePath, apiToken, payload);
        if (!write.Success && !IsAmbiguousWriteFailure(write))
            return $"Smart Tiered Cache recovery was incomplete: {write.ErrorMessage}";

        var verified = ReadCurrent(httpClient, relativePath, apiToken, dryRun: false, previousEnabled);
        if (verified.Success && verified.PreviousEnabled == previousEnabled)
            return "The previous Smart Tiered Cache state was restored.";

        var error = write.Success ? verified.Message : write.ErrorMessage;
        return $"Smart Tiered Cache recovery was incomplete: {error}";
    }

    private static bool IsAmbiguousWriteFailure(CloudflareApiResponse response) =>
        !response.Success &&
        (response.TransportError is not null || (int)response.StatusCode >= 500);
}
