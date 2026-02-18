using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace PowerForge.Web.Cli;

internal sealed class IndexNowSubmissionOptions
{
    public IReadOnlyList<string> Urls { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Endpoints { get; set; } = Array.Empty<string>();
    public string Key { get; set; } = string.Empty;
    public string? KeyLocation { get; set; }
    public string? Host { get; set; }
    public bool DryRun { get; set; }
    public bool FailOnRequestError { get; set; } = true;
    public int BatchSize { get; set; } = 500;
    public int RetryCount { get; set; } = 2;
    public int RetryDelayMs { get; set; } = 500;
    public int TimeoutSeconds { get; set; } = 20;
}

internal sealed class IndexNowSubmissionResult
{
    public bool Success { get; set; }
    public bool DryRun { get; set; }
    public int UrlCount { get; set; }
    public int HostCount { get; set; }
    public int RequestCount { get; set; }
    public int FailedRequestCount { get; set; }
    public string[] Errors { get; set; } = Array.Empty<string>();
    public string[] Warnings { get; set; } = Array.Empty<string>();
    public IndexNowRequestResult[] Requests { get; set; } = Array.Empty<IndexNowRequestResult>();
}

internal sealed class IndexNowRequestResult
{
    public string Endpoint { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int UrlCount { get; set; }
    public bool Success { get; set; }
    public int AttemptCount { get; set; }
    public int? StatusCode { get; set; }
    public string? Error { get; set; }
    public string? ResponsePreview { get; set; }
}

internal static class IndexNowSubmitter
{
    private const string DefaultEndpoint = "https://api.indexnow.org/indexnow";
    private const int MaxResponsePreviewLength = 300;
    private const int MaxBatchSize = 10_000;
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class IndexNowPayload
    {
        public string Host { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string KeyLocation { get; set; } = string.Empty;
        public string[] UrlList { get; set; } = Array.Empty<string>();
    }

    internal static IndexNowSubmissionResult Submit(IndexNowSubmissionOptions options, WebConsoleLogger? logger)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var requests = new List<IndexNowRequestResult>();

        var endpoints = NormalizeEndpoints(options.Endpoints, warnings);
        if (endpoints.Count == 0)
            endpoints.Add(new Uri(DefaultEndpoint, UriKind.Absolute));

        var urls = NormalizeUrls(options.Urls, warnings);
        var normalizedKey = (options.Key ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedKey))
            errors.Add("indexnow: missing key.");
        else if (HasUnsafeKeyCharacters(normalizedKey))
            errors.Add("indexnow: key contains unsupported characters (path/query delimiters).");

        if (urls.Count == 0)
            warnings.Add("indexnow: no URLs to submit.");

        var batchSize = Math.Max(1, options.BatchSize);
        if (batchSize > MaxBatchSize)
        {
            warnings.Add($"indexnow: batchSize {batchSize} exceeds protocol max {MaxBatchSize}; using {MaxBatchSize}.");
            batchSize = MaxBatchSize;
        }

        var hostGroups = GroupByHost(urls, options.Host, warnings);
        var hasErrorsBeforeSubmit = errors.Count > 0;
        if (!hasErrorsBeforeSubmit)
        {
            foreach (var endpoint in endpoints)
            {
                foreach (var group in hostGroups)
                {
                    var keyLocation = ResolveKeyLocation(options.KeyLocation, group.Scheme, group.Host, normalizedKey);
                    foreach (var batch in Batch(group.Urls, batchSize))
                    {
                        IndexNowRequestResult requestResult;
                        if (options.DryRun)
                        {
                            requestResult = new IndexNowRequestResult
                            {
                                Endpoint = endpoint.ToString(),
                                Host = group.Host,
                                UrlCount = batch.Length,
                                Success = true,
                                AttemptCount = 0,
                                ResponsePreview = "dry-run"
                            };
                        }
                        else
                        {
                            requestResult = SubmitBatch(
                                endpoint,
                                group.Host,
                                normalizedKey,
                                keyLocation,
                                batch,
                                Math.Max(0, options.RetryCount),
                                Math.Max(0, options.RetryDelayMs),
                                Math.Max(1, options.TimeoutSeconds));
                        }

                        requests.Add(requestResult);

                        if (!requestResult.Success)
                        {
                            var endpointLabel = requestResult.Endpoint;
                            var statusLabel = requestResult.StatusCode.HasValue ? $"HTTP {requestResult.StatusCode.Value}" : "transport";
                            var errorText = string.IsNullOrWhiteSpace(requestResult.Error) ? "unknown error" : requestResult.Error;
                            errors.Add($"indexnow: {statusLabel} failure for {endpointLabel} ({requestResult.Host}): {errorText}");
                        }
                    }
                }
            }
        }

