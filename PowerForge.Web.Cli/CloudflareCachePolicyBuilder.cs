using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace PowerForge.Web.Cli;

internal static class CloudflareCachePolicyBuilder
{
    private const int MaxHtmlPaths = 64;

    private static readonly string[] DefaultHtmlPaths =
    {
        "/",
        "/docs/",
        "/api/",
        "/blog/",
        "/showcase/",
        "/playground/",
        "/pricing/",
        "/benchmarks/",
        "/faq/",
        "/search/",
        "/404/"
    };

    internal static JsonArray BuildManagedRules(
        string hostname,
        string policyName,
        IReadOnlyCollection<string>? htmlPaths)
    {
        hostname = NormalizeHostname(hostname);
        policyName = NormalizePolicyName(policyName, hostname);
        var hostFilter = $"http.host eq \"{hostname}\" and http.request.method eq \"GET\" and ";

        var staticExpression = $"({hostFilter}(" + string.Join(" or ", new[]
        {
            "http.request.uri.path wildcard \"/css/*\"",
            "http.request.uri.path wildcard \"/js/*\"",
            "http.request.uri.path wildcard \"/assets/*\"",
            "http.request.uri.path wildcard \"/fonts/*\"",
            "http.request.uri.path wildcard \"/images/*\"",
            "http.request.uri.path wildcard \"/img/*\"",
            "http.request.uri.path wildcard \"/*.css\"",
            "http.request.uri.path wildcard \"/*.js\"",
            "http.request.uri.path wildcard \"/*.mjs\"",
            "http.request.uri.path wildcard \"/*.png\"",
            "http.request.uri.path wildcard \"/*.jpg\"",
            "http.request.uri.path wildcard \"/*.jpeg\"",
            "http.request.uri.path wildcard \"/*.webp\"",
            "http.request.uri.path wildcard \"/*.svg\"",
            "http.request.uri.path wildcard \"/*.ico\"",
            "http.request.uri.path wildcard \"/*.woff\"",
            "http.request.uri.path wildcard \"/*.woff2\""
        }) + "))";

        var dataExpression = $"({hostFilter}(" + string.Join(" or ", new[]
        {
            "http.request.uri.path wildcard \"/data/*\"",
            "http.request.uri.path eq \"/sitemap.xml\"",
            "http.request.uri.path eq \"/llms.txt\"",
            "http.request.uri.path eq \"/llms-full.txt\"",
            "http.request.uri.path eq \"/llms.json\""
        }) + "))";

        var routeClauses = BuildHtmlRouteClauses(htmlPaths);
        routeClauses.Add("http.request.uri.path wildcard \"*.html\"");
        var htmlExpression = $"({hostFilter}(" + string.Join(" or ", routeClauses) + "))";

        return new JsonArray
        {
            BuildRule($"PowerForge {policyName}: static assets", staticExpression, ignoreQueryString: true),
            BuildRule($"PowerForge {policyName}: data files", dataExpression, ignoreQueryString: false),
            BuildRule($"PowerForge {policyName}: HTML docs and API", htmlExpression, ignoreQueryString: false)
        };
    }

    internal static string NormalizeHostname(string hostname)
    {
        var normalized = (hostname ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();
        if (Uri.CheckHostName(normalized) != UriHostNameType.Dns)
            throw new ArgumentException($"Invalid Cloudflare hostname '{hostname}'.", nameof(hostname));
        return normalized;
    }

    internal static string NormalizePolicyName(string policyName, string hostname)
    {
        var normalized = string.IsNullOrWhiteSpace(policyName) ? hostname : policyName.Trim();
        if (normalized.Length > 80 || normalized.Contains(':') || normalized.Any(char.IsControl))
            throw new ArgumentException("Cloudflare policy name must be 1-80 characters and cannot contain a colon or control character.", nameof(policyName));
        return normalized;
    }

    private static List<string> BuildHtmlRouteClauses(IReadOnlyCollection<string>? htmlPaths)
    {
        var paths = DefaultHtmlPaths
            .Concat(htmlPaths ?? Array.Empty<string>())
            .Select(NormalizeHtmlPath)
            .Where(path => path is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxHtmlPaths + 1)
            .ToArray();

        if (paths.Length > MaxHtmlPaths)
            throw new ArgumentException($"Cloudflare cache policy supports at most {MaxHtmlPaths} HTML routes.", nameof(htmlPaths));

        var clauses = new List<string>();
        foreach (var path in paths)
        {
            var escaped = EscapeExpressionString(path);
            clauses.Add($"http.request.uri.path eq \"{escaped}\"");
            if (path.Length > 1 && path.EndsWith("/", StringComparison.Ordinal))
                clauses.Add($"http.request.uri.path wildcard \"{escaped}*\"");
        }

        return clauses;
    }

    private static string? NormalizeHtmlPath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return null;

        var path = rawPath.Trim().Replace('\\', '/');
        var delimiter = path.IndexOfAny(new[] { '?', '#' });
        if (delimiter >= 0)
            path = path[..delimiter];
        if (!path.StartsWith("/", StringComparison.Ordinal))
            path = "/" + path;
        while (path.Contains("//", StringComparison.Ordinal))
            path = path.Replace("//", "/", StringComparison.Ordinal);

        if (path.Contains("..", StringComparison.Ordinal) ||
            path.Contains('*') ||
            path.Any(char.IsControl))
            throw new ArgumentException($"Invalid Cloudflare HTML route '{rawPath}'.", nameof(rawPath));

        if (path.Equals("/sitemap.xml", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/data/", StringComparison.OrdinalIgnoreCase))
            return null;

        return path;
    }

    private static JsonObject BuildRule(string description, string expression, bool ignoreQueryString)
    {
        var actionParameters = new JsonObject
        {
            ["cache"] = true,
            ["edge_ttl"] = new JsonObject { ["mode"] = "respect_origin" },
            ["browser_ttl"] = new JsonObject { ["mode"] = "respect_origin" },
            ["respect_strong_etags"] = true
        };

        if (ignoreQueryString)
        {
            actionParameters["cache_key"] = new JsonObject
            {
                ["custom_key"] = new JsonObject
                {
                    ["query_string"] = new JsonObject
                    {
                        ["exclude"] = new JsonObject { ["all"] = true }
                    }
                }
            };
        }

        return new JsonObject
        {
            ["description"] = description,
            ["expression"] = expression,
            ["action"] = "set_cache_settings",
            ["action_parameters"] = actionParameters,
            ["enabled"] = true
        };
    }

    private static string EscapeExpressionString(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
