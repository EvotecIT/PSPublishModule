using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace PowerForge.Web;

/// <summary>Collects bounded daily end-user HTTP traffic from Cloudflare GraphQL Analytics.</summary>
public sealed class CloudflareAnalyticsCollector
{
    /// <summary>Fleet provider kind handled by this collector.</summary>
    public const string ProviderKind = "cloudflare-analytics";
    private const int ClientMaximumRowsPerDay = 10_000;
    private static readonly Uri DefaultEndpoint = new("https://api.cloudflare.com/client/v4/graphql");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly ICloudflareAnalyticsTokenProvider _tokenProvider;
    private readonly Uri _endpoint;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a Cloudflare GraphQL Analytics collector.</summary>
    public CloudflareAnalyticsCollector(
        HttpClient httpClient,
        ICloudflareAnalyticsTokenProvider tokenProvider,
        Uri? endpoint = null,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        _endpoint = endpoint ?? DefaultEndpoint;
        _timeProvider = timeProvider ?? TimeProvider.System;
        if (!_endpoint.IsAbsoluteUri || _endpoint.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Cloudflare GraphQL endpoint must be absolute HTTPS.", nameof(endpoint));
    }

    /// <summary>Checks token access and discovers plan-specific dataset limits for the configured zone.</summary>
    public async Task<CloudflareAnalyticsCapabilityProbeResult> ProbeAsync(
        string zoneId,
        string siteBaseUrl,
        CancellationToken cancellationToken = default)
    {
        ValidateZoneId(zoneId);
        var siteHost = NormalizeSiteHost(siteBaseUrl);
        string token;
        try
        {
            token = await _tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return ProbeFailure(0, "credential-unavailable", "Cloudflare analytics credential resolution failed.");
        }

        var zoneResponse = await GetZoneAsync(zoneId, token, cancellationToken).ConfigureAwait(false);
        if (!zoneResponse.Success)
            return ProbeFailure(1, zoneResponse.ErrorCode!, zoneResponse.ErrorMessage!);
        var zoneName = NormalizeZoneName(zoneResponse.Value?.Name);
        if (zoneResponse.Value is null || !string.Equals(zoneResponse.Value.Id, zoneId, StringComparison.OrdinalIgnoreCase) || zoneName is null)
            return ProbeFailure(1, "invalid-response", "Cloudflare returned invalid zone identity details.");
        if (!HostBelongsToZone(siteHost, zoneName))
            return ProbeFailure(1, "zone-site-mismatch", "The configured Cloudflare zone does not own the fleet site host.");

        var response = await SendAsync<CloudflareCapabilityData>(CapabilityQuery, new { zoneTag = zoneId.ToLowerInvariant() }, token, cancellationToken)
            .ConfigureAwait(false);
        if (!response.Success)
            return ProbeFailure(2, response.ErrorCode!, response.ErrorMessage!);
        var zones = response.Value?.Viewer?.Zones ?? Array.Empty<CloudflareCapabilityZone>();
        if (zones.Length != 1)
            return ProbeFailure(2, "zone-not-visible", "The configured Cloudflare zone is not visible to this credential.");
        if (zones[0] is null)
            return ProbeFailure(2, "invalid-response", "Cloudflare returned an invalid analytics capability response.");
        var settings = zones[0].Settings?.HttpRequestsAdaptiveGroups;
        if (settings?.Enabled != true)
            return ProbeFailure(2, "dataset-unavailable", "Cloudflare httpRequestsAdaptiveGroups is not enabled for this zone.");
        if (settings.MaxPageSize is null or <= 0)
            return ProbeFailure(2, "invalid-response", "Cloudflare did not report a usable analytics page size.");
        if (settings.MaxDuration is <= 0 || settings.NotOlderThan is <= 0)
            return ProbeFailure(2, "invalid-response", "Cloudflare reported an invalid analytics duration or retention boundary.");

        return new CloudflareAnalyticsCapabilityProbeResult
        {
            Success = true,
            DatasetEnabled = true,
            ZoneName = zoneName,
            RequestCount = 2,
            MaxPageSize = Math.Min(settings.MaxPageSize.Value, ClientMaximumRowsPerDay),
            MaxDurationSeconds = settings.MaxDuration,
            NotOlderThanSeconds = settings.NotOlderThan
        };
    }

    /// <summary>Collects one bounded GraphQL partition per UTC reporting date.</summary>
    public async Task<CloudflareAnalyticsCollectionResult> CollectAsync(
        CloudflareAnalyticsCollectionOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        var probe = await ProbeAsync(options.ZoneId, options.SiteBaseUrl, cancellationToken).ConfigureAwait(false);
        if (!probe.Success)
            return Failure(options, probe, probe.RequestCount, Array.Empty<WebTrafficObservation>(), Array.Empty<DateOnly>(), options.FromDate, probe.ErrorCode!, probe.ErrorMessage!);

        var firstPartitionStart = new DateTimeOffset(options.FromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        if (probe.NotOlderThanSeconds is > 0 &&
            firstPartitionStart < _timeProvider.GetUtcNow().Subtract(TimeSpan.FromSeconds(probe.NotOlderThanSeconds.Value)))
        {
            return Failure(options, probe, probe.RequestCount, Array.Empty<WebTrafficObservation>(), Array.Empty<DateOnly>(), options.FromDate,
                "retention-boundary", "The requested Cloudflare traffic range starts before the provider-reported retention boundary.");
        }
        if (probe.MaxDurationSeconds is > 0 and < 86_400)
        {
            return Failure(options, probe, probe.RequestCount, Array.Empty<WebTrafficObservation>(), Array.Empty<DateOnly>(), options.FromDate,
                "duration-boundary", "The provider-reported query duration cannot cover a complete UTC traffic partition.");
        }

        string token;
        try
        {
            token = await _tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Failure(options, probe, probe.RequestCount, Array.Empty<WebTrafficObservation>(), Array.Empty<DateOnly>(), options.FromDate, "credential-unavailable", "Cloudflare analytics credential resolution failed.");
        }

        var observations = new List<WebTrafficObservation>();
        var completedDates = new List<DateOnly>();
        var requestCount = probe.RequestCount;
        for (var date = options.FromDate;; date = date.AddDays(1))
        {
            var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var end = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var variables = new
            {
                zoneTag = options.ZoneId.ToLowerInvariant(),
                limit = probe.MaxPageSize,
                filter = BuildTrafficFilter(options.SiteBaseUrl, start, end)
            };
            var response = await SendAsync<CloudflareTrafficData>(TrafficQuery, variables, token, cancellationToken).ConfigureAwait(false);
            requestCount++;
            if (!response.Success)
                return Failure(options, probe, requestCount, observations, completedDates, date, response.ErrorCode!, response.ErrorMessage!);
            var zones = response.Value?.Viewer?.Zones ?? Array.Empty<CloudflareTrafficZone?>();
            if (zones.Length != 1)
                return Failure(options, probe, requestCount, observations, completedDates, date, "zone-not-visible", "Cloudflare did not return the configured zone.");
            if (zones[0] is not { } zone)
                return Failure(options, probe, requestCount, observations, completedDates, date, "invalid-response", "Cloudflare returned an invalid zone result.");
            var rows = zone.Traffic;
            if (rows is null)
                return Failure(options, probe, requestCount, observations, completedDates, date, "invalid-response", "Cloudflare returned no traffic dataset for the configured zone.");
            if (!TryMapRows(rows, options, date, out var mapped))
                return Failure(options, probe, requestCount, observations, completedDates, date, "invalid-response", "Cloudflare returned invalid traffic analytics rows.");
            observations.AddRange(mapped);
            if (rows.Length >= probe.MaxPageSize)
                return Failure(options, probe, requestCount, observations, completedDates, date, "row-limit-reached", "Cloudflare reached the daily row limit, so this partition cannot be marked complete.");
            completedDates.Add(date);
            if (date == options.ThroughDate)
                break;
        }

        var batch = WebTrafficObservationNormalizer.Normalize(new WebTrafficObservationBatch
        {
            Provider = options.ProviderId,
            SiteId = options.SiteId,
            CollectedAtUtc = _timeProvider.GetUtcNow(),
            SourceKind = "api",
            Status = "complete",
            ConfigurationHash = options.ConfigurationHash,
            EvidenceReference = options.EvidenceReference,
            ZeroDataConfirmed = observations.Count == 0,
            CollectionCoverage = new WebTrafficObservationCollectionCoverage
            {
                FromDate = options.FromDate,
                ThroughDate = options.ThroughDate,
                CompletedDates = completedDates.ToArray()
            },
            Observations = observations.ToArray()
        });
        return new CloudflareAnalyticsCollectionResult
        {
            Success = true,
            RequestCount = requestCount,
            CompletedDateCount = completedDates.Count,
            Probe = probe,
            Batch = batch
        };
    }

    private static bool TryMapRows(
        IReadOnlyCollection<CloudflareTrafficGroup?> rows,
        CloudflareAnalyticsCollectionOptions options,
        DateOnly requestedDate,
        out WebTrafficObservation[] observations)
    {
        var mapped = new List<WebTrafficObservation>(rows.Count);
        var siteUri = new Uri(options.SiteBaseUrl, UriKind.Absolute);
        var siteHost = siteUri.IdnHost.TrimEnd('.').ToLowerInvariant();
        var siteScheme = siteUri.Scheme.ToLowerInvariant();
        var sitePath = NormalizeSitePath(siteUri.AbsolutePath);
        foreach (var row in rows)
        {
            if (row is null)
            {
                observations = Array.Empty<WebTrafficObservation>();
                return false;
            }
            var dimensions = row.Dimensions;
            if (row.Count is null || row.Sum?.Visits is null || row.Sum.EdgeResponseBytes is null ||
                row.Average?.SampleInterval is null || dimensions?.Date != requestedDate ||
                string.IsNullOrWhiteSpace(dimensions.Host) || string.IsNullOrWhiteSpace(dimensions.Path) ||
                string.IsNullOrWhiteSpace(dimensions.Scheme) ||
                row.Count > long.MaxValue || row.Sum.Visits > long.MaxValue || row.Sum.EdgeResponseBytes > long.MaxValue ||
                !double.IsFinite(row.Average.SampleInterval.Value) || row.Average.SampleInterval.Value < 1d)
            {
                observations = Array.Empty<WebTrafficObservation>();
                return false;
            }
            if (!TryScaleSampledCount(row.Count.Value, row.Average.SampleInterval.Value, out var requests))
            {
                observations = Array.Empty<WebTrafficObservation>();
                return false;
            }
            if (!IsValidRequestPath(dimensions.Path) || !TryNormalizeHostDimension(dimensions.Host, out var rowHost))
            {
                observations = Array.Empty<WebTrafficObservation>();
                return false;
            }
            if (!rowHost.Equals(siteHost, StringComparison.Ordinal) ||
                !dimensions.Scheme.Trim().Equals(siteScheme, StringComparison.OrdinalIgnoreCase) ||
                !PathBelongsToSite(dimensions.Path, sitePath))
            {
                observations = Array.Empty<WebTrafficObservation>();
                return false;
            }
            mapped.Add(new WebTrafficObservation
            {
                Date = requestedDate,
                Host = rowHost,
                Path = dimensions.Path,
                Requests = requests,
                Visits = (long)row.Sum.Visits.Value,
                EdgeResponseBytes = (long)row.Sum.EdgeResponseBytes.Value,
                SampleInterval = row.Average.SampleInterval.Value,
                EvidenceReference = options.EvidenceReference
            });
        }
        observations = mapped.ToArray();
        return observations
            .GroupBy(observation => (observation.Date, observation.Host, observation.Path))
            .All(group => group.Count() == 1);
    }

    private static Dictionary<string, object?> BuildTrafficFilter(string siteBaseUrl, DateTime start, DateTime end)
    {
        var siteUri = new Uri(siteBaseUrl, UriKind.Absolute);
        var sitePath = NormalizeSitePath(siteUri.AbsolutePath);
        var filter = new Dictionary<string, object?>
        {
            ["datetime_geq"] = start.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["datetime_lt"] = end.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["requestSource"] = "eyeball",
            ["clientRequestHTTPHost"] = siteUri.IdnHost.TrimEnd('.').ToLowerInvariant(),
            ["clientRequestScheme"] = siteUri.Scheme.ToLowerInvariant()
        };
        if (sitePath != "/")
        {
            var exactPath = sitePath.TrimEnd('/');
            filter["OR"] = new object[]
            {
                new Dictionary<string, string> { ["clientRequestPath"] = exactPath },
                new Dictionary<string, string> { ["clientRequestPath_like"] = exactPath + "/%" }
            };
        }
        return filter;
    }

    private static string NormalizeSitePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
            return "/";
        return "/" + path.Trim('/') + "/";
    }

    private static bool PathBelongsToSite(string path, string sitePath)
    {
        if (sitePath == "/")
            return true;
        var prefix = sitePath.TrimEnd('/');
        return path.Equals(prefix, StringComparison.Ordinal) || path.StartsWith(prefix + "/", StringComparison.Ordinal);
    }

    private static bool IsValidRequestPath(string path) =>
        path.Equals(path.Trim(), StringComparison.Ordinal) &&
        path.StartsWith("/", StringComparison.Ordinal) &&
        !path.Contains('#') &&
        !path.Any(char.IsControl);

    private static bool TryNormalizeHostDimension(string value, out string host)
    {
        host = string.Empty;
        var candidate = value.Trim();
        if (!candidate.Equals(value, StringComparison.Ordinal) ||
            candidate.Length == 0 ||
            candidate.Any(char.IsWhiteSpace) ||
            candidate.Any(char.IsControl) ||
            candidate.IndexOfAny(['/', '\\', ':', '@', '?', '#']) >= 0)
        {
            return false;
        }

        candidate = candidate.TrimEnd('.');
        if (candidate.Length == 0 ||
            !Uri.TryCreate("https://" + candidate + "/", UriKind.Absolute, out var uri) ||
            Uri.CheckHostName(uri.IdnHost) != UriHostNameType.Dns)
        {
            return false;
        }

        host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        return host.Length > 0;
    }

    private static bool TryScaleSampledCount(ulong count, double sampleInterval, out long value)
    {
        value = 0;
        try
        {
            var scaled = checked((decimal)count * (decimal)sampleInterval);
            var rounded = decimal.Round(scaled, 0, MidpointRounding.AwayFromZero);
            if (rounded > long.MaxValue)
                return false;
            value = decimal.ToInt64(rounded);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private CloudflareAnalyticsCollectionResult Failure(
        CloudflareAnalyticsCollectionOptions options,
        CloudflareAnalyticsCapabilityProbeResult probe,
        int requestCount,
        IReadOnlyCollection<WebTrafficObservation> observations,
        IReadOnlyCollection<DateOnly> completedDates,
        DateOnly failedDate,
        string errorCode,
        string errorMessage)
    {
        var batch = WebTrafficObservationNormalizer.Normalize(new WebTrafficObservationBatch
        {
            Provider = options.ProviderId,
            SiteId = options.SiteId,
            CollectedAtUtc = _timeProvider.GetUtcNow(),
            SourceKind = "api",
            Status = "partial",
            ConfigurationHash = options.ConfigurationHash,
            EvidenceReference = options.EvidenceReference,
            CollectionCoverage = new WebTrafficObservationCollectionCoverage
            {
                FromDate = options.FromDate,
                ThroughDate = options.ThroughDate,
                CompletedDates = completedDates.ToArray(),
                FailedDate = failedDate,
                FailureCategory = errorCode
            },
            Observations = observations.ToArray()
        });
        return new CloudflareAnalyticsCollectionResult
        {
            Success = false,
            RequestCount = requestCount,
            CompletedDateCount = completedDates.Count,
            Probe = probe,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Batch = batch
        };
    }

    private async Task<ApiResult<T>> SendAsync<T>(
        string query,
        object variables,
        string token,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = JsonContent.Create(new { query, variables }, options: JsonOptions)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return ApiResult<T>.Failed(MapStatus(response.StatusCode), $"Cloudflare GraphQL returned HTTP {(int)response.StatusCode}.");
            var envelope = await response.Content.ReadFromJsonAsync<CloudflareGraphQlEnvelope<T>>(JsonOptions, cancellationToken).ConfigureAwait(false);
            if (envelope is null || envelope.Errors is { Length: > 0 } || envelope.Data is null)
                return ApiResult<T>.Failed("graphql-error", "Cloudflare GraphQL returned errors or omitted data.");
            return ApiResult<T>.Succeeded(envelope.Data);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException)
        {
            return ApiResult<T>.Failed("invalid-response", "Cloudflare GraphQL returned invalid JSON.");
        }
        catch
        {
            return ApiResult<T>.Failed("request-failed", "Cloudflare GraphQL request failed.");
        }
    }

    private async Task<ApiResult<CloudflareZoneDetails>> GetZoneAsync(
        string zoneId,
        string token,
        CancellationToken cancellationToken)
    {
        try
        {
            var endpoint = new Uri(_endpoint, "zones/" + zoneId.ToLowerInvariant());
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return ApiResult<CloudflareZoneDetails>.Failed(MapStatus(response.StatusCode), $"Cloudflare zone lookup returned HTTP {(int)response.StatusCode}.");
            var envelope = await response.Content.ReadFromJsonAsync<CloudflareApiEnvelope<CloudflareZoneDetails>>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (envelope?.Success != true || envelope.Result is null || envelope.Errors.Length > 0)
                return ApiResult<CloudflareZoneDetails>.Failed("zone-lookup-error", "Cloudflare zone lookup returned errors or omitted the zone.");
            return ApiResult<CloudflareZoneDetails>.Succeeded(envelope.Result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException)
        {
            return ApiResult<CloudflareZoneDetails>.Failed("invalid-response", "Cloudflare zone lookup returned invalid JSON.");
        }
        catch
        {
            return ApiResult<CloudflareZoneDetails>.Failed("request-failed", "Cloudflare zone lookup request failed.");
        }
    }

    private void ValidateOptions(CloudflareAnalyticsCollectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ProviderId) || string.IsNullOrWhiteSpace(options.SiteId))
            throw new ArgumentException("Cloudflare collection requires provider and site identifiers.", nameof(options));
        ValidateZoneId(options.ZoneId);
        _ = NormalizeSiteHost(options.SiteBaseUrl);
        if (options.FromDate == default || options.ThroughDate == default || options.FromDate > options.ThroughDate)
            throw new ArgumentException("Cloudflare collection date range is invalid.", nameof(options));
        var currentUtcDate = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        if (options.ThroughDate >= currentUtcDate)
            throw new ArgumentException("Cloudflare traffic collection requires closed UTC dates before the current day.", nameof(options));
    }

    private static void ValidateZoneId(string zoneId)
    {
        if (zoneId?.Length != 32 || !zoneId.All(Uri.IsHexDigit))
            throw new ArgumentException("Cloudflare zoneId must be a 32-character hexadecimal identifier.", nameof(zoneId));
    }

    private static string NormalizeSiteHost(string siteBaseUrl)
    {
        if (!Uri.TryCreate(siteBaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath.Contains("//", StringComparison.Ordinal) ||
            uri.AbsolutePath.IndexOfAny(['%', '_']) >= 0)
        {
            throw new ArgumentException(
                "Cloudflare site base URL must be absolute HTTP(S) on its default port without user info, query, fragment, repeated path separators, or analytics wildcard metacharacters.",
                nameof(siteBaseUrl));
        }

        return uri.IdnHost.TrimEnd('.').ToLowerInvariant();
    }

    private static string? NormalizeZoneName(string? zoneName)
    {
        return !string.IsNullOrWhiteSpace(zoneName) && TryNormalizeHostDimension(zoneName, out var host)
            ? host
            : null;
    }

    private static bool HostBelongsToZone(string host, string zoneName) =>
        string.Equals(host, zoneName, StringComparison.Ordinal) ||
        host.EndsWith("." + zoneName, StringComparison.Ordinal);

    private static CloudflareAnalyticsCapabilityProbeResult ProbeFailure(int requestCount, string code, string message) => new()
    {
        RequestCount = requestCount,
        ErrorCode = code,
        ErrorMessage = message
    };

    private static string MapStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "authentication-rejected",
        HttpStatusCode.TooManyRequests => "rate-limited",
        _ when (int)status >= 500 => "provider-unavailable",
        _ => "request-rejected"
    };

    private sealed record ApiResult<T>(bool Success, T? Value, string? ErrorCode, string? ErrorMessage)
    {
        internal static ApiResult<T> Succeeded(T value) => new(true, value, null, null);
        internal static ApiResult<T> Failed(string code, string message) => new(false, default, code, message);
    }

    private const string CapabilityQuery = """
        query PowerForgeCloudflareCapabilities($zoneTag: string) {
          viewer {
            zones(filter: { zoneTag: $zoneTag }) {
              settings {
                httpRequestsAdaptiveGroups {
                  enabled
                  maxPageSize
                  maxDuration
                  notOlderThan
                }
              }
            }
          }
        }
        """;

    private const string TrafficQuery = """
        query PowerForgeCloudflareTraffic($zoneTag: string, $filter: ZoneHttpRequestsAdaptiveGroupsFilter_InputObject, $limit: Int) {
          viewer {
            zones(filter: { zoneTag: $zoneTag }) {
              traffic: httpRequestsAdaptiveGroups(
                filter: $filter
                limit: $limit
                orderBy: [count_DESC]
              ) {
                count
                avg { sampleInterval }
                sum { visits edgeResponseBytes }
                dimensions { date clientRequestHTTPHost clientRequestPath clientRequestScheme }
              }
            }
          }
        }
        """;
}
