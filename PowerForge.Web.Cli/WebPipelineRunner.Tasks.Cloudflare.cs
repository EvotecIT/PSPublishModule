using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecuteCloudflare(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
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
        var baseUrl = GetString(step, "baseUrl") ?? GetString(step, "base-url");

        var urls = ReadStringList(step, "urls", "url").ToList();
        var paths = ReadStringList(step, "paths", "path").ToArray();
        if (urls.Count == 0 && paths.Length > 0)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException("cloudflare: missing 'baseUrl' (required when using 'paths').");

            urls.AddRange(paths.Select(p => CombineUrl(baseUrl, p)));
        }

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

