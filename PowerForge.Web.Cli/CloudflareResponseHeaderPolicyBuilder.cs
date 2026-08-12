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

        var linkHeader = BuildDiscoveryLinkHeader(agentReadiness, basePath);
        if (!string.IsNullOrWhiteSpace(linkHeader))
        {
            if (linkHeader.Length > MaxHeaderValueLength)
                throw new ArgumentException($"Cloudflare response header 'Link' exceeds {MaxHeaderValueLength} characters.");
            rules.Add(new JsonObject
            {
                ["description"] = $"{descriptionPrefix} discovery Link headers",
                ["expression"] = BuildHomepageExpression(hostname, basePath),
                ["action"] = "rewrite",
                ["action_parameters"] = new JsonObject
                {
                    ["headers"] = new JsonObject
                    {
                        ["Link"] = new JsonObject { ["operation"] = "add", ["value"] = linkHeader }
                    }
                },
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
            var resourceExpression = BuildDiscoveryExpression(hostname, basePath, group);
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

        var root = CloudflareCachePolicyBuilder.EscapeExpressionString(
            CloudflareCachePolicyBuilder.EncodeUriPathForExpression(basePath.TrimEnd('/')));
        var prefix = CloudflareCachePolicyBuilder.EscapeExpressionString(
            CloudflareCachePolicyBuilder.EncodeUriPathForExpression(basePath));
        return $"({hostExpression} and (http.request.uri.path eq \"{root}\" or starts_with(http.request.uri.path, \"{prefix}\")))";
    }

    private static string BuildHomepageExpression(string hostname, string basePath)
    {
        var homepage = CloudflareCachePolicyBuilder.EncodeUriPathForExpression(
            basePath == "/" ? "/" : basePath.TrimEnd('/'));
        var slashHomepage = CloudflareCachePolicyBuilder.EncodeUriPathForExpression(basePath);
        return homepage == slashHomepage
            ? $"(http.host eq \"{hostname}\" and http.request.uri.path eq \"{homepage}\")"
            : $"(http.host eq \"{hostname}\" and (http.request.uri.path eq \"{homepage}\" or http.request.uri.path eq \"{slashHomepage}\"))";
    }

    private static string? BuildDiscoveryLinkHeader(AgentReadinessSpec? readiness, string basePath)
    {
        if (readiness?.Enabled != true || !readiness.LinkHeaders)
            return null;

        var links = new List<string>();
        void Add(string? configured, string fallback, string relation, string type)
        {
            var value = string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();
            string target;
            if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) &&
                (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
            {
                if (value.Any(char.IsControl))
                    throw new ArgumentException($"Invalid Cloudflare discovery resource URL '{configured}'.", nameof(configured));
                target = absolute.AbsoluteUri;
            }
            else
            {
                target = NormalizeDiscoveryPath(value, fallback, basePath);
            }
            links.Add($"<{EscapeLinkUriReference(target)}>; rel=\"{relation}\"; type=\"{type}\"");
        }

        if (readiness.ApiCatalog?.Enabled == true)
            Add(readiness.ApiCatalog.OutputPath, ".well-known/api-catalog", "api-catalog", "application/linkset+json");
        if (readiness.AgentSkills?.Enabled == true)
            Add(readiness.AgentSkills.IndexPath, ".well-known/agent-skills/index.json", "describedby", "application/json");
        if (readiness.AgentsJson?.Enabled == true)
            Add(readiness.AgentsJson.OutputPath, "agents.json", "describedby", "application/json");
        if (readiness.A2AAgentCard?.Enabled == true)
            Add(readiness.A2AAgentCard.OutputPath, ".well-known/agent-card.json", "service-desc", "application/json");
        if (readiness.McpServerCard?.Enabled == true)
            Add(readiness.McpServerCard.OutputPath, ".well-known/mcp/server-card.json", "service-desc", "application/json");
        if (readiness.OpenApi?.Enabled == true)
        {
            if (string.IsNullOrWhiteSpace(readiness.OpenApi.Path))
                throw new ArgumentException(
                    "Cloudflare managed Link headers require AgentReadiness.OpenApi.Path when OpenAPI discovery is enabled; configure the deployed document path explicitly.");
            Add(readiness.OpenApi.Path, "openapi.json", "service-desc", "application/openapi+json");
        }
        if (readiness.MarkdownArtifacts?.Enabled == true)
        {
            var extension = string.IsNullOrWhiteSpace(readiness.MarkdownArtifacts.Extension)
                ? ".md"
                : readiness.MarkdownArtifacts.Extension!.Trim();
            if (!extension.StartsWith(".", StringComparison.Ordinal))
                extension = "." + extension;
            Add("index" + (extension == "." ? ".md" : extension), "index.md", "alternate", "text/markdown");
        }
        return string.Join(", ", links.Distinct(StringComparer.Ordinal));
    }

    private static string EscapeLinkUriReference(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
            return absolute.AbsoluteUri;

        return CloudflareCachePolicyBuilder.EncodeUriPathForExpression(value);
    }

    private static DiscoveryHeaderGroup[] BuildDiscoveryHeaderGroups(
        AgentReadinessSpec? readiness,
        string basePath)
    {
        if (readiness?.Enabled != true)
            return Array.Empty<DiscoveryHeaderGroup>();

        var groups = new List<DiscoveryHeaderGroup>();
        if (readiness.ApiCatalog?.Enabled == true)
        {
            groups.Add(new DiscoveryHeaderGroup(
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
            groups.Add(new DiscoveryHeaderGroup("JSON", "application/json", jsonPaths.Distinct(StringComparer.Ordinal).ToArray()));

        if (readiness.MarkdownArtifacts?.Enabled == true)
        {
            var extension = NormalizeMarkdownArtifactExtension(readiness.MarkdownArtifacts.Extension);
            groups.Add(new DiscoveryHeaderGroup("Markdown", "text/markdown; charset=utf-8", [], extension));
        }

        return groups.ToArray();
    }

    private static string NormalizeMarkdownArtifactExtension(string? configured)
    {
        var extension = string.IsNullOrWhiteSpace(configured) ? ".md" : configured.Trim();
        if (!extension.StartsWith(".", StringComparison.Ordinal))
            extension = "." + extension;
        return extension == "." ? ".md" : extension;
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
            $"http.request.uri.path eq \"{CloudflareCachePolicyBuilder.EscapeExpressionString(CloudflareCachePolicyBuilder.EncodeUriPathForExpression(path))}\"");
        return $"(http.host eq \"{hostname}\" and ({string.Join(" or ", clauses)}))";
    }

    private static string BuildDiscoveryExpression(string hostname, string basePath, DiscoveryHeaderGroup group)
    {
        if (string.IsNullOrWhiteSpace(group.PathSuffix))
            return BuildExactPathExpression(hostname, group.Paths);

        var suffix = CloudflareCachePolicyBuilder.EscapeExpressionString(
            CloudflareCachePolicyBuilder.EncodeUriPathForExpression(group.PathSuffix));
        if (basePath == "/")
            return $"(http.host eq \"{hostname}\" and ends_with(http.request.uri.path, \"{suffix}\"))";

        var prefix = CloudflareCachePolicyBuilder.EscapeExpressionString(
            CloudflareCachePolicyBuilder.EncodeUriPathForExpression(basePath));
        return $"(http.host eq \"{hostname}\" and starts_with(http.request.uri.path, \"{prefix}\") and ends_with(http.request.uri.path, \"{suffix}\"))";
    }

    private sealed record DiscoveryHeaderGroup(string Name, string ContentType, string[] Paths, string? PathSuffix = null);
}
