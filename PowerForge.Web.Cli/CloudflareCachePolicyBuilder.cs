using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace PowerForge.Web.Cli;

internal static class CloudflareCachePolicyBuilder
{
    private const int MaxHtmlPaths = 64;
    private const int MaxRuleExpressionLength = 4096;
    private const int LegacyHtmlEdgeTtlSeconds = 7200;
    private const int LegacyHtmlBrowserTtlSeconds = 300;

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
        string? basePath = null,
        CloudflareCacheSpec? cache = null)
    {
        hostname = NormalizeHostname(hostname);
        policyName = NormalizePolicyName(policyName, hostname);
        basePath = NormalizeBasePath(basePath);
        var hostFilter = $"http.host eq \"{hostname}\" and http.request.method eq \"GET\" and ";
        var allGetExpression = basePath == "/"
            ? $"(http.host eq \"{hostname}\" and http.request.method eq \"GET\")"
            : $"({hostFilter}({BuildPathClause("eq", basePath.TrimEnd('/'))} or {BuildPathClause("wildcard", basePath + "*")}))";

        var staticExpression = $"({hostFilter}(" + string.Join(" or ", new[]
        {
            BuildPathClause("wildcard", CombineBasePath(basePath, "/css/*")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/js/*")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/assets/*")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/fonts/*")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/images/*")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/img/*")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/media/*")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/_framework/*")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/_content/*")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.map")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.css")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.js")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.mjs")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.png")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.jpg")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.jpeg")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.webp")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.svg")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.svgz")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.gif")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.apng")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.avif")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.ico")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.woff")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.woff2")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.ttf")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.mp4")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.webm")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.ogg")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.br")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.gz")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.pdf")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.zip")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.wasm")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.webcil")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.dat")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.dll")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.pdb"))
        }) + "))";

        var dataExpression = $"({hostFilter}(" + string.Join(" or ", new[]
        {
            BuildPathClause("wildcard", CombineBasePath(basePath, "/data/*")),
            BuildPathClause("eq", CombineBasePath(basePath, "/sitemap.xml")),
            BuildPathClause("eq", CombineBasePath(basePath, "/llms.txt")),
            BuildPathClause("eq", CombineBasePath(basePath, "/llms-full.txt")),
            BuildPathClause("eq", CombineBasePath(basePath, "/llms.json")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.json")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.xml")),
            BuildPathClause("wildcard", CombineBasePath(basePath, "/*.txt"))
        }) + "))";

        var routeClauses = BuildHtmlRouteClauses(basePath, htmlPaths);
        routeClauses.Add(BuildScopedPathFunctionClause(basePath, "ends_with", "/"));
        routeClauses.Add(BuildScopedPathFunctionClause(basePath, "ends_with", ".html"));
        routeClauses.Add(BuildPathClause("wildcard", CombineBasePath(basePath, "/*.html")));
        routeClauses.Add(BuildScopedPathFunctionClause(basePath, "ends_with", ".htm"));
        routeClauses.Add(BuildPathClause("wildcard", CombineBasePath(basePath, "/*.htm")));
        var htmlExpression = $"({hostFilter}(" + string.Join(" or ", routeClauses) + "))";

        ValidateExpressionLength("static assets", staticExpression);
        ValidateExpressionLength("data files", dataExpression);
        ValidateExpressionLength("HTML docs and API", htmlExpression);
        var descriptionPrefix = CloudflareManagedRuleOwnership.BuildDescriptionPrefix(policyName, hostname, basePath);

        var htmlEdgeTtlSeconds = cache?.EdgeTtlSeconds ?? LegacyHtmlEdgeTtlSeconds;
        ValidateTtl("edge", htmlEdgeTtlSeconds);

        var dataRule = cache is null
            ? BuildRespectOriginRule($"{descriptionPrefix} data files", dataExpression)
            : BuildOverrideRule($"{descriptionPrefix} data files", dataExpression, htmlEdgeTtlSeconds, browserTtlSeconds: null);
        var staticRule = cache is null
            ? BuildRespectOriginRule($"{descriptionPrefix} static assets", staticExpression)
            : BuildOverrideRule($"{descriptionPrefix} static assets", allGetExpression, htmlEdgeTtlSeconds, browserTtlSeconds: null);

        return new JsonArray
        {
            BuildOverrideRule(
                $"{descriptionPrefix} HTML docs and API",
                htmlExpression,
                htmlEdgeTtlSeconds,
                cache is null ? LegacyHtmlBrowserTtlSeconds : null),
            dataRule,
            staticRule
        };
    }

    private static void ValidateTtl(string name, int value)
    {
        if (value is < 1 or > 31536000)
            throw new ArgumentOutOfRangeException(name, value, $"Cloudflare {name} TTL must be between 1 and 31536000 seconds.");
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
            // Directory routes plus .html and .htm files are already covered by the compact
            // provider-wide clauses. Keep only exceptional extensionless routes so
            // large documentation menus do not inflate the Cloudflare expression.
            .Where(path => !path.EndsWith("/", StringComparison.Ordinal) &&
                           !path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) &&
                           !path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
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
        $"http.request.uri.path {operation} \"{EscapeExpressionString(EncodeUriPathForExpression(path))}\"";

    private static string BuildScopedPathFunctionClause(string basePath, string functionName, string suffix)
    {
        var function = $"{functionName}(http.request.uri.path, \"{EscapeExpressionString(EncodeUriPathForExpression(suffix))}\")";
        return basePath == "/"
            ? function
            : $"(starts_with(http.request.uri.path, \"{EscapeExpressionString(EncodeUriPathForExpression(basePath))}\") and {function})";
    }

    internal static void ValidateExpressionLength(string ruleName, string expression)
    {
        if (expression.Length > MaxRuleExpressionLength)
        {
            throw new ArgumentException(
                $"Cloudflare {ruleName} expression is {expression.Length} characters; the maximum is {MaxRuleExpressionLength}.");
        }
    }

    private static JsonObject BuildOverrideRule(
        string description,
        string expression,
        int edgeTtlSeconds,
        int? browserTtlSeconds)
    {
        var browserTtl = browserTtlSeconds.HasValue
            ? new JsonObject
            {
                ["mode"] = "override_origin",
                ["default"] = browserTtlSeconds.Value
            }
            : new JsonObject { ["mode"] = "respect_origin" };
        var actionParameters = new JsonObject
        {
            ["cache"] = true,
            ["edge_ttl"] = new JsonObject
            {
                ["mode"] = "override_origin",
                ["default"] = edgeTtlSeconds,
                ["status_code_ttl"] = BuildStatusCodeTtls(edgeTtlSeconds)
            },
            ["browser_ttl"] = browserTtl,
            ["respect_strong_etags"] = true
        };

        return BuildRule(description, expression, actionParameters);
    }

    private static JsonObject BuildRespectOriginRule(string description, string expression)
    {
        var actionParameters = new JsonObject
        {
            ["cache"] = true,
            ["edge_ttl"] = new JsonObject
            {
                ["mode"] = "respect_origin",
                ["status_code_ttl"] = new JsonArray
                {
                    StatusCodeRange(300, 499, 0),
                    StatusCodeRange(500, null, -1)
                }
            },
            ["browser_ttl"] = new JsonObject { ["mode"] = "respect_origin" },
            ["respect_strong_etags"] = true
        };

        return BuildRule(description, expression, actionParameters);
    }

    private static JsonArray BuildStatusCodeTtls(int successTtlSeconds) => new()
    {
        StatusCodeRange(null, 199, -1),
        StatusCodeRange(200, 299, successTtlSeconds),
        StatusCodeRange(300, 499, 0),
        StatusCodeRange(500, null, -1)
    };

    private static JsonObject StatusCodeRange(int? from, int? to, int value)
    {
        var range = new JsonObject();
        if (from.HasValue)
            range["from"] = from.Value;
        if (to.HasValue)
            range["to"] = to.Value;

        return new JsonObject
        {
            ["status_code_range"] = range,
            ["value"] = value
        };
    }

    private static JsonObject BuildRule(string description, string expression, JsonObject actionParameters) =>
        new()
        {
            ["description"] = description,
            ["expression"] = expression,
            ["action"] = "set_cache_settings",
            ["action_parameters"] = actionParameters,
            ["enabled"] = true
        };

    internal static string EscapeExpressionString(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    /// <summary>Encodes configured URI path segments for Cloudflare Rules matching while preserving wildcard operators.</summary>
    internal static string EncodeUriPathForExpression(string value) =>
        string.Join("/", value.Split('/').Select(static segment =>
        {
            string decoded;
            try
            {
                decoded = Uri.UnescapeDataString(segment);
            }
            catch (UriFormatException)
            {
                decoded = segment;
            }

            return Uri.EscapeDataString(decoded).Replace("%2A", "*", StringComparison.OrdinalIgnoreCase);
        }));
}