        var failedRequestCount = requests.Count(request => !request.Success);
        var success = options.FailOnRequestError ? errors.Count == 0 : true;

        if (options.DryRun)
        {
            logger?.Info($"IndexNow dry-run: urls={urls.Count}, hosts={hostGroups.Count}, requests={requests.Count}.");
        }

        return new IndexNowSubmissionResult
        {
            Success = success,
            DryRun = options.DryRun,
            UrlCount = urls.Count,
            HostCount = hostGroups.Count,
            RequestCount = requests.Count,
            FailedRequestCount = failedRequestCount,
            Errors = errors.ToArray(),
            Warnings = warnings.ToArray(),
            Requests = requests.ToArray()
        };
    }

    private static List<Uri> NormalizeEndpoints(IReadOnlyList<string> values, List<string> warnings)
    {
        var endpoints = new List<Uri>();
        foreach (var value in values ?? Array.Empty<string>())
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
                !(uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                  uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add($"indexnow: endpoint ignored (invalid URL): {trimmed}");
                continue;
            }

            endpoints.Add(uri);
        }

        return endpoints
            .Distinct()
            .ToList();
    }

    private static List<Uri> NormalizeUrls(IReadOnlyList<string> values, List<string> warnings)
    {
        var urls = new List<Uri>();
        foreach (var value in values ?? Array.Empty<string>())
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
                !(uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                  uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add($"indexnow: URL ignored (must be absolute http/https): {trimmed}");
                continue;
            }

            var builder = new UriBuilder(uri)
            {
                Fragment = string.Empty
            };
            var normalized = builder.Uri;
            if (IsNonPublicSubmissionUrl(normalized, out var reason))
            {
                warnings.Add($"indexnow: URL ignored ({reason}): {trimmed}");
                continue;
            }

            urls.Add(normalized);
        }

