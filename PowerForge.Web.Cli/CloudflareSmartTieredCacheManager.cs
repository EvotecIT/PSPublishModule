using System;
using System.Collections.Generic;
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
    internal bool? PreviousTieredCacheEnabled { get; init; }
    internal string Message { get; init; } = string.Empty;
}

/// <summary>Reconciles Smart Topology together with its zone-level Tiered Cache prerequisite.</summary>
internal static class CloudflareSmartTieredCacheManager
{
    private const string ParentSuffix = "argo/tiered_caching";
    private const string TopologySuffix = "cache/tiered_cache_smart_topology_enable";

    internal static CloudflareSmartTieredCacheResult Apply(
        string zoneId, string apiToken, bool enabled, bool dryRun, HttpClient? httpClient = null) =>
        Reconcile(zoneId, apiToken, enabled, parentEnabled: enabled ? true : null, dryRun, restoring: false, httpClient);

    /// <summary>Restores both settings, including a parent disabled before the site-policy transaction.</summary>
    internal static CloudflareSmartTieredCacheResult Restore(
        string zoneId, string apiToken, CloudflareSmartTieredCacheResult applied, HttpClient httpClient)
    {
        if (applied.PreviousEnabled is null || applied.PreviousTieredCacheEnabled is null)
            return Result(false, false, false, false, applied.Enabled, null, null, "The previous Tiered Cache snapshot is unavailable.");
        return Reconcile(zoneId, apiToken, applied.PreviousEnabled.Value, applied.PreviousTieredCacheEnabled,
            dryRun: false, restoring: true, httpClient);
    }

    private static CloudflareSmartTieredCacheResult Reconcile(
        string zoneId, string apiToken, bool enabled, bool? parentEnabled, bool dryRun, bool restoring, HttpClient? httpClient)
    {
        var normalizedZoneId = (zoneId ?? string.Empty).Trim();
        if (normalizedZoneId.Length != 32 || normalizedZoneId.Any(character => !Uri.IsHexDigit(character)))
            return Result(false, dryRun, false, false, enabled, null, null, "Cloudflare zoneId must be a 32-character hexadecimal identifier.");
        if (string.IsNullOrWhiteSpace(apiToken))
            return Result(false, dryRun, false, false, enabled, null, null, "Missing apiToken.");

        var ownsHttpClient = httpClient is null;
        httpClient ??= new HttpClient { BaseAddress = new Uri("https://api.cloudflare.com/client/v4/") };
        try
        {
            var parent = Read(httpClient, $"zones/{normalizedZoneId}/{ParentSuffix}", "Tiered Cache", apiToken);
            if (parent.Setting is null)
                return Result(false, dryRun, false, false, enabled, null, null, parent.Error);
            var topology = Read(httpClient, $"zones/{normalizedZoneId}/{TopologySuffix}", "Smart Tiered Cache", apiToken);
            if (topology.Setting is null)
                return Result(false, dryRun, false, false, enabled, null, parent.Setting.Enabled, topology.Error);

            var targetParent = parentEnabled ?? parent.Setting.Enabled;
            var targets = targetParent
                ? new[] { (parent.Setting, targetParent), (topology.Setting, enabled) }
                : new[] { (topology.Setting, enabled), (parent.Setting, targetParent) };
            var changes = targets.Where(target => target.Setting.Enabled != target.Item2).ToArray();
            var locked = changes.FirstOrDefault(target => !target.Setting.Editable).Setting;
            if (locked is not null)
                return Finish(false, false, $"Cloudflare reports that {locked.Name} is not editable for this zone.");
            if (changes.Length == 0)
                return Finish(true, false, $"Smart Tiered Cache is already {(enabled ? "enabled" : "disabled")}.");
            if (dryRun)
                return Finish(true, false, $"Smart Tiered Cache would be {(enabled ? "enabled" : "disabled")}; its Tiered Cache prerequisite is included.");

            var attempted = new List<Setting>();
            var failures = new List<string>();
            var ambiguous = false;
            foreach (var (setting, desired) in changes)
            {
                var write = WriteAndVerify(httpClient, setting, desired, apiToken);
                ambiguous |= write.Ambiguous;
                if (write.MayHaveChanged)
                    attempted.Add(setting);
                if (write.Success)
                    continue;
                failures.Add(write.Error);
                if (restoring)
                    continue;

                var recoveryErrors = new List<string>();
                foreach (var previous in attempted.AsEnumerable().Reverse())
                {
                    var recovery = WriteAndVerify(httpClient, previous, previous.Enabled, apiToken);
                    if (!recovery.Success)
                        recoveryErrors.Add(recovery.Error);
                }
                var recoveryMessage = recoveryErrors.Count > 0
                    ? $"Tiered Cache recovery was incomplete: {string.Join(" ", recoveryErrors)}"
                    : attempted.Count > 0
                        ? "The previous Smart Tiered Cache state was restored, including the Tiered Cache prerequisite."
                        : "The previous state remained active.";
                return Finish(false, false, $"Smart Tiered Cache update failed: {write.Error} {recoveryMessage}");
            }

            if (failures.Count > 0)
                return Finish(false, attempted.Count > 0, $"Tiered Cache recovery was incomplete: {string.Join(" ", failures)}");
            return Finish(true, true, ambiguous
                ? "Smart Tiered Cache reached the requested state after an ambiguous API response."
                : $"Smart Tiered Cache was {(enabled ? "enabled" : "disabled")}; its Tiered Cache prerequisite was reconciled.");

            CloudflareSmartTieredCacheResult Finish(bool success, bool changed, string message) =>
                Result(success, dryRun, changes.Length > 0, changed, enabled,
                    topology.Setting.Enabled, parent.Setting.Enabled, message);
        }
        finally
        {
            if (ownsHttpClient)
                httpClient.Dispose();
        }
    }

