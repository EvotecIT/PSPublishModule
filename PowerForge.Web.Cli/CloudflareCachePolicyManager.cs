using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerForge.Web.Cli;

internal sealed class CloudflareCachePolicyApplyResult
{
    public bool Success { get; init; }
    public bool DryRun { get; init; }
    public bool ChangesRequired { get; init; }
    public bool Changed { get; init; }
    public string Hostname { get; init; } = string.Empty;
    public string PolicyName { get; init; } = string.Empty;
    public int ManagedRuleCount { get; init; }
    public int PreservedRuleCount { get; init; }
    public string Message { get; init; } = string.Empty;
}

internal static class CloudflareCachePolicyManager
{
    private const string CachePhase = "http_request_cache_settings";

    internal static CloudflareCachePolicyApplyResult Apply(
        string zoneId,
        string apiToken,
        string hostname,
        string policyName,
        IReadOnlyCollection<string>? htmlPaths,
        bool dryRun,
        WebConsoleLogger? logger,
        HttpClient? httpClient = null,
        string? basePath = null)
    {
        if (string.IsNullOrWhiteSpace(zoneId))
            return Failure("Missing zoneId.", hostname, policyName, dryRun);
        if (string.IsNullOrWhiteSpace(apiToken))
            return Failure("Missing apiToken.", hostname, policyName, dryRun);

        var normalizedZoneId = zoneId.Trim();
        if (normalizedZoneId.Length != 32 || normalizedZoneId.Any(character => !Uri.IsHexDigit(character)))
            return Failure("Cloudflare zoneId must be a 32-character hexadecimal identifier.", hostname, policyName, dryRun);

        JsonArray managedRules;
        try
        {
            hostname = CloudflareCachePolicyBuilder.NormalizeHostname(hostname);
            policyName = CloudflareCachePolicyBuilder.NormalizePolicyName(policyName, hostname);
            managedRules = CloudflareCachePolicyBuilder.BuildManagedRules(hostname, policyName, htmlPaths, basePath);
        }
        catch (ArgumentException ex)
        {
            return Failure(ex.Message, hostname, policyName, dryRun);
        }

        var managedPrefix = $"PowerForge {policyName}:";
        var ownsHttpClient = httpClient is null;
        httpClient ??= new HttpClient { BaseAddress = new Uri("https://api.cloudflare.com/client/v4/") };

        try
        {
            var entrypoint = $"zones/{Uri.EscapeDataString(normalizedZoneId)}/rulesets/phases/{CachePhase}/entrypoint";
            var getResponse = CloudflareApiClient.Send(httpClient, HttpMethod.Get, entrypoint, apiToken, body: null);
            if (getResponse.TransportError is not null)
                return Failure(getResponse.TransportError, hostname, policyName, dryRun);

            var entrypointExists = getResponse.StatusCode != HttpStatusCode.NotFound;
            if (entrypointExists && !getResponse.Success)
                return Failure(getResponse.ErrorMessage, hostname, policyName, dryRun);

            var existingRules = new JsonArray();
            if (entrypointExists && !TryReadRules(getResponse.Result, out existingRules))
            {
                return Failure(
                    "Cloudflare entry-point response did not contain a rules array; refusing to replace the existing ruleset.",
                    hostname,
                    policyName,
                    dryRun);
            }
            if (existingRules.Any(rule => rule is not JsonObject))
            {
                return Failure(
                    "Cloudflare entry-point response contained a malformed rule; refusing to replace the existing ruleset.",
                    hostname,
                    policyName,
                    dryRun);
            }
            var existingManaged = new List<JsonObject>();

            foreach (var ruleNode in existingRules)
            {
                if (ruleNode is not JsonObject rule)
                    continue;

                var description = rule["description"]?.GetValue<string>() ?? string.Empty;
                if (description.StartsWith(managedPrefix, StringComparison.Ordinal))
                    existingManaged.Add(rule);
            }

            CopyManagedRuleIdentity(existingManaged, managedRules);
            var desiredRules = BuildDesiredRuleSequence(existingRules, managedRules, managedPrefix, out var preservedCount);

            var normalizedCurrent = NormalizeRulesForComparison(existingRules);
            var normalizedDesired = NormalizeRulesForComparison(desiredRules);
            var changesRequired = !JsonNode.DeepEquals(normalizedCurrent, normalizedDesired);
            var managedCount = managedRules.Count;

            if (!changesRequired)
            {
                var unchanged = $"Cloudflare cache policy is already current for {hostname} ({managedCount} managed rule(s), {preservedCount} preserved rule(s)).";
                logger?.Info(unchanged);
                return SuccessfulResult(hostname, policyName, dryRun, changesRequired: false, changed: false, managedCount, preservedCount, unchanged);
            }

            if (dryRun)
            {
                var preview = $"Cloudflare cache policy would update {managedCount} managed rule(s) for {hostname} and preserve {preservedCount} unrelated rule(s).";
                logger?.Info(preview);
                return SuccessfulResult(hostname, policyName, dryRun: true, changesRequired: true, changed: false, managedCount, preservedCount, preview);
            }

            var updateResponse = entrypointExists
                ? CloudflareApiClient.Send(
                    httpClient,
                    HttpMethod.Put,
                    entrypoint,
                    apiToken,
                    new JsonObject { ["rules"] = desiredRules })
                : CloudflareApiClient.Send(
                    httpClient,
                    HttpMethod.Post,
                    $"zones/{Uri.EscapeDataString(normalizedZoneId)}/rulesets",
                    apiToken,
                    new JsonObject
                    {
                        ["name"] = "PowerForge cache policy",
                        ["description"] = "PowerForge-managed cache policy",
                        ["kind"] = "zone",
                        ["phase"] = CachePhase,
                        ["rules"] = desiredRules
                    });

            if (!updateResponse.Success)
                return Failure(updateResponse.ErrorMessage, hostname, policyName, dryRun);

            var message = $"Applied {managedCount} Cloudflare cache rule(s) for {hostname}; preserved {preservedCount} unrelated rule(s).";
            logger?.Info(message);
            return SuccessfulResult(hostname, policyName, dryRun: false, changesRequired: true, changed: true, managedCount, preservedCount, message);
        }
        catch (Exception ex)
        {
            return Failure($"Cloudflare cache policy failed: {ex.GetType().Name}: {ex.Message}", hostname, policyName, dryRun);
        }
        finally
        {
            if (ownsHttpClient)
                httpClient.Dispose();
        }
    }

