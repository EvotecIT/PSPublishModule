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
        string? basePath = null,
        AgentReadinessSpec? agentReadiness = null)
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

        var descriptionPrefix = CloudflareManagedRuleOwnership.BuildPrefix(policyName, hostname);
        var rules = new JsonArray();

        if (headers.Count > 0)
        {
            rules.Add(new JsonObject
            {
                ["description"] = $"{descriptionPrefix} security headers",
                ["expression"] = BuildExpression(hostname, basePath),
                ["action"] = "rewrite",
                ["action_parameters"] = new JsonObject { ["headers"] = headers },
                ["enabled"] = true
            });
        }

        var discoveryPaths = BuildDiscoveryPaths(agentReadiness, basePath);
        if (security.Enabled && security.CorsForWellKnown &&
            !string.IsNullOrWhiteSpace(security.CorsAllowOrigin) && discoveryPaths.Length > 0)
        {
            var corsHeaders = new JsonObject();
            AddHeader(corsHeaders, "Access-Control-Allow-Origin", enabled: true, security.CorsAllowOrigin);
            rules.Add(new JsonObject
            {
                ["description"] = $"{descriptionPrefix} discovery resource CORS",
                ["expression"] = BuildExactPathExpression(hostname, discoveryPaths),
                ["action"] = "rewrite",
                ["action_parameters"] = new JsonObject { ["headers"] = corsHeaders },
                ["enabled"] = true
            });
        }

        return rules;
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

    private static string[] BuildDiscoveryPaths(AgentReadinessSpec? readiness, string basePath)
    {
        if (readiness?.Enabled != true)
            return Array.Empty<string>();

        var paths = new List<string>();
        if (readiness.ApiCatalog?.Enabled == true)
            paths.Add(NormalizeDiscoveryPath(readiness.ApiCatalog.OutputPath, ".well-known/api-catalog", basePath));
        if (readiness.AgentSkills?.Enabled == true)
            paths.Add(NormalizeDiscoveryPath(readiness.AgentSkills.IndexPath, ".well-known/agent-skills/index.json", basePath));
        if (readiness.AgentsJson?.Enabled == true)
        {
            paths.Add(NormalizeDiscoveryPath(readiness.AgentsJson.OutputPath, "agents.json", basePath));
            paths.Add(NormalizeDiscoveryPath(readiness.AgentsJson.WellKnownOutputPath, ".well-known/agents.json", basePath));
        }
        if (readiness.A2AAgentCard?.Enabled == true)
            paths.Add(NormalizeDiscoveryPath(readiness.A2AAgentCard.OutputPath, ".well-known/agent-card.json", basePath));
        if (readiness.McpServerCard?.Enabled == true)
            paths.Add(NormalizeDiscoveryPath(readiness.McpServerCard.OutputPath, ".well-known/mcp/server-card.json", basePath));

        return paths.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string NormalizeDiscoveryPath(string? configured, string fallback, string basePath)
    {
        var value = string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out _) || value.Contains("..", StringComparison.Ordinal) ||
            value.Contains('*') || value.Contains('?') || value.Contains('#') || value.Any(char.IsControl))
            throw new ArgumentException($"Invalid Cloudflare discovery resource path '{configured}'.", nameof(configured));

        var route = "/" + value.Replace('\\', '/').Trim('/');
        return basePath == "/" ? route : basePath.TrimEnd('/') + route;
    }

    private static string BuildExactPathExpression(string hostname, IReadOnlyCollection<string> paths)
    {
        var clauses = paths.Select(path =>
            $"http.request.uri.path eq \"{CloudflareCachePolicyBuilder.EscapeExpressionString(path)}\"");
        return $"(http.host eq \"{hostname}\" and ({string.Join(" or ", clauses)}))";
    }
}
