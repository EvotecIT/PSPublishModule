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
        agentReadiness = agentReadiness is null ? null : WebAgentReadiness.ResolveSpec(agentReadiness);
        var security = securityHeaders ?? new AgentSecurityHeadersSpec();

        var headers = new JsonObject();
        if (security.Enabled)
        {
            AddHeader(headers, "Strict-Transport-Security", security.Hsts, security.HstsValue);
            AddHeader(headers, "Content-Security-Policy", security.ContentSecurityPolicy, security.ContentSecurityPolicyValue);
            AddHeader(headers, "X-Content-Type-Options", security.XContentTypeOptions, "nosniff");
            AddHeader(headers, "X-Frame-Options", security.XFrameOptions,
                string.IsNullOrWhiteSpace(security.XFrameOptionsValue) ? "DENY" : security.XFrameOptionsValue);
            AddHeader(headers, "Referrer-Policy", security.ReferrerPolicy,
                string.IsNullOrWhiteSpace(security.ReferrerPolicyValue) ? "strict-origin-when-cross-origin" : security.ReferrerPolicyValue);
            AddHeader(headers, "Permissions-Policy", security.PermissionsPolicy, security.PermissionsPolicyValue);
        }

        var descriptionPrefix = CloudflareManagedRuleOwnership.BuildDescriptionPrefix(policyName, hostname, basePath);
        var rules = new JsonArray();

        if (headers.Count > 0)
        {
            var securityExpression = BuildExpression(hostname, basePath);
            CloudflareCachePolicyBuilder.ValidateExpressionLength("response security headers", securityExpression);
            rules.Add(new JsonObject
            {
                ["description"] = $"{descriptionPrefix} security headers",
                ["expression"] = securityExpression,
                ["action"] = "rewrite",
                ["action_parameters"] = new JsonObject { ["headers"] = headers },
                ["enabled"] = true
            });
        }

        foreach (var group in BuildDiscoveryHeaderGroups(agentReadiness, basePath))
        {
            var resourceHeaders = new JsonObject();
            AddHeader(resourceHeaders, "Content-Type", enabled: true, group.ContentType);
            AddHeader(
                resourceHeaders,
                "Access-Control-Allow-Origin",
                security.Enabled && security.CorsForWellKnown,
                security.CorsAllowOrigin);
            var resourceExpression = BuildExactPathExpression(hostname, group.Paths);
            CloudflareCachePolicyBuilder.ValidateExpressionLength($"discovery {group.Name} headers", resourceExpression);
            rules.Add(new JsonObject
            {
                ["description"] = $"{descriptionPrefix} discovery {group.Name} headers",
                ["expression"] = resourceExpression,
                ["action"] = "rewrite",
                ["action_parameters"] = new JsonObject { ["headers"] = resourceHeaders },
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

    private static (string Name, string ContentType, string[] Paths)[] BuildDiscoveryHeaderGroups(
        AgentReadinessSpec? readiness,
        string basePath)
    {
        if (readiness?.Enabled != true)
            return Array.Empty<(string, string, string[])>();

        var groups = new List<(string Name, string ContentType, string[] Paths)>();
        if (readiness.ApiCatalog?.Enabled == true)
        {
            groups.Add((
                "API catalog",
                "application/linkset+json; profile=\"https://www.rfc-editor.org/info/rfc9727\"",
                [NormalizeDiscoveryPath(readiness.ApiCatalog.OutputPath, ".well-known/api-catalog", basePath)]));
        }

        var jsonPaths = new List<string>();
        if (readiness.AgentSkills?.Enabled == true)
            jsonPaths.Add(NormalizeDiscoveryPath(readiness.AgentSkills.IndexPath, ".well-known/agent-skills/index.json", basePath));
        if (readiness.AgentsJson?.Enabled == true)
        {
            jsonPaths.Add(NormalizeDiscoveryPath(readiness.AgentsJson.OutputPath, "agents.json", basePath));
            jsonPaths.Add(NormalizeDiscoveryPath(readiness.AgentsJson.WellKnownOutputPath, ".well-known/agents.json", basePath));
        }
        if (readiness.A2AAgentCard?.Enabled == true)
            jsonPaths.Add(NormalizeDiscoveryPath(readiness.A2AAgentCard.OutputPath, ".well-known/agent-card.json", basePath));
        if (readiness.McpServerCard?.Enabled == true)
            jsonPaths.Add(NormalizeDiscoveryPath(readiness.McpServerCard.OutputPath, ".well-known/mcp/server-card.json", basePath));
        if (jsonPaths.Count > 0)
            groups.Add(("JSON", "application/json", jsonPaths.Distinct(StringComparer.Ordinal).ToArray()));

        return groups.ToArray();
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
