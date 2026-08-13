using System;
using System.Collections.Generic;
using System.Net.Http;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal sealed class CloudflareSitePolicyApplyResult
{
    internal bool Success { get; init; }
    internal bool DryRun { get; init; }
    internal bool ChangesRequired { get; init; }
    internal bool Changed { get; init; }
    internal int CacheManagedRuleCount { get; init; }
    internal int ResponseHeaderManagedRuleCount { get; init; }
    internal bool SmartTieredCacheManaged { get; init; }
    internal bool? SmartTieredCacheEnabled { get; init; }
    internal string Message { get; init; } = string.Empty;
}

/// <summary>Preflights and applies the cache and response-header rulesets as one recoverable operation.</summary>
internal static class CloudflareSitePolicyManager
{
    internal static CloudflareSitePolicyApplyResult Apply(
        string zoneId,
        string apiToken,
        string hostname,
        string policyName,
        IReadOnlyCollection<string>? htmlPaths,
        AgentSecurityHeadersSpec? securityHeaders,
        bool dryRun,
        string? basePath = null,
        HttpClient? httpClient = null,
        AgentReadinessSpec? agentReadiness = null,
        CloudflareCacheSpec? cache = null,
        bool? smartTieredCache = null)
    {
        var ownsHttpClient = httpClient is null;
        httpClient ??= new HttpClient { BaseAddress = new Uri("https://api.cloudflare.com/client/v4/") };
        try
        {
            var cachePreflight = CloudflareCachePolicyManager.Apply(
                zoneId, apiToken, hostname, policyName, htmlPaths, dryRun: true, logger: null, httpClient, basePath, cache);
            if (!cachePreflight.Success)
                return Failure(dryRun, cachePreflight.Message);

            var headerPreflight = CloudflareResponseHeaderPolicyManager.Apply(
                zoneId, apiToken, hostname, policyName, securityHeaders, dryRun: true, httpClient, basePath, agentReadiness);
            if (!headerPreflight.Success)
                return Failure(dryRun, $"No changes were made. {headerPreflight.Message}");

            CloudflareSmartTieredCacheResult? tieredPreflight = null;
            if (smartTieredCache.HasValue)
            {
                tieredPreflight = CloudflareSmartTieredCacheManager.Apply(
                    zoneId, apiToken, smartTieredCache.Value, dryRun: true, httpClient);
                if (!tieredPreflight.Success)
                    return Failure(dryRun, $"No changes were made. {tieredPreflight.Message}");
            }

            if (dryRun)
                return Success(
                    cachePreflight,
                    headerPreflight,
                    tieredPreflight,
                    changed: false,
                    $"{headerPreflight.Message} {cachePreflight.Message}{FormatTieredMessage(tieredPreflight)}");

            CloudflareSmartTieredCacheResult? tieredResult = null;
            if (smartTieredCache.HasValue)
            {
                tieredResult = CloudflareSmartTieredCacheManager.Apply(
                    zoneId, apiToken, smartTieredCache.Value, dryRun: false, httpClient);
                if (!tieredResult.Success)
                    return Failure(false, $"No ruleset changes were made. {tieredResult.Message}");
            }

            var cacheResult = CloudflareCachePolicyManager.Apply(
                zoneId, apiToken, hostname, policyName, htmlPaths, dryRun: false, logger: null, httpClient, basePath, cache);
            if (!cacheResult.Success)
            {
                var reconciliation = cacheResult.Reconciliation;
                if (reconciliation?.Snapshot is null)
                {
                    var noSnapshotTieredRollback = RollbackTieredCache(zoneId, apiToken, tieredResult, httpClient);
                    return Failure(false, $"{cacheResult.Message}{noSnapshotTieredRollback.Message}");
                }

                var cacheRollback = CloudflareManagedRulesetManager.Restore(
                    reconciliation.Snapshot,
                    reconciliation.AppliedRulesetId,
                    apiToken,
                    httpClient);
                var cacheRecovery = cacheRollback.Success
                    ? "The previous cache-policy state was restored."
                    : "Cache rollback was incomplete; rerun the site policy after resolving the reported Cloudflare error.";
                var cacheTieredRollback = RollbackTieredCache(zoneId, apiToken, tieredResult, httpClient);
                return Failure(false, $"Cache apply failed: {cacheResult.Message} Cache rollback: {cacheRollback.Message} {cacheRecovery}{cacheTieredRollback.Message}");
            }

            var headerResult = CloudflareResponseHeaderPolicyManager.Apply(
                zoneId, apiToken, hostname, policyName, securityHeaders, dryRun: false, httpClient, basePath, agentReadiness);
            if (headerResult.Success)
                return Success(
                    cacheResult,
                    headerResult,
                    tieredResult,
                    cacheResult.Changed || headerResult.Changed || tieredResult?.Changed == true,
                    $"{headerResult.Message} {cacheResult.Message}{FormatTieredMessage(tieredResult)}");

            var rollbackMessages = new List<string>();
            var rollbackSucceeded = true;

            if (headerResult.Snapshot is not null)
            {
                var headerRollback = CloudflareManagedRulesetManager.Restore(
                    headerResult.Snapshot, headerResult.AppliedRulesetId, apiToken, httpClient);
                rollbackSucceeded &= headerRollback.Success;
                rollbackMessages.Add($"Response-header rollback: {headerRollback.Message}");
            }

            if (cacheResult.Changed && cacheResult.Reconciliation?.Snapshot is not null)
            {
                var cacheRollback = CloudflareManagedRulesetManager.Restore(
                    cacheResult.Reconciliation.Snapshot,
                    cacheResult.Reconciliation.AppliedRulesetId,
                    apiToken,
                    httpClient);
                rollbackSucceeded &= cacheRollback.Success;
                rollbackMessages.Add($"Cache rollback: {cacheRollback.Message}");
            }

            var rollbackSummary = rollbackMessages.Count == 0
                ? "No preceding ruleset change required rollback."
                : string.Join(" ", rollbackMessages);
            var tieredRollback = RollbackTieredCache(zoneId, apiToken, tieredResult, httpClient);
            rollbackSucceeded &= tieredRollback.Success;
            var recovery = rollbackSucceeded
                ? "The previous site-policy state was restored."
                : "Rollback was incomplete; rerun the site policy after resolving the reported Cloudflare error.";
            return Failure(false, $"Response-header apply failed: {headerResult.Message} {rollbackSummary}{tieredRollback.Message} {recovery}");
        }
        finally
        {
            if (ownsHttpClient)
                httpClient.Dispose();
        }
    }

