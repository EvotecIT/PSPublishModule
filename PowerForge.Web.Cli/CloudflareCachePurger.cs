using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace PowerForge.Web.Cli;

internal enum CloudflareCachePurgeMode
{
    Files,
    Incremental,
    Hostname,
    Everything
}

internal static class CloudflareCachePurger
{
    private const int MaxFileTargets = 100;
    private const int MaxHostnameTargets = 30;

    internal static (bool ok, string message) Purge(
        string zoneId,
        string apiToken,
        CloudflareCachePurgeMode mode,
        IReadOnlyList<string> targets,
        bool dryRun,
        WebConsoleLogger? logger,
        HttpClient? httpClient = null)
    {
        var normalizedZoneId = (zoneId ?? string.Empty).Trim();
        if (normalizedZoneId.Length != 32 || normalizedZoneId.Any(character => !Uri.IsHexDigit(character)))
            return (false, "Cloudflare zoneId must be a 32-character hexadecimal identifier.");
        if (string.IsNullOrWhiteSpace(apiToken))
            return (false, "Missing apiToken.");
        if (mode == CloudflareCachePurgeMode.Incremental)
            return (false, "Incremental purge requires current and previous deployment manifests.");

        var normalizedTargets = (targets ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (mode != CloudflareCachePurgeMode.Everything && normalizedTargets.Length == 0)
            return (false, $"Nothing to purge in {FormatMode(mode)} mode.");

        if (mode == CloudflareCachePurgeMode.Files)
        {
            if (normalizedTargets.Length > MaxFileTargets)
                return (false, $"Cloudflare file purge accepts at most {MaxFileTargets} URLs per request.");

            foreach (var target in normalizedTargets)
            {
                if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                    string.IsNullOrWhiteSpace(uri.Host))
                    return (false, $"Cloudflare file purge target must be an absolute HTTP or HTTPS URL: '{target}'.");
            }
        }
        else if (mode == CloudflareCachePurgeMode.Hostname)
        {
            try
            {
                normalizedTargets = normalizedTargets
                    .Select(CloudflareCachePolicyBuilder.NormalizeHostname)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (ArgumentException ex)
            {
                return (false, ex.Message);
            }

            if (normalizedTargets.Length > MaxHostnameTargets)
                return (false, $"Cloudflare hostname purge accepts at most {MaxHostnameTargets} hostnames per request.");
        }

        if (dryRun)
        {
            logger?.Info($"Cloudflare purge dry-run (zone={normalizedZoneId}, mode={FormatMode(mode)}, targets={normalizedTargets.Length}).");
            foreach (var target in normalizedTargets.Take(50))
                logger?.Info($"  - {target}");
            if (normalizedTargets.Length > 50)
                logger?.Info($"  ... ({normalizedTargets.Length - 50} more)");
            return (true, "Dry run.");
        }

        var payload = mode switch
        {
            CloudflareCachePurgeMode.Everything => new JsonObject { ["purge_everything"] = true },
            CloudflareCachePurgeMode.Hostname => new JsonObject { ["hosts"] = BuildTargetArray(normalizedTargets) },
            _ => new JsonObject { ["files"] = BuildTargetArray(normalizedTargets) }
        };

        var ownsHttpClient = httpClient is null;
        httpClient ??= new HttpClient { BaseAddress = new Uri("https://api.cloudflare.com/client/v4/") };
        try
        {
            var response = CloudflareApiClient.Send(
                httpClient,
                HttpMethod.Post,
                $"zones/{normalizedZoneId}/purge_cache",
                apiToken,
                payload);
            if (!response.Success)
                return (false, $"Cloudflare purge failed: {response.ErrorMessage}");

            return mode switch
            {
                CloudflareCachePurgeMode.Everything => (true, "Purged everything."),
                CloudflareCachePurgeMode.Hostname => (true, $"Purged {normalizedTargets.Length} hostname(s)."),
                _ => (true, $"Purged {normalizedTargets.Length} URL(s).")
            };
        }
        finally
        {
            if (ownsHttpClient)
                httpClient.Dispose();
        }
    }

    internal static bool TryParseMode(string? raw, out CloudflareCachePurgeMode mode)
    {
        switch ((raw ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "":
            case "files":
                mode = CloudflareCachePurgeMode.Files;
                return true;
            case "incremental":
            case "manifest":
                mode = CloudflareCachePurgeMode.Incremental;
                return true;
            case "hostname":
            case "host":
            case "hosts":
                mode = CloudflareCachePurgeMode.Hostname;
                return true;
            case "everything":
            case "all":
                mode = CloudflareCachePurgeMode.Everything;
                return true;
            default:
                mode = default;
                return false;
        }
    }

    internal static bool TryParseCanonicalMode(string? raw, out CloudflareCachePurgeMode mode)
    {
        switch (raw)
        {
            case "files":
                mode = CloudflareCachePurgeMode.Files;
                return true;
            case "incremental":
                mode = CloudflareCachePurgeMode.Incremental;
                return true;
            case "hostname":
                mode = CloudflareCachePurgeMode.Hostname;
                return true;
            case "everything":
                mode = CloudflareCachePurgeMode.Everything;
                return true;
            default:
                mode = default;
                return false;
        }
    }

    internal static string FormatMode(CloudflareCachePurgeMode mode) => mode switch
    {
        CloudflareCachePurgeMode.Incremental => "incremental",
        CloudflareCachePurgeMode.Hostname => "hostname",
        CloudflareCachePurgeMode.Everything => "everything",
        _ => "files"
    };

    private static JsonArray BuildTargetArray(IEnumerable<string> targets)
    {
        var array = new JsonArray();
        foreach (var target in targets)
            array.Add(target);
        return array;
    }
}
