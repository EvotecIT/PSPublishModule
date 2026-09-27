using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace PowerForge.Web.Cli;

/// <summary>Builds a fail-closed cache rule for explicitly selected anonymous application routes.</summary>
internal static class CloudflareDynamicOriginCachePolicyBuilder
{
    private const int MaxPublicPaths = 32;

    internal static JsonArray BuildManagedRules(
        string hostname,
        string policyName,
        IReadOnlyCollection<string>? publicPaths,
        string? basePath = null)
    {
        hostname = CloudflareCachePolicyBuilder.NormalizeHostname(hostname);
        policyName = CloudflareCachePolicyBuilder.NormalizePolicyName(policyName, hostname);
        basePath = CloudflareCachePolicyBuilder.NormalizeBasePath(basePath);

        if (publicPaths is null || publicPaths.Count == 0 || publicPaths.Count > MaxPublicPaths)
            throw new ArgumentException($"Specify 1-{MaxPublicPaths} explicit public paths for dynamic origin caching.", nameof(publicPaths));

        var paths = publicPaths.Select(path => NormalizePublicPath(path, basePath))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var pathExpression = string.Join(" or ", paths.Select(path =>
            $"http.request.uri.path eq \"{CloudflareCachePolicyBuilder.EscapeExpressionString(CloudflareCachePolicyBuilder.EncodeUriPathForExpression(path))}\""));
        var expression = $"(http.host eq \"{hostname}\" and " +
                         "(http.request.method eq \"GET\" or http.request.method eq \"PURGE\") and " +
                         "not http.request.headers.truncated and " +
                         "not any(lower(http.request.headers.names[*])[*] eq \"cookie\") and " +
                         "not any(lower(http.request.headers.names[*])[*] eq \"authorization\") and " +
                         $"({pathExpression}))";
        CloudflareCachePolicyBuilder.ValidateExpressionLength("dynamic public routes", expression);

        var description = $"{CloudflareManagedRuleOwnership.BuildDescriptionPrefix(policyName, hostname, basePath)} anonymous public routes";
        return new JsonArray
        {
            new JsonObject
            {
                ["description"] = description,
                ["expression"] = expression,
                ["action"] = "set_cache_settings",
                ["action_parameters"] = new JsonObject
                {
                    ["cache"] = true,
                    // No origin cache directive means no edge caching. Never substitute a TTL for
                    // private, no-store, Set-Cookie, or an unexpected application response.
                    ["edge_ttl"] = new JsonObject { ["mode"] = "bypass_by_default" },
                    ["browser_ttl"] = new JsonObject { ["mode"] = "respect_origin" },
                    ["respect_strong_etags"] = true
                },
                ["enabled"] = true
            }
        };
    }

    private static string NormalizePublicPath(string? path, string basePath)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path != path.Trim() ||
            !path.StartsWith("/", StringComparison.Ordinal) ||
            path == "/" ||
            path.Contains("//", StringComparison.Ordinal) ||
            path.Contains("..", StringComparison.Ordinal) ||
            path.IndexOfAny(['?', '#', '*', '%', '\\']) >= 0 ||
            path.Any(char.IsControl) ||
            path.Any(char.IsWhiteSpace) ||
            (basePath != "/" && !path.StartsWith(basePath, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"Invalid explicit public cache path '{path}'. Paths must be exact, absolute, and inside the site base path.", nameof(path));
        }

        return path;
    }
}