    private static CloudflareSitePolicyApplyResult Success(
        CloudflareCachePolicyApplyResult cache,
        CloudflareManagedRulesetResult headers,
        CloudflareSmartTieredCacheResult? tiered,
        bool changed,
        string message) => new()
    {
        Success = true,
        DryRun = cache.DryRun,
        ChangesRequired = cache.ChangesRequired || headers.ChangesRequired || tiered?.ChangesRequired == true,
        Changed = changed,
        CacheManagedRuleCount = cache.ManagedRuleCount,
        ResponseHeaderManagedRuleCount = headers.ManagedRuleCount,
        SmartTieredCacheManaged = tiered is not null,
        SmartTieredCacheEnabled = tiered?.Enabled,
        Message = message
    };

    private static string FormatTieredMessage(CloudflareSmartTieredCacheResult? result) =>
        result is null ? string.Empty : $" {result.Message}";

    private static (bool Success, string Message) RollbackTieredCache(
        string zoneId,
        string apiToken,
        CloudflareSmartTieredCacheResult? applied,
        HttpClient httpClient)
    {
        if (applied?.Changed != true || applied.PreviousEnabled is null)
            return (true, string.Empty);

        var rollback = CloudflareSmartTieredCacheManager.Apply(
            zoneId, apiToken, applied.PreviousEnabled.Value, dryRun: false, httpClient);
        return rollback.Success
            ? (true, $" Smart Tiered Cache rollback: {rollback.Message}")
            : (false, $" Smart Tiered Cache rollback was incomplete: {rollback.Message}");
    }

    private static CloudflareSitePolicyApplyResult Failure(bool dryRun, string message) => new()
    {
        DryRun = dryRun,
        Message = message
    };
}
