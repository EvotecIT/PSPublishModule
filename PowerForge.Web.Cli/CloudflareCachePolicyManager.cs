using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using PowerForge.Web;

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
    internal CloudflareManagedRulesetResult? Reconciliation { get; init; }
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
        string? basePath = null,
        CloudflareCacheSpec? cache = null)
    {
        if (!TryValidateInputs(zoneId, apiToken, ref hostname, ref policyName, out var error))
            return Failure(error, hostname, policyName, dryRun);

        JsonArray managedRules;
        try
        {
            managedRules = CloudflareCachePolicyBuilder.BuildManagedRules(hostname, policyName, htmlPaths, basePath, cache);
        }
        catch (ArgumentException ex)
        {
            return Failure(ex.Message, hostname, policyName, dryRun);
        }

        var ownsHttpClient = httpClient is null;
        httpClient ??= new HttpClient { BaseAddress = new Uri("https://api.cloudflare.com/client/v4/") };
        try
        {
            var reconciled = CloudflareManagedRulesetManager.Apply(
                zoneId.Trim(),
                apiToken,
                CachePhase,
                "PowerForge cache policy",
                "PowerForge-managed cache policy",
                CloudflareManagedRuleOwnership.BuildOwnershipPrefix(hostname, basePath),
                managedRules,
                dryRun,
                $"cache policy for {hostname}",
                httpClient,
                rule => CloudflareManagedRuleOwnership.IsLegacyRuleForSite(rule, policyName, hostname, basePath));
            if (reconciled.Success)
                logger?.Info(reconciled.Message);
            return new CloudflareCachePolicyApplyResult
            {
                Success = reconciled.Success,
                DryRun = dryRun,
                ChangesRequired = reconciled.ChangesRequired,
                Changed = reconciled.Changed,
                Hostname = hostname,
                PolicyName = policyName,
                ManagedRuleCount = reconciled.ManagedRuleCount,
                PreservedRuleCount = reconciled.PreservedRuleCount,
                Message = reconciled.Message,
                Reconciliation = reconciled
            };
        }
        finally
        {
            if (ownsHttpClient)
                httpClient.Dispose();
        }
    }

    internal static bool TryValidateInputs(string zoneId, string apiToken, ref string hostname, ref string policyName, out string error)
    {
        if (string.IsNullOrWhiteSpace(zoneId))
        {
            error = "Missing zoneId.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(apiToken))
        {
            error = "Missing apiToken.";
            return false;
        }
        var normalizedZoneId = zoneId.Trim();
        if (normalizedZoneId.Length != 32 || normalizedZoneId.Any(character => !Uri.IsHexDigit(character)))
        {
            error = "Cloudflare zoneId must be a 32-character hexadecimal identifier.";
            return false;
        }

        try
        {
            hostname = CloudflareCachePolicyBuilder.NormalizeHostname(hostname);
            policyName = CloudflareCachePolicyBuilder.NormalizePolicyName(policyName, hostname);
            error = string.Empty;
            return true;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static CloudflareCachePolicyApplyResult Failure(string message, string hostname, string policyName, bool dryRun) => new()
    {
        DryRun = dryRun,
        Hostname = hostname ?? string.Empty,
        PolicyName = policyName ?? string.Empty,
        Message = message
    };
}
