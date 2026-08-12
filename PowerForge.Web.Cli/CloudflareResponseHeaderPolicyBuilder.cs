using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static class CloudflareResponseHeaderPolicyBuilder
{
    private const int MaxHeaderValueLength = 4096;

    internal static JsonArray BuildManagedRules(string hostname, string policyName, AgentSecurityHeadersSpec? securityHeaders)
    {
        hostname = CloudflareCachePolicyBuilder.NormalizeHostname(hostname);
        policyName = CloudflareCachePolicyBuilder.NormalizePolicyName(policyName, hostname);
        var security = securityHeaders ?? new AgentSecurityHeadersSpec();
        if (!security.Enabled)
            return new JsonArray();

        var headers = new JsonObject();
        AddHeader(headers, "Strict-Transport-Security", security.Hsts, security.HstsValue);
        AddHeader(headers, "Content-Security-Policy", security.ContentSecurityPolicy, security.ContentSecurityPolicyValue);
        AddHeader(headers, "X-Content-Type-Options", security.XContentTypeOptions, "nosniff");
        AddHeader(headers, "X-Frame-Options", security.XFrameOptions, security.XFrameOptionsValue ?? "DENY");
        AddHeader(headers, "Referrer-Policy", security.ReferrerPolicy, security.ReferrerPolicyValue);
        AddHeader(headers, "Permissions-Policy", security.PermissionsPolicy, security.PermissionsPolicyValue);

        if (headers.Count == 0)
            return new JsonArray();

        var descriptionPrefix = CloudflareManagedRuleOwnership.BuildPrefix(policyName, hostname);

        return new JsonArray
        {
            new JsonObject
            {
                ["description"] = $"{descriptionPrefix} security headers",
                ["expression"] = $"(http.host eq \"{hostname}\")",
                ["action"] = "rewrite",
                ["action_parameters"] = new JsonObject { ["headers"] = headers },
                ["enabled"] = true
            }
        };
    }

    private static void AddHeader(JsonObject headers, string name, bool enabled, string? value)
    {
        if (!enabled || string.IsNullOrWhiteSpace(value))
            return;

        var normalized = value.Trim();
        if (normalized.Length > MaxHeaderValueLength)
            throw new ArgumentException($"Cloudflare response header '{name}' exceeds {MaxHeaderValueLength} characters.");
        if (normalized.Any(char.IsControl))
            throw new ArgumentException($"Cloudflare response header '{name}' contains a control character.");

        headers[name] = new JsonObject
        {
            ["operation"] = "set",
            ["value"] = normalized
        };
    }
}
