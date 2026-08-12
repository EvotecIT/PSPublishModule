using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerForge.Web.Cli;

internal sealed class CloudflareManagedRulesetResult
{
    public bool Success { get; init; }
    public bool ChangesRequired { get; init; }
    public bool Changed { get; init; }
    public int ManagedRuleCount { get; init; }
    public int PreservedRuleCount { get; init; }
    public string Message { get; init; } = string.Empty;
    internal CloudflareManagedRulesetSnapshot? Snapshot { get; init; }
    internal string? AppliedRulesetId { get; init; }
}

internal sealed class CloudflareManagedRulesetSnapshot
{
    internal required string ZoneId { get; init; }
    internal required string Phase { get; init; }
    internal required bool EntryPointExists { get; init; }
    internal required JsonArray ExistingRules { get; init; }
}

internal sealed class CloudflareManagedRulesetRestoreResult
{
    internal bool Success { get; init; }
    internal string Message { get; init; } = string.Empty;
}

/// <summary>Reconciles one PowerForge-owned rule group while preserving unrelated Cloudflare rules.</summary>
internal static class CloudflareManagedRulesetManager
{
    internal static CloudflareManagedRulesetResult Apply(
        string zoneId,
        string apiToken,
        string phase,
        string rulesetName,
        string rulesetDescription,
        string managedPrefix,
        JsonArray managedRules,
        bool dryRun,
        string policyLabel,
        HttpClient httpClient,
        Func<JsonObject, bool>? isLegacyManagedRule = null)
    {
        try
        {
            var entrypoint = $"zones/{Uri.EscapeDataString(zoneId)}/rulesets/phases/{phase}/entrypoint";
            var getResponse = CloudflareApiClient.Send(httpClient, HttpMethod.Get, entrypoint, apiToken, body: null);
            if (getResponse.TransportError is not null)
                return Failure(getResponse.TransportError);

            var entrypointExists = getResponse.StatusCode != HttpStatusCode.NotFound;
            if (entrypointExists && !getResponse.Success)
                return Failure(getResponse.ErrorMessage);

            var existingRules = new JsonArray();
            if (entrypointExists && !TryReadRules(getResponse.Result, out existingRules))
                return Failure("Cloudflare entry-point response did not contain a rules array; refusing to replace the existing ruleset.");
            if (existingRules.Any(rule => rule is not JsonObject))
                return Failure("Cloudflare entry-point response contained a malformed rule; refusing to replace the existing ruleset.");

            var snapshot = new CloudflareManagedRulesetSnapshot
            {
                ZoneId = zoneId.Trim(),
                Phase = phase,
                EntryPointExists = entrypointExists,
                ExistingRules = existingRules.DeepClone().AsArray()
            };

            var existingManaged = existingRules
                .OfType<JsonObject>()
                .Where(rule => IsManagedRule(rule, managedPrefix, isLegacyManagedRule))
                .ToArray();
            CopyManagedRuleIdentity(existingManaged, managedRules);

            var desiredRules = BuildDesiredRuleSequence(existingRules, managedRules, managedPrefix, isLegacyManagedRule, out var preservedCount);
            var changesRequired = !JsonNode.DeepEquals(
                NormalizeRulesForComparison(existingRules),
                NormalizeRulesForComparison(desiredRules));
            var managedCount = managedRules.Count;

            if (!changesRequired)
                return Success(false, false, managedCount, preservedCount, $"Cloudflare {policyLabel} is already current ({managedCount} managed rule(s), {preservedCount} preserved rule(s)).", snapshot);

            if (dryRun)
                return Success(true, false, managedCount, preservedCount, $"Cloudflare {policyLabel} would update {managedCount} managed rule(s) and preserve {preservedCount} unrelated rule(s).", snapshot);

            var updateResponse = entrypointExists
                ? CloudflareApiClient.Send(httpClient, HttpMethod.Put, entrypoint, apiToken, new JsonObject { ["rules"] = desiredRules })
                : CloudflareApiClient.Send(
                    httpClient,
                    HttpMethod.Post,
                    $"zones/{Uri.EscapeDataString(zoneId)}/rulesets",
                    apiToken,
                    new JsonObject
                    {
                        ["name"] = rulesetName,
                        ["description"] = rulesetDescription,
                        ["kind"] = "zone",
                        ["phase"] = phase,
                        ["rules"] = desiredRules
                    });

            if (!updateResponse.Success)
            {
                if (!entrypointExists && updateResponse.TransportError is not null)
                {
                    var reconciliation = CloudflareApiClient.Send(httpClient, HttpMethod.Get, entrypoint, apiToken, body: null);
                    if (reconciliation.Success &&
                        TryReadRules(reconciliation.Result, out var reconciledRules) &&
                        JsonNode.DeepEquals(
                            NormalizeRulesForComparison(reconciledRules),
                            NormalizeRulesForComparison(desiredRules)))
                    {
                        var reconciledRulesetId = TryReadRulesetId(reconciliation.Result);
                        if (!string.IsNullOrWhiteSpace(reconciledRulesetId))
                            return Failure(updateResponse.ErrorMessage, snapshot, reconciledRulesetId);
                    }

                    if (reconciliation.StatusCode == HttpStatusCode.NotFound)
                        return Failure(updateResponse.ErrorMessage);

                    return Failure(
                        $"{updateResponse.ErrorMessage} The newly created ruleset could not be identified safely for rollback.",
                        snapshot);
                }

                return Failure(updateResponse.ErrorMessage, entrypointExists ? snapshot : null);
            }

            return Success(
                true,
                true,
                managedCount,
                preservedCount,
                $"Applied {managedCount} Cloudflare {policyLabel} rule(s); preserved {preservedCount} unrelated rule(s).",
                snapshot,
                entrypointExists ? null : TryReadRulesetId(updateResponse.Result));
        }
        catch (Exception ex)
        {
            return Failure($"Cloudflare {policyLabel} failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    internal static CloudflareManagedRulesetRestoreResult Restore(
        CloudflareManagedRulesetSnapshot snapshot,
        string? appliedRulesetId,
        string apiToken,
        HttpClient httpClient)
    {
        try
        {
            CloudflareApiResponse response;
            if (snapshot.EntryPointExists)
            {
                var restoreRules = new JsonArray();
                foreach (var rule in snapshot.ExistingRules.OfType<JsonObject>())
                    restoreRules.Add(PrepareRuleForUpdate(rule));
                var entrypoint = $"zones/{Uri.EscapeDataString(snapshot.ZoneId)}/rulesets/phases/{snapshot.Phase}/entrypoint";
                response = CloudflareApiClient.Send(
                    httpClient,
                    HttpMethod.Put,
                    entrypoint,
                    apiToken,
                    new JsonObject { ["rules"] = restoreRules });
            }
            else
            {
                if (string.IsNullOrWhiteSpace(appliedRulesetId))
                {
                    return new CloudflareManagedRulesetRestoreResult
                    {
                        Message = "Cannot remove the newly created Cloudflare ruleset because its identifier was not returned."
                    };
                }

                response = CloudflareApiClient.Send(
                    httpClient,
                    HttpMethod.Delete,
                    $"zones/{Uri.EscapeDataString(snapshot.ZoneId)}/rulesets/{Uri.EscapeDataString(appliedRulesetId)}",
                    apiToken,
                    body: null);
            }

            return response.Success
                ? new CloudflareManagedRulesetRestoreResult { Success = true, Message = "Restored the previous Cloudflare ruleset state." }
                : new CloudflareManagedRulesetRestoreResult { Message = $"Cloudflare ruleset rollback failed: {response.ErrorMessage}" };
        }
        catch (Exception ex)
        {
            return new CloudflareManagedRulesetRestoreResult
            {
                Message = $"Cloudflare ruleset rollback failed: {ex.GetType().Name}: {ex.Message}"
            };
        }
    }

    private static void CopyManagedRuleIdentity(IEnumerable<JsonObject> existingRules, JsonArray desiredRules)
    {
        var byDescription = existingRules
            .Where(rule => rule["description"] is not null)
            .GroupBy(rule => rule["description"]!.GetValue<string>(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var desired in desiredRules.OfType<JsonObject>())
        {
            var description = desired["description"]?.GetValue<string>() ?? string.Empty;
            if (!byDescription.TryGetValue(description, out var existing))
                continue;

            foreach (var identityName in new[] { "id", "ref" })
            {
                if (existing[identityName] is not null)
                    desired[identityName] = existing[identityName]!.DeepClone();
            }
        }
    }

    private static JsonArray BuildDesiredRuleSequence(
        JsonArray existingRules,
        JsonArray managedRules,
        string managedPrefix,
        Func<JsonObject, bool>? isLegacyManagedRule,
        out int preservedCount)
    {
        var desiredByDescription = managedRules
            .OfType<JsonObject>()
            .ToDictionary(rule => rule["description"]?.GetValue<string>() ?? string.Empty, rule => rule, StringComparer.Ordinal);
        var emittedDescriptions = new HashSet<string>(StringComparer.Ordinal);
        var lastManagedIndex = -1;
        for (var index = 0; index < existingRules.Count; index++)
        {
            var existing = existingRules[index]!.AsObject();
            if (IsManagedRule(existing, managedPrefix, isLegacyManagedRule))
                lastManagedIndex = index;
        }

        var desiredRules = new JsonArray();
        if (lastManagedIndex < 0)
            AddMissingManagedRules(desiredRules, managedRules, emittedDescriptions);

        preservedCount = 0;
        for (var index = 0; index < existingRules.Count; index++)
        {
            var existing = existingRules[index]!.AsObject();
            var description = existing["description"]?.GetValue<string>() ?? string.Empty;
            if (!IsManagedRule(existing, managedPrefix, isLegacyManagedRule))
            {
                desiredRules.Add(PrepareRuleForUpdate(existing));
                preservedCount++;
                continue;
            }

            if (desiredByDescription.TryGetValue(description, out var desired) && emittedDescriptions.Add(description))
                desiredRules.Add(desired.DeepClone());

            if (index == lastManagedIndex)
                AddMissingManagedRules(desiredRules, managedRules, emittedDescriptions);
        }

        return desiredRules;
    }

    private static bool IsManagedRule(JsonObject rule, string managedPrefix, Func<JsonObject, bool>? isLegacyManagedRule)
    {
        var description = rule["description"]?.GetValue<string>() ?? string.Empty;
        return description.StartsWith(managedPrefix, StringComparison.Ordinal) ||
               isLegacyManagedRule?.Invoke(rule) == true;
    }

    private static void AddMissingManagedRules(JsonArray destination, JsonArray managedRules, HashSet<string> emittedDescriptions)
    {
        foreach (var managed in managedRules.OfType<JsonObject>())
        {
            var description = managed["description"]?.GetValue<string>() ?? string.Empty;
            if (emittedDescriptions.Add(description))
                destination.Add(managed.DeepClone());
        }
    }

    private static bool TryReadRules(JsonElement? result, out JsonArray rules)
    {
        rules = new JsonArray();
        if (result is null || result.Value.ValueKind != JsonValueKind.Object ||
            !result.Value.TryGetProperty("rules", out var rulesElement) ||
            rulesElement.ValueKind != JsonValueKind.Array)
            return false;

        rules = JsonNode.Parse(rulesElement.GetRawText()) as JsonArray ?? new JsonArray();
        return true;
    }

    private static JsonArray NormalizeRulesForComparison(JsonArray rules)
    {
        var normalized = new JsonArray();
        foreach (var rule in rules.OfType<JsonObject>())
        {
            var clone = PrepareRuleForUpdate(rule);
            clone.Remove("id");
            clone.Remove("ref");
            normalized.Add(clone);
        }
        return normalized;
    }

    private static JsonObject PrepareRuleForUpdate(JsonObject source)
    {
        var clone = source.DeepClone().AsObject();
        clone.Remove("version");
        clone.Remove("last_updated");
        return clone;
    }

    private static string? TryReadRulesetId(JsonElement? result)
    {
        if (result is null || result.Value.ValueKind != JsonValueKind.Object ||
            !result.Value.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String)
            return null;
        return id.GetString();
    }

    private static CloudflareManagedRulesetResult Failure(
        string message,
        CloudflareManagedRulesetSnapshot? snapshot = null,
        string? appliedRulesetId = null) => new()
    {
        Message = message,
        Snapshot = snapshot,
        AppliedRulesetId = appliedRulesetId
    };

    private static CloudflareManagedRulesetResult Success(
        bool changesRequired,
        bool changed,
        int managedCount,
        int preservedCount,
        string message,
        CloudflareManagedRulesetSnapshot snapshot,
        string? appliedRulesetId = null) => new()
    {
        Success = true,
        ChangesRequired = changesRequired,
        Changed = changed,
        ManagedRuleCount = managedCount,
        PreservedRuleCount = preservedCount,
        Message = message,
        Snapshot = snapshot,
        AppliedRulesetId = appliedRulesetId
    };
}
