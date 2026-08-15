using System.Net.Http;
using System.Text.Json;

namespace PowerForge.Web.Cli;

internal sealed class CloudflareIncrementalPurgeResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public CloudflareCachePurgeMode ActualMode { get; init; }
    public int TargetCount { get; init; }
    public int RequestCount { get; init; }
    public bool UsedFallback { get; init; }
    public string? FallbackReason { get; init; }
}

internal static class CloudflareIncrementalCachePurger
{
    internal const int MaxIncrementalTargets = 500;
    private const int FileTargetsPerRequest = 100;

    internal static CloudflareIncrementalPurgeResult Purge(
        string zoneId,
        string apiToken,
        string baseUrl,
        string currentManifestPath,
        string? previousManifestPath,
        bool dryRun,
        WebConsoleLogger? logger,
        HttpClient? httpClient = null,
        string? forcedHostnameFallbackReason = null)
    {
        CloudflareDeploymentManifest current;
        string normalizedBaseUrl;
        try
        {
            normalizedBaseUrl = CloudflareDeploymentManifestStore.NormalizeBaseUrl(baseUrl);
            current = CloudflareDeploymentManifestStore.LoadRequired(currentManifestPath);
            if (!current.BaseUrl.Equals(normalizedBaseUrl, StringComparison.Ordinal))
                return Failure($"Current deployment manifest BaseUrl '{current.BaseUrl}' does not match configured site BaseUrl '{normalizedBaseUrl}'.");
            if (!CloudflareDeploymentManifestStore.IsValidPolicyFingerprint(current.CachePolicyFingerprint))
                return Failure("Current deployment manifest has no valid cache-policy fingerprint.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException)
        {
            return Failure($"Current deployment manifest is invalid: {ex.Message}");
        }

        var previousAvailable = TryLoadPrevious(previousManifestPath, out var previous, out var fallbackReason);
        var fallbackBaseUrls = new[] { normalizedBaseUrl, previous?.BaseUrl };
        if (!string.IsNullOrWhiteSpace(forcedHostnameFallbackReason))
            return PurgeHostnameFallback(zoneId, apiToken, fallbackBaseUrls, dryRun, forcedHostnameFallbackReason.Trim(), logger, httpClient);
        if (!previousAvailable)
            return PurgeHostnameFallback(zoneId, apiToken, fallbackBaseUrls, dryRun, fallbackReason, logger, httpClient);

        if (!previous!.BaseUrl.Equals(normalizedBaseUrl, StringComparison.Ordinal))
            return PurgeHostnameFallback(
                zoneId,
                apiToken,
                fallbackBaseUrls,
                dryRun,
                $"previous manifest BaseUrl '{previous.BaseUrl}' does not match '{normalizedBaseUrl}'",
                logger,
                httpClient);

        if (!CloudflareDeploymentManifestStore.IsValidPolicyFingerprint(previous.CachePolicyFingerprint))
            return PurgeHostnameFallback(zoneId, apiToken, fallbackBaseUrls, dryRun, "the previous deployment manifest has no cache-policy fingerprint", logger, httpClient);
        if (!current.CachePolicyFingerprint.Equals(previous.CachePolicyFingerprint, StringComparison.OrdinalIgnoreCase))
            return PurgeHostnameFallback(zoneId, apiToken, fallbackBaseUrls, dryRun, "the managed cache policy changed", logger, httpClient);

        var previousFiles = previous.Files.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        var currentFiles = current.Files.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        var changedPaths = previousFiles.Keys
            .Concat(currentFiles.Keys)
            .Distinct(StringComparer.Ordinal)
            .Where(path => !previousFiles.TryGetValue(path, out var oldEntry) ||
                           !currentFiles.TryGetValue(path, out var newEntry) ||
                           oldEntry.Length != newEntry.Length ||
                           !oldEntry.Sha256.Equals(newEntry.Sha256, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (changedPaths.Length == 0)
        {
            return new CloudflareIncrementalPurgeResult
            {
                Success = true,
                Message = "Deployment manifest is unchanged; no Cloudflare purge was required.",
                ActualMode = CloudflareCachePurgeMode.Files
            };
        }

        if (changedPaths.Length > MaxIncrementalTargets)
        {
            return PurgeHostnameFallback(
                zoneId,
                apiToken,
                fallbackBaseUrls,
                dryRun,
                $"incremental diff contains {changedPaths.Length} URL paths, exceeding the {MaxIncrementalTargets} target safety limit",
                logger,
                httpClient);
        }

        string[] urls;
        try
        {
            urls = changedPaths
                .Select(path => CloudflareDeploymentManifestStore.ResolveUrl(normalizedBaseUrl, path).AbsoluteUri)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
        catch (InvalidDataException ex)
        {
            return Failure($"Deployment manifest diff contains an unsafe URL path: {ex.Message}");
        }

        var ownsHttpClient = httpClient is null;
        httpClient ??= new HttpClient { BaseAddress = new Uri("https://api.cloudflare.com/client/v4/") };
        try
        {
            var requestCount = 0;
            var plannedBatchCount = (urls.Length + FileTargetsPerRequest - 1) / FileTargetsPerRequest;
            foreach (var batch in urls.Chunk(FileTargetsPerRequest))
            {
                var (ok, message, requestAttempted) = CloudflareCachePurger.Purge(
                    zoneId,
                    apiToken,
                    CloudflareCachePurgeMode.Files,
                    batch,
                    dryRun,
                    logger,
                    httpClient);
                if (requestAttempted)
                    requestCount++;
                if (!ok)
                    return Failure($"Incremental Cloudflare purge stopped after {requestCount} request attempt(s): {message}", urls.Length, requestCount);
            }

            return new CloudflareIncrementalPurgeResult
            {
                Success = true,
                Message = dryRun
                    ? $"Incremental purge dry-run selected {urls.Length} changed URL(s) in {plannedBatchCount} planned batch(es)."
                    : $"Incrementally purged {urls.Length} changed URL(s) in {requestCount} batch(es).",
                ActualMode = CloudflareCachePurgeMode.Files,
                TargetCount = urls.Length,
                RequestCount = requestCount
            };
        }
        finally
        {
            if (ownsHttpClient)
                httpClient.Dispose();
        }
    }

    private static bool TryLoadPrevious(
        string? previousManifestPath,
        out CloudflareDeploymentManifest? previous,
        out string fallbackReason)
    {
        previous = null;
        if (string.IsNullOrWhiteSpace(previousManifestPath))
        {
            fallbackReason = "no last-successfully-deployed manifest is available";
            return false;
        }

        try
        {
            if (!File.Exists(Path.GetFullPath(previousManifestPath)))
            {
                fallbackReason = "no last-successfully-deployed manifest is available";
                return false;
            }

            previous = CloudflareDeploymentManifestStore.LoadRequired(previousManifestPath);
            fallbackReason = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException)
        {
            fallbackReason = $"the previous deployment manifest is invalid ({ex.Message})";
            previous = null;
            return false;
        }
    }

    private static CloudflareIncrementalPurgeResult PurgeHostnameFallback(
        string zoneId,
        string apiToken,
        IEnumerable<string?> baseUrls,
        bool dryRun,
        string reason,
        WebConsoleLogger? logger,
        HttpClient? httpClient)
    {
        var hostnames = baseUrls
            .Where(baseUrl => !string.IsNullOrWhiteSpace(baseUrl))
            .Select(baseUrl => new Uri(CloudflareDeploymentManifestStore.NormalizeBaseUrl(baseUrl!)).Host)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var (ok, message, requestAttempted) = CloudflareCachePurger.Purge(
            zoneId,
            apiToken,
            CloudflareCachePurgeMode.Hostname,
            hostnames,
            dryRun,
            logger,
            httpClient);
        return new CloudflareIncrementalPurgeResult
        {
            Success = ok,
            Message = ok
                ? $"Incremental purge used hostname fallback because {reason}. {message}"
                : $"Incremental purge hostname fallback failed because {reason}. {message}",
            ActualMode = CloudflareCachePurgeMode.Hostname,
            TargetCount = hostnames.Length,
            RequestCount = requestAttempted ? 1 : 0,
            UsedFallback = true,
            FallbackReason = reason
        };
    }

    private static CloudflareIncrementalPurgeResult Failure(string message, int targetCount = 0, int requestCount = 0) => new()
    {
        Success = false,
        Message = message,
        ActualMode = CloudflareCachePurgeMode.Files,
        TargetCount = targetCount,
        RequestCount = requestCount
    };
}
