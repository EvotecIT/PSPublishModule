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
        AgentReadinessSpec? agentReadiness = null)
    {
        var ownsHttpClient = httpClient is null;
        httpClient ??= new HttpClient { BaseAddress = new Uri("https://api.cloudflare.com/client/v4/") };
        try
        {
            var cachePreflight = CloudflareCachePolicyManager.Apply(
                zoneId, apiToken, hostname, policyName, htmlPaths, dryRun: true, logger: null, httpClient, basePath);
            if (!cachePreflight.Success)
                return Failure(dryRun, cachePreflight.Message);

            var headerPreflight = CloudflareResponseHeaderPolicyManager.Apply(
                zoneId, apiToken, hostname, policyName, securityHeaders, dryRun: true, httpClient, basePath, agentReadiness);
            if (!headerPreflight.Success)
                return Failure(dryRun, $"No changes were made. {headerPreflight.Message}");

            if (dryRun)
                return Success(cachePreflight, headerPreflight, changed: false, $"{headerPreflight.Message} {cachePreflight.Message}");

            var cacheResult = CloudflareCachePolicyManager.Apply(
                zoneId, apiToken, hostname, policyName, htmlPaths, dryRun: false, logger: null, httpClient, basePath);
            if (!cacheResult.Success)
            {
                var reconciliation = cacheResult.Reconciliation;
                if (reconciliation?.Snapshot is null)
                    return Failure(false, cacheResult.Message);

                var cacheRollback = CloudflareManagedRulesetManager.Restore(
                    reconciliation.Snapshot,
                    reconciliation.AppliedRulesetId,
                    apiToken,
                    httpClient);
                var cacheRecovery = cacheRollback.Success
                    ? "The previous cache-policy state was restored."
                    : "Cache rollback was incomplete; rerun the site policy after resolving the reported Cloudflare error.";
                return Failure(false, $"Cache apply failed: {cacheResult.Message} Cache rollback: {cacheRollback.Message} {cacheRecovery}");
            }

            var headerResult = CloudflareResponseHeaderPolicyManager.Apply(
                zoneId, apiToken, hostname, policyName, securityHeaders, dryRun: false, httpClient, basePath, agentReadiness);
            if (headerResult.Success)
                return Success(cacheResult, headerResult, cacheResult.Changed || headerResult.Changed, $"{headerResult.Message} {cacheResult.Message}");

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
            var recovery = rollbackSucceeded
                ? "The previous site-policy state was restored."
                : "Rollback was incomplete; rerun the site policy after resolving the reported Cloudflare error.";
            return Failure(false, $"Response-header apply failed: {headerResult.Message} {rollbackSummary} {recovery}");
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
        bool changed,
        string message) => new()
    {
        Success = true,
        DryRun = cache.DryRun,
        ChangesRequired = cache.ChangesRequired || headers.ChangesRequired,
        Changed = changed,
        CacheManagedRuleCount = cache.ManagedRuleCount,
        ResponseHeaderManagedRuleCount = headers.ManagedRuleCount,
        Message = message
    };

    private static CloudflareSitePolicyApplyResult Failure(bool dryRun, string message) => new()
    {
        DryRun = dryRun,
        Message = message
    };
}
