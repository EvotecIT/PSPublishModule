using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static class CloudflareResponseHeaderPolicyBuilder
{
    private const int MaxHeaderValueLength = 4096;

    internal static JsonArray BuildManagedRules(
        string hostname,
        string policyName,
        AgentSecurityHeadersSpec? securityHeaders,
        string? basePath = null)
    {
        hostname = CloudflareCachePolicyBuilder.NormalizeHostname(hostname);
        policyName = CloudflareCachePolicyBuilder.NormalizePolicyName(policyName, hostname);
        basePath = CloudflareCachePolicyBuilder.NormalizeBasePath(basePath);
        var security = securityHeaders ?? new AgentSecurityHeadersSpec();
        if (!security.Enabled)
            return new JsonArray();

        var headers = new JsonObject();
        AddHeader(headers, "Strict-Transport-Security", security.Hsts, security.HstsValue);
        AddHeader(headers, "Content-Security-Policy", security.ContentSecurityPolicy, security.ContentSecurityPolicyValue);
        AddHeader(headers, "X-Content-Type-Options", security.XContentTypeOptions, "nosniff");
        AddHeader(headers, "X-Frame-Options", security.XFrameOptions,
            string.IsNullOrWhiteSpace(security.XFrameOptionsValue) ? "DENY" : security.XFrameOptionsValue);
        AddHeader(headers, "Referrer-Policy", security.ReferrerPolicy,
            string.IsNullOrWhiteSpace(security.ReferrerPolicyValue) ? "strict-origin-when-cross-origin" : security.ReferrerPolicyValue);
        AddHeader(headers, "Permissions-Policy", security.PermissionsPolicy, security.PermissionsPolicyValue);

        if (headers.Count == 0)
            return new JsonArray();

        var descriptionPrefix = CloudflareManagedRuleOwnership.BuildPrefix(policyName, hostname);

        return new JsonArray
        {
            new JsonObject
            {
                ["description"] = $"{descriptionPrefix} security headers",
                ["expression"] = BuildExpression(hostname, basePath),
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

    private static string BuildExpression(string hostname, string basePath)
    {
        var hostExpression = $"http.host eq \"{hostname}\"";
        if (basePath == "/")
            return $"({hostExpression})";

        var root = CloudflareCachePolicyBuilder.EscapeExpressionString(basePath.TrimEnd('/'));
        var prefix = CloudflareCachePolicyBuilder.EscapeExpressionString(basePath);
        return $"({hostExpression} and (http.request.uri.path eq \"{root}\" or starts_with(http.request.uri.path, \"{prefix}\")))";
    }
}