    private static (Setting? Setting, string Error) Read(HttpClient client, string path, string name, string token)
    {
        var response = CloudflareApiClient.Send(client, HttpMethod.Get, path, token, body: null);
        if (!response.Success)
            return (null, $"{name}: {response.ErrorMessage}");
        if (response.Result is not { ValueKind: JsonValueKind.Object } result ||
            !result.TryGetProperty("value", out var valueElement) || valueElement.ValueKind != JsonValueKind.String)
            return (null, $"Cloudflare {name} response omitted its value.");
        var value = valueElement.GetString();
        if (value is not ("on" or "off"))
            return (null, $"Cloudflare {name} returned an unsupported value '{value}'.");
        var editable = !result.TryGetProperty("editable", out var editableElement) || editableElement.ValueKind != JsonValueKind.False;
        return (new Setting(path, name, value == "on", editable), string.Empty);
    }

    private static (bool Success, bool MayHaveChanged, bool Ambiguous, string Error) WriteAndVerify(
        HttpClient client, Setting setting, bool enabled, string token)
    {
        var write = CloudflareApiClient.Send(client, HttpMethod.Patch, setting.Path, token,
            new JsonObject { ["value"] = enabled ? "on" : "off" });
        var ambiguous = !write.Success && (write.TransportError is not null || (int)write.StatusCode >= 500);
        if (!write.Success && !ambiguous)
            return (false, false, false, $"{setting.Name}: {write.ErrorMessage}");

        var verified = Read(client, setting.Path, setting.Name, token);
        if (verified.Setting?.Enabled == enabled)
            return (true, true, ambiguous, string.Empty);
        var mayHaveChanged = verified.Setting is null || verified.Setting.Enabled != setting.Enabled;
        var error = verified.Setting is null ? verified.Error : "the requested state was not observed";
        return (false, mayHaveChanged, ambiguous, $"{setting.Name}: {(write.Success ? error : write.ErrorMessage)}");
    }

    private static CloudflareSmartTieredCacheResult Result(
        bool success, bool dryRun, bool changesRequired, bool changed, bool enabled,
        bool? previousEnabled, bool? previousParent, string message) => new()
    {
        Success = success,
        DryRun = dryRun,
        ChangesRequired = changesRequired,
        Changed = changed,
        Enabled = enabled,
        PreviousEnabled = previousEnabled,
        PreviousTieredCacheEnabled = previousParent,
        Message = message
    };

    private sealed record Setting(string Path, string Name, bool Enabled, bool Editable);
}
