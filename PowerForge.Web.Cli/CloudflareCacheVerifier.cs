using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace PowerForge.Web.Cli;

internal sealed class CloudflareVerifyEntry
{
    public string Url { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int HttpStatusCode { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}

internal static class CloudflareCacheVerifier
{
    internal static readonly string[] DefaultAllowedStatuses = { "HIT", "REVALIDATED", "EXPIRED", "STALE" };

    internal static (bool ok, string message, CloudflareVerifyEntry[] entries) Verify(
        IReadOnlyList<string> urls,
        int warmupRequests,
        IReadOnlyCollection<string> allowedStatuses,
        int timeoutMs,
        WebConsoleLogger? logger)
    {
        var normalizedUrls = (urls ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedUrls.Length == 0)
            return (false, "No URLs provided.", Array.Empty<CloudflareVerifyEntry>());

        var allowed = (allowedStatuses ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (allowed.Length == 0)
            allowed = DefaultAllowedStatuses;

        warmupRequests = Math.Clamp(warmupRequests, 0, 10);
        timeoutMs = Math.Clamp(timeoutMs, 1000, 120000);

        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromMilliseconds(timeoutMs)
        };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "powerforge-web-cloudflare-verify");

        var entries = new List<CloudflareVerifyEntry>(normalizedUrls.Length);
        foreach (var url in normalizedUrls)
        {
            for (var i = 0; i < warmupRequests; i++)
                RequestCacheStatus(http, url, out _, out _, out _);

            var requestOk = RequestCacheStatus(http, url, out var cacheStatus, out var httpStatusCode, out var error);
            var normalizedStatus = string.IsNullOrWhiteSpace(cacheStatus)
                ? "MISSING"
                : cacheStatus.Trim().ToUpperInvariant();
            var success = requestOk &&
                          !string.IsNullOrWhiteSpace(cacheStatus) &&
                          allowed.Contains(normalizedStatus, StringComparer.OrdinalIgnoreCase);

            entries.Add(new CloudflareVerifyEntry
            {
                Url = url,
                Status = normalizedStatus,
                HttpStatusCode = httpStatusCode,
                Success = success,
                Error = error
            });

            if (success)
                logger?.Info($"{url} -> cf-cache-status={normalizedStatus} (HTTP {httpStatusCode})");
            else
                logger?.Warn($"{url} -> cf-cache-status={normalizedStatus} (HTTP {httpStatusCode}){FormatError(error)}");
        }

        var failed = entries.Where(entry => !entry.Success).ToArray();
        if (failed.Length == 0)
            return (true, $"Verified {entries.Count} URL(s): cache statuses are acceptable.", entries.ToArray());

        var preview = string.Join(", ", failed.Take(5).Select(entry => $"{entry.Url}={entry.Status}"));
        if (failed.Length > 5)
            preview += $", ... (+{failed.Length - 5} more)";

        return (false,
            $"Cloudflare cache verify failed for {failed.Length}/{entries.Count} URL(s). Allowed statuses: {string.Join(", ", allowed)}. Failing: {preview}",
            entries.ToArray());
    }

    private static bool RequestCacheStatus(
        HttpClient http,
        string url,
        out string? cacheStatus,
        out int httpStatusCode,
        out string? error)
    {
        cacheStatus = null;
        httpStatusCode = 0;
        error = null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = http.Send(request);
            httpStatusCode = (int)response.StatusCode;

            if (response.Headers.TryGetValues("cf-cache-status", out var values))
                cacheStatus = values.FirstOrDefault();

            return true;
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private static string FormatError(string? error)
    {
        return string.IsNullOrWhiteSpace(error) ? string.Empty : $" error={error}";
    }
}
