using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecuteCloudflare(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var operation = GetString(step, "operation") ?? GetString(step, "action") ?? "purge";
        operation = operation.Trim();
        var siteProfile = LoadCloudflareSiteProfile(step, baseDir);
        var baseUrl = GetString(step, "baseUrl") ?? GetString(step, "base-url") ?? siteProfile?.BaseUrl ??
                      Environment.GetEnvironmentVariable("POWERFORGE_BASE_URL");
        var urls = ReadStringList(step, "urls", "url").ToList();
        var paths = ReadStringList(step, "paths", "path").ToList();

        if (urls.Count == 0 && paths.Count == 0 && siteProfile is not null)
        {
            var profilePaths = operation.Equals("verify", StringComparison.OrdinalIgnoreCase)
                ? siteProfile.VerifyPaths
                : siteProfile.PurgePaths;
            paths.AddRange(profilePaths);
        }

        if (urls.Count == 0 && paths.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException("cloudflare: missing 'baseUrl' (required when using 'paths').");

            urls.AddRange(paths.Select(p => CombineUrl(baseUrl, p)));
        }

        if (operation.Equals("verify", StringComparison.OrdinalIgnoreCase))
        {
            ExecuteCloudflareVerify(step, baseDir, urls, stepResult);
            return;
        }

        if (!operation.Equals("purge", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"cloudflare: unsupported operation '{operation}'. Supported operations: purge, verify.");

        var zoneId = GetString(step, "zoneId") ?? GetString(step, "zone-id") ?? GetString(step, "zone");
        if (string.IsNullOrWhiteSpace(zoneId))
            throw new InvalidOperationException("cloudflare: missing required 'zoneId' (or 'zone-id').");

        var token = GetString(step, "token") ?? GetString(step, "apiToken") ?? GetString(step, "api-token");
        var tokenEnv = GetString(step, "tokenEnv") ?? GetString(step, "token-env") ??
                       GetString(step, "apiTokenEnv") ?? GetString(step, "api-token-env") ??
                       "CLOUDFLARE_API_TOKEN";
        if (string.IsNullOrWhiteSpace(token))
            token = Environment.GetEnvironmentVariable(tokenEnv);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException($"cloudflare: missing API token (set env '{tokenEnv}' or provide 'token').");

        var purgeEverything = (GetBool(step, "purgeEverything") ?? GetBool(step, "purge-everything") ?? false);
        var dryRun = (GetBool(step, "dryRun") ?? GetBool(step, "dry-run") ?? false);

        ExecuteCloudflarePurge(zoneId, token, purgeEverything, dryRun, urls, stepResult);
    }

    private static CloudflareSiteRouteProfile? LoadCloudflareSiteProfile(JsonElement step, string baseDir)
    {
        var siteConfigPath = GetString(step, "siteConfig") ?? GetString(step, "site-config") ?? GetString(step, "config");
        if (string.IsNullOrWhiteSpace(siteConfigPath))
            return null;

        var resolvedSiteConfigPath = ResolvePath(baseDir, siteConfigPath);
        if (string.IsNullOrWhiteSpace(resolvedSiteConfigPath))
            throw new InvalidOperationException($"cloudflare: unable to resolve siteConfig path '{siteConfigPath}'.");

        try
        {
            return CloudflareRouteProfileResolver.Load(resolvedSiteConfigPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"cloudflare: failed to load siteConfig '{resolvedSiteConfigPath}': {ex.Message}",
                ex);
        }
    }

    private static void ExecuteCloudflarePurge(
        string zoneId,
        string token,
        bool purgeEverything,
        bool dryRun,
        IReadOnlyList<string> urls,
        WebPipelineStepResult stepResult)
    {
        var (ok, message) = CloudflareCachePurger.Purge(
            zoneId: zoneId,
            apiToken: token,
            purgeEverything: purgeEverything,
            fileUrls: urls,
            dryRun: dryRun,
            logger: null);

        stepResult.Success = ok;
        stepResult.Message = message;
        if (!ok)
            throw new InvalidOperationException(message);
    }

    private static void ExecuteCloudflareVerify(JsonElement step, string baseDir, IReadOnlyList<string> urls, WebPipelineStepResult stepResult)
    {
        if (urls.Count == 0)
            throw new InvalidOperationException("cloudflare: missing target URLs. Provide 'urls'/'url' or 'paths'/'path' (with baseUrl).");

        var warmupRequests = GetInt(step, "warmup") ?? GetInt(step, "warmupRequests") ?? GetInt(step, "warmup-requests") ?? 1;
        warmupRequests = Math.Clamp(warmupRequests, 0, 10);

        var timeoutMs = GetInt(step, "timeoutMs") ?? GetInt(step, "timeout-ms") ?? GetInt(step, "timeout") ?? 15000;
        timeoutMs = Math.Clamp(timeoutMs, 1000, 120000);

        var allowedStatuses = ReadStringList(step, "allowStatuses", "allow-statuses", "allowStatus", "allow-status", "allow")
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (allowedStatuses.Length == 0)
            allowedStatuses = CloudflareCacheVerifier.DefaultAllowedStatuses;

        var (ok, message, entries) = CloudflareCacheVerifier.Verify(
            urls: urls,
            warmupRequests: warmupRequests,
            allowedStatuses: allowedStatuses,
            timeoutMs: timeoutMs,
            logger: null);

        var reportPath = ResolvePath(baseDir, GetString(step, "reportPath") ?? GetString(step, "report-path"));
        if (!string.IsNullOrWhiteSpace(reportPath))
            WriteCloudflareVerifyReport(reportPath, ok, message, urls.Count, warmupRequests, timeoutMs, allowedStatuses, entries);

        var summaryPath = ResolvePath(baseDir, GetString(step, "summaryPath") ?? GetString(step, "summary-path"));
        if (!string.IsNullOrWhiteSpace(summaryPath))
            WriteCloudflareVerifySummary(summaryPath, ok, message, urls.Count, warmupRequests, timeoutMs, allowedStatuses, entries);

        stepResult.Success = ok;
        stepResult.Message = message;
        if (!ok)
            throw new InvalidOperationException(message);
    }

    private static void WriteCloudflareVerifyReport(
        string reportPath,
        bool ok,
        string message,
        int urlCount,
        int warmupRequests,
        int timeoutMs,
        string[] allowedStatuses,
        CloudflareVerifyEntry[] entries)
    {
        var directory = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var payload = new
        {
            ok,
            message,
            urlCount,
            warmupRequests,
            timeoutMs,
            allowedStatuses,
            entries
        };

        File.WriteAllText(reportPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    private static void WriteCloudflareVerifySummary(
        string summaryPath,
        bool ok,
        string message,
        int urlCount,
        int warmupRequests,
        int timeoutMs,
        string[] allowedStatuses,
        IEnumerable<CloudflareVerifyEntry> entries)
    {
        var directory = Path.GetDirectoryName(summaryPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var builder = new StringBuilder();
        builder.AppendLine("# Cloudflare Cache Verify");
        builder.AppendLine();
        builder.AppendLine($"- Result: {(ok ? "pass" : "fail")}");
        builder.AppendLine($"- URLs: {urlCount}");
        builder.AppendLine($"- Allowed statuses: {string.Join(", ", allowedStatuses)}");
        builder.AppendLine($"- Warmup requests: {warmupRequests}");
        builder.AppendLine($"- Timeout (ms): {timeoutMs}");
        builder.AppendLine();
        builder.AppendLine(message);
        builder.AppendLine();
        builder.AppendLine("| URL | CF-Cache-Status | HTTP | Result |");
        builder.AppendLine("| --- | --- | ---: | --- |");

        foreach (var entry in entries)
        {
            var result = entry.Success ? "pass" : "fail";
            builder.AppendLine($"| {entry.Url} | {entry.Status} | {entry.HttpStatusCode} | {result} |");
            if (!string.IsNullOrWhiteSpace(entry.Error))
                builder.AppendLine($"|  | error: {entry.Error.Replace('|', '/')} |  |  |");
        }

        File.WriteAllText(summaryPath, builder.ToString());
    }

    private static IEnumerable<string> ReadStringList(JsonElement step, params string[] names)
    {
        foreach (var name in names)
        {
            var value = GetString(step, name);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            foreach (var token in value.Split(new[] { ',', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = token.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    yield return trimmed;
            }
        }
    }

    private static string CombineUrl(string baseUrl, string path)
    {
        var b = (baseUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(b))
            return path ?? string.Empty;

        if (!b.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !b.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            b = "https://" + b;

        b = b.TrimEnd('/');

        var p = (path ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(p))
            return b + "/";
        if (!p.StartsWith("/", StringComparison.Ordinal))
            p = "/" + p;

        return b + p;
    }
}
