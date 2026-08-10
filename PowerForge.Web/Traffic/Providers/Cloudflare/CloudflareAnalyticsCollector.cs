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
        CancellationToken cancellationToken = default)
    {
        ValidateZoneId(zoneId);
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
            return ProbeFailure("credential-unavailable", "Cloudflare analytics credential resolution failed.");
        }

        var response = await SendAsync<CloudflareCapabilityData>(CapabilityQuery, new { zoneTag = zoneId.ToLowerInvariant() }, token, cancellationToken)
            .ConfigureAwait(false);
        if (!response.Success)
            return ProbeFailure(response.ErrorCode!, response.ErrorMessage!);
        var zones = response.Value?.Viewer?.Zones ?? Array.Empty<CloudflareCapabilityZone>();
        if (zones.Length != 1)
            return ProbeFailure("zone-not-visible", "The configured Cloudflare zone is not visible to this credential.");
        var settings = zones[0].Settings?.HttpRequestsAdaptiveGroups;
        if (settings?.Enabled != true)
            return ProbeFailure("dataset-unavailable", "Cloudflare httpRequestsAdaptiveGroups is not enabled for this zone.");
        if (settings.MaxPageSize is null or <= 0)
            return ProbeFailure("invalid-response", "Cloudflare did not report a usable analytics page size.");

        return new CloudflareAnalyticsCapabilityProbeResult
        {
            Success = true,
            DatasetEnabled = true,
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
        var probe = await ProbeAsync(options.ZoneId, cancellationToken).ConfigureAwait(false);
        if (!probe.Success)
            return Failure(options, probe, 1, Array.Empty<WebTrafficObservation>(), Array.Empty<DateOnly>(), options.FromDate, probe.ErrorCode!, probe.ErrorMessage!);

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
            return Failure(options, probe, 1, Array.Empty<WebTrafficObservation>(), Array.Empty<DateOnly>(), options.FromDate, "credential-unavailable", "Cloudflare analytics credential resolution failed.");
        }

        var observations = new List<WebTrafficObservation>();
        var completedDates = new List<DateOnly>();
        var requestCount = 1;
        for (var date = options.FromDate;; date = date.AddDays(1))
        {
            var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var end = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var variables = new
            {
                zoneTag = options.ZoneId.ToLowerInvariant(),
                limit = probe.MaxPageSize,
                filter = new
                {
                    datetime_geq = start.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                    datetime_lt = end.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                    requestSource = "eyeball"
                }
            };
            var response = await SendAsync<CloudflareTrafficData>(TrafficQuery, variables, token, cancellationToken).ConfigureAwait(false);
            requestCount++;
            if (!response.Success)
                return Failure(options, probe, requestCount, observations, completedDates, date, response.ErrorCode!, response.ErrorMessage!);
            var zones = response.Value?.Viewer?.Zones ?? Array.Empty<CloudflareTrafficZone>();
            if (zones.Length != 1)
                return Failure(options, probe, requestCount, observations, completedDates, date, "zone-not-visible", "Cloudflare did not return the configured zone.");
            var rows = zones[0].Traffic ?? Array.Empty<CloudflareTrafficGroup>();
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
        IReadOnlyCollection<CloudflareTrafficGroup> rows,
        CloudflareAnalyticsCollectionOptions options,
        DateOnly requestedDate,
        out WebTrafficObservation[] observations)
    {
        var mapped = new List<WebTrafficObservation>(rows.Count);
        foreach (var row in rows)
        {
            var dimensions = row.Dimensions;
            if (row.Count is null || row.Sum?.Visits is null || row.Sum.EdgeResponseBytes is null ||
                row.Average?.SampleInterval is null || dimensions?.Date != requestedDate ||
                string.IsNullOrWhiteSpace(dimensions.Host) || string.IsNullOrWhiteSpace(dimensions.Path) ||
                row.Count > long.MaxValue || row.Sum.Visits > long.MaxValue || row.Sum.EdgeResponseBytes > long.MaxValue ||
                !double.IsFinite(row.Average.SampleInterval.Value) || row.Average.SampleInterval.Value < 1d)
            {
                observations = Array.Empty<WebTrafficObservation>();
                return false;
            }
            mapped.Add(new WebTrafficObservation
            {
                Date = requestedDate,
                Host = dimensions.Host,
                Path = dimensions.Path,
                Requests = (long)row.Count.Value,
                Visits = (long)row.Sum.Visits.Value,
                EdgeResponseBytes = (long)row.Sum.EdgeResponseBytes.Value,
                SampleInterval = row.Average.SampleInterval.Value,
                EvidenceReference = options.EvidenceReference
            });
        }
        observations = mapped.ToArray();
        return true;
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

    private static void ValidateOptions(CloudflareAnalyticsCollectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ProviderId) || string.IsNullOrWhiteSpace(options.SiteId))
            throw new ArgumentException("Cloudflare collection requires provider and site identifiers.", nameof(options));
        ValidateZoneId(options.ZoneId);
        if (options.FromDate == default || options.ThroughDate == default || options.FromDate > options.ThroughDate)
            throw new ArgumentException("Cloudflare collection date range is invalid.", nameof(options));
    }

    private static void ValidateZoneId(string zoneId)
    {
        if (zoneId?.Length != 32 || !zoneId.All(Uri.IsHexDigit))
            throw new ArgumentException("Cloudflare zoneId must be a 32-character hexadecimal identifier.", nameof(zoneId));
    }

    private static CloudflareAnalyticsCapabilityProbeResult ProbeFailure(string code, string message) => new()
    {
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
        query PowerForgeCloudflareTraffic($zoneTag: string, $filter: filter, $limit: Int) {
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
                dimensions { date clientRequestHTTPHost clientRequestPath }
              }
            }
          }
        }
        """;
}