    private static void CopyManagedRuleIdentity(IEnumerable<JsonObject> existingRules, JsonArray desiredRules)
    {
        var byDescription = existingRules
            .Where(rule => rule["description"] is not null)
            .GroupBy(rule => rule["description"]!.GetValue<string>(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var desiredNode in desiredRules)
        {
            if (desiredNode is not JsonObject desired)
                continue;
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
        out int preservedCount)
    {
        var desiredByDescription = managedRules
            .OfType<JsonObject>()
            .ToDictionary(
                rule => rule["description"]?.GetValue<string>() ?? string.Empty,
                rule => rule,
                StringComparer.Ordinal);
        var emittedDescriptions = new HashSet<string>(StringComparer.Ordinal);
        var lastManagedIndex = -1;
        for (var index = 0; index < existingRules.Count; index++)
        {
            var description = existingRules[index]!["description"]?.GetValue<string>() ?? string.Empty;
            if (description.StartsWith(managedPrefix, StringComparison.Ordinal))
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
            if (!description.StartsWith(managedPrefix, StringComparison.Ordinal))
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

    private static void AddMissingManagedRules(
        JsonArray destination,
        JsonArray managedRules,
        HashSet<string> emittedDescriptions)
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
        foreach (var node in rules)
        {
            if (node is JsonObject rule)
                normalized.Add(PrepareRuleForComparison(rule));
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

    private static JsonObject PrepareRuleForComparison(JsonObject source)
    {
        var clone = PrepareRuleForUpdate(source);
        clone.Remove("id");
        clone.Remove("ref");
        return clone;
    }

    private static CloudflareCachePolicyApplyResult Failure(string message, string hostname, string policyName, bool dryRun) => new()
    {
        Success = false,
        DryRun = dryRun,
        Hostname = hostname ?? string.Empty,
        PolicyName = policyName ?? string.Empty,
        Message = message
    };

    private static CloudflareCachePolicyApplyResult SuccessfulResult(
        string hostname,
        string policyName,
        bool dryRun,
        bool changesRequired,
        bool changed,
        int managedRuleCount,
        int preservedRuleCount,
        string message) => new()
    {
        Success = true,
        DryRun = dryRun,
        ChangesRequired = changesRequired,
        Changed = changed,
        Hostname = hostname,
        PolicyName = policyName,
        ManagedRuleCount = managedRuleCount,
        PreservedRuleCount = preservedRuleCount,
        Message = message
    };
}
