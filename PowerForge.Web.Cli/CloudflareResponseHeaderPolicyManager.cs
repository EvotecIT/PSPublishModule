using System;
using System.Net.Http;
using System.Text.Json.Nodes;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static class CloudflareResponseHeaderPolicyManager
{
    private const string ResponseHeadersPhase = "http_response_headers_transform";

    internal static CloudflareManagedRulesetResult Apply(
        string zoneId,
        string apiToken,
        string hostname,
        string policyName,
        AgentSecurityHeadersSpec? securityHeaders,
        bool dryRun,
        HttpClient? httpClient = null)
    {
        if (!CloudflareCachePolicyManager.TryValidateInputs(zoneId, apiToken, ref hostname, ref policyName, out var error))
            return new CloudflareManagedRulesetResult { Message = error };

        JsonArray managedRules;
        try
        {
            managedRules = CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(hostname, policyName, securityHeaders);
        }
        catch (ArgumentException ex)
        {
            return new CloudflareManagedRulesetResult { Message = ex.Message };
        }

        var ownsHttpClient = httpClient is null;
        httpClient ??= new HttpClient { BaseAddress = new Uri("https://api.cloudflare.com/client/v4/") };
        try
        {
            return CloudflareManagedRulesetManager.Apply(
                zoneId.Trim(),
                apiToken,
                ResponseHeadersPhase,
                "PowerForge response header policy",
                "PowerForge-managed response security headers",
                $"PowerForge {policyName}:",
                managedRules,
                dryRun,
                $"response header policy for {hostname}",
                httpClient);
        }
        finally
        {
            if (ownsHttpClient)
                httpClient.Dispose();
        }
    }
}
