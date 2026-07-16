using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace PowerForge.Web.Cli;

internal static class CloudflareCachePolicyBuilder
{
    private const int MaxHtmlPaths = 64;
    private const int MaxRuleExpressionLength = 4096;

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
        IReadOnlyCollection<string>? htmlPaths,
        string? basePath = null)
    {
        hostname = NormalizeHostname(hostname);
        policyName = NormalizePolicyName(policyName, hostname);
        basePath = NormalizeBasePath(basePath);
        var hostFilter = $"http.host eq \"{hostname}\" and http.request.method eq \"GET\" and ";

        var staticExpression = $"({hostFilter}(" + string.Join(" or ", new[]
        {
            BuildPathClause("wildcard", CombineBasePath(basePath, "/css/*")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/js/*")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/assets/*")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/fonts/*")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/images/*")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/img/*")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.css")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.js")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.mjs")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.png")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.jpg")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.jpeg")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.webp")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.svg")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.ico")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.woff")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.woff2"))
        }) + "))";

        var dataExpression = $"({hostFilter}(" + string.Join(" or ", new[]
        {
            BuildPathClause("wildcard", CombineBasePath(basePath, "/data/*")),
            BuildPathClause("eq", CombineBasePath(basePath, "/sitemap.xml")),
            BuildPathClause("eq", CombineBasePath(basePath, "/llms.txt")),
            BuildPathClause("eq", CombineBasePath(basePath, "/llms-full.txt")),
            BuildPathClause("eq", CombineBasePath(basePath, "/llms.json"))
        }) + "))";

        var routeClauses = BuildHtmlRouteClauses(basePath, htmlPaths);
        routeClauses.Add(BuildPathClause("wildcard", CombineBasePath(basePath, "/*.html")));
        var htmlExpression = $"({hostFilter}(" + string.Join(" or ", routeClauses) + "))";

        ValidateExpressionLength("static assets", staticExpression);
        ValidateExpressionLength("data files", dataExpression);
        ValidateExpressionLength("HTML docs and API", htmlExpression);

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

    internal static string NormalizeBasePath(string? rawBasePath)
    {
        var normalized = string.IsNullOrWhiteSpace(rawBasePath)
            ? "/"
            : rawBasePath.Trim().Replace('\\', '/');
        var delimiter = normalized.IndexOfAny(new[] { '?', '#' });
        if (delimiter >= 0)
            normalized = normalized[..delimiter];
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = "/" + normalized;
        while (normalized.Contains("//", StringComparison.Ordinal))
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);

        if (normalized.Contains("..", StringComparison.Ordinal) ||
            normalized.Contains('*') ||
            normalized.Any(char.IsControl))
            throw new ArgumentException($"Invalid Cloudflare base path '{rawBasePath}'.", nameof(rawBasePath));

        if (!normalized.EndsWith("/", StringComparison.Ordinal))
            normalized += "/";
        return normalized;
    }

    private static List<string> BuildHtmlRouteClauses(string basePath, IReadOnlyCollection<string>? htmlPaths)
    {
        var paths = DefaultHtmlPaths
            .Concat(htmlPaths ?? Array.Empty<string>())
            .Select(NormalizeHtmlPath)
            .Where(path => path is not null)
            .Cast<string>()
            .Select(path => CombineBasePath(basePath, path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxHtmlPaths + 1)
            .ToArray();

        if (paths.Length > MaxHtmlPaths)
            throw new ArgumentException($"Cloudflare cache policy supports at most {MaxHtmlPaths} HTML routes.", nameof(htmlPaths));

        var clauses = new List<string>();
        foreach (var path in paths)
        {
            clauses.Add(BuildPathClause("eq", path));
            if (path.Length > 1 && path.EndsWith("/", StringComparison.Ordinal))
                clauses.Add(BuildPathClause("wildcard", path + "*"));
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

    private static string CombineBasePath(string basePath, string path)
    {
        if (basePath == "/")
            return path;
        if (path == "/")
            return basePath;
        if (path.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            return path;
        return basePath.TrimEnd('/') + path;
    }

    private static string BuildPathClause(string operation, string path) =>
        $"http.request.uri.path {operation} \"{EscapeExpressionString(path)}\"";

    private static void ValidateExpressionLength(string ruleName, string expression)
    {
        if (expression.Length > MaxRuleExpressionLength)
        {
            throw new ArgumentException(
                $"Cloudflare {ruleName} expression is {expression.Length} characters; the maximum is {MaxRuleExpressionLength}.");
        }
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