        return urls
            .Distinct()
            .ToList();
    }

    private static List<HostGroup> GroupByHost(IReadOnlyList<Uri> urls, string? overrideHost, List<string> warnings)
    {
        var groups = new List<HostGroup>();
        foreach (var grouping in urls.GroupBy(uri => uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            var host = grouping.Key;
            if (!string.IsNullOrWhiteSpace(overrideHost) &&
                !string.Equals(host, overrideHost.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"indexnow: URL host '{host}' does not match configured host '{overrideHost}'.");
            }

            var scheme = grouping
                .Select(uri => uri.Scheme)
                .FirstOrDefault(static s => !string.IsNullOrWhiteSpace(s)) ?? Uri.UriSchemeHttps;
            var batchUrls = grouping
                .Select(uri => uri.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            groups.Add(new HostGroup(host, scheme, batchUrls));
        }

        if (groups.Count == 0 &&
            !string.IsNullOrWhiteSpace(overrideHost))
        {
            groups.Add(new HostGroup(overrideHost.Trim(), Uri.UriSchemeHttps, Array.Empty<string>()));
        }

        return groups;
    }

    private static string ResolveKeyLocation(string? keyLocation, string scheme, string host, string key)
    {
        if (!string.IsNullOrWhiteSpace(keyLocation))
        {
            if (Uri.TryCreate(keyLocation, UriKind.Absolute, out var absolute))
                return absolute.ToString();

            var relative = keyLocation.Trim().TrimStart('/');
            return $"{scheme}://{host}/{relative}";
        }

        var keyFileName = key.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? key : key + ".txt";
        return $"{scheme}://{host}/{Uri.EscapeDataString(keyFileName)}";
    }

    private static bool HasUnsafeKeyCharacters(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return true;

        foreach (var ch in key)
        {
            if (ch is '/' or '\\' or '?' or '#' or '&')
                return true;
        }

        return false;
    }

    private static bool IsNonPublicSubmissionUrl(Uri uri, out string reason)
    {
        reason = string.Empty;
        if (uri is null)
        {
            reason = "invalid URI";
            return true;
        }

        if (uri.IsLoopback)
        {
            reason = "loopback host";
            return true;
        }

        var host = uri.DnsSafeHost;
        if (string.IsNullOrWhiteSpace(host))
        {
            reason = "missing host";
            return true;
        }

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            reason = "local host";
            return true;
        }

        if (IPAddress.TryParse(host, out var ip) && IsNonPublicIp(ip))
        {
            reason = "private/link-local IP";
            return true;
        }

        return false;
    }

    private static bool IsNonPublicIp(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
            return true;

        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6Multicast || ip.IsIPv6SiteLocal)
                return true;

            var bytesV6 = ip.GetAddressBytes();
            // fc00::/7 unique-local addresses.
            if ((bytesV6[0] & 0xFE) == 0xFC)
                return true;

            return false;
        }

        if (ip.AddressFamily != AddressFamily.InterNetwork)
            return true;

        var bytes = ip.GetAddressBytes();
        var first = bytes[0];
        var second = bytes[1];

        // 10.0.0.0/8
        if (first == 10) return true;
        // 127.0.0.0/8
        if (first == 127) return true;
        // 0.0.0.0/8
        if (first == 0) return true;
        // 169.254.0.0/16 (link-local)
        if (first == 169 && second == 254) return true;
        // 172.16.0.0/12
        if (first == 172 && second >= 16 && second <= 31) return true;
        // 192.168.0.0/16
        if (first == 192 && second == 168) return true;
        // 100.64.0.0/10 (carrier-grade NAT)
        if (first == 100 && second >= 64 && second <= 127) return true;

        return false;
    }

    private static IEnumerable<string[]> Batch(IReadOnlyList<string> values, int size)
    {
        if (values.Count == 0)
            yield break;

        for (var i = 0; i < values.Count; i += size)
        {
            var count = Math.Min(size, values.Count - i);
            var batch = new string[count];
            for (var j = 0; j < count; j++)
                batch[j] = values[i + j];
            yield return batch;
        }
    }

    private static IndexNowRequestResult SubmitBatch(
        Uri endpoint,
        string host,
        string key,
        string keyLocation,
        string[] urls,
        int retryCount,
        int retryDelayMs,
        int timeoutSeconds)
    {
        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };

        var attempts = 0;
        Exception? lastException = null;
        int? lastStatusCode = null;
        string? lastReasonPhrase = null;
        string? lastBody = null;
        var maxAttempts = Math.Max(1, retryCount + 1);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            attempts = attempt;
            lastException = null;
            lastStatusCode = null;
            lastReasonPhrase = null;
            lastBody = null;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                var payload = new IndexNowPayload
                {
                    Host = host,
                    Key = key,
                    KeyLocation = keyLocation,
                    UrlList = urls
                };
                var json = JsonSerializer.Serialize(payload, PayloadJsonOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = http.Send(request);
                lastStatusCode = (int)response.StatusCode;
                lastReasonPhrase = response.ReasonPhrase;
                lastBody = response.Content is null
                    ? null
                    : response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (response.IsSuccessStatusCode)
                {
                    return new IndexNowRequestResult
                    {
                        Endpoint = endpoint.ToString(),
                        Host = host,
                        UrlCount = urls.Length,
                        Success = true,
                        AttemptCount = attempts,
                        StatusCode = lastStatusCode,
                        ResponsePreview = TruncateResponse(lastBody)
                    };
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            if (attempt < maxAttempts && retryDelayMs > 0)
                Thread.Sleep(retryDelayMs);
        }

        var errorText = lastException?.Message;
        if (string.IsNullOrWhiteSpace(errorText) && lastStatusCode.HasValue)
        {
            errorText = string.IsNullOrWhiteSpace(lastBody)
                ? lastReasonPhrase
                : lastBody;
        }

        return new IndexNowRequestResult
        {
            Endpoint = endpoint.ToString(),
            Host = host,
            UrlCount = urls.Length,
            Success = false,
            AttemptCount = attempts,
            StatusCode = lastStatusCode,
            Error = string.IsNullOrWhiteSpace(errorText) ? "request failed" : TruncateResponse(errorText),
            ResponsePreview = TruncateResponse(lastBody)
        };
    }

    private static string? TruncateResponse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var normalized = text.Trim();
        if (normalized.Length <= MaxResponsePreviewLength)
            return normalized;

        return normalized.Substring(0, MaxResponsePreviewLength) + "...";
    }

    private sealed record HostGroup(string Host, string Scheme, string[] Urls);
}
