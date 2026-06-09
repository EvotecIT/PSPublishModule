using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PowerForge.Web.Cli;

internal static class CloudflareCachePurger
{
    private sealed class CloudflareResponse
    {
        public bool Success { get; set; }
        public CloudflareMessage[] Errors { get; set; } = Array.Empty<CloudflareMessage>();
        public CloudflareMessage[] Messages { get; set; } = Array.Empty<CloudflareMessage>();
    }

    private sealed class CloudflareMessage
    {
        public int Code { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    internal static (bool ok, string message) Purge(
        string zoneId,
        string apiToken,
        bool purgeEverything,
        IReadOnlyList<string> fileUrls,
        bool dryRun,
        WebConsoleLogger? logger)
    {
        if (string.IsNullOrWhiteSpace(zoneId))
            return (false, "Missing zoneId.");
        if (string.IsNullOrWhiteSpace(apiToken))
            return (false, "Missing apiToken.");

        var urls = (fileUrls ?? Array.Empty<string>())
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!purgeEverything && urls.Length == 0)
            return (false, "Nothing to purge: provide at least one URL/path or use purgeEverything.");

        if (dryRun)
        {
            logger?.Info($"Cloudflare purge dry-run (zone={zoneId}, purgeEverything={purgeEverything}, urls={urls.Length}).");
            foreach (var u in urls.Take(50))
                logger?.Info($"  - {u}");
            if (urls.Length > 50)
                logger?.Info($"  ... ({urls.Length - 50} more)");
            return (true, "Dry run.");
        }

        using var http = new HttpClient
        {
            BaseAddress = new Uri("https://api.cloudflare.com/client/v4/")
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"zones/{Uri.EscapeDataString(zoneId)}/purge_cache");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiToken}");

        var payload = purgeEverything
            ? new Dictionary<string, object?> { ["purge_everything"] = true }
            : new Dictionary<string, object?> { ["files"] = urls };

        var json = JsonSerializer.Serialize(payload, WebCliJson.Options);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = http.Send(request);
        }
        catch (Exception ex)
        {
            return (false, $"Cloudflare purge request failed: {ex.GetType().Name}: {ex.Message}");
        }

        var body = string.Empty;
        try
        {
            body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // ignored: best-effort error message below
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            var text = string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase ?? "HTTP error" : body.Trim();
            return (false, $"Cloudflare purge failed (HTTP {status}): {text}");
        }

        CloudflareResponse? parsed = null;
        try
        {
            parsed = JsonSerializer.Deserialize<CloudflareResponse>(body, WebCliJson.Options);
        }
        catch
        {
            // If the API shape changes, fall back to HTTP status success.
        }

        if (parsed is not null && !parsed.Success)
        {
            var error = parsed.Errors?.FirstOrDefault()?.Message;
            if (string.IsNullOrWhiteSpace(error))
                error = parsed.Messages?.FirstOrDefault()?.Message;
            if (string.IsNullOrWhiteSpace(error))
                error = "Unknown Cloudflare API error.";
            return (false, $"Cloudflare purge failed: {error}");
        }

        return (true, purgeEverything ? "Purged everything." : $"Purged {urls.Length} URL(s).");
    }
}

