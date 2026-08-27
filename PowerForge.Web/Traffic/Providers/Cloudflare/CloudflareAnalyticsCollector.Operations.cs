using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace PowerForge.Web;

public sealed partial class CloudflareAnalyticsCollector
{
    /// <summary>Collects hourly requests, cache/status, WAF mitigation and RUM installation state.</summary>
    public async Task<CloudflareOperationalCollectionResult> CollectOperationsAsync(
        CloudflareOperationalCollectionOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateOperationalOptions(options);
        var probe = await ProbeAsync(options.ZoneId, options.SiteBaseUrl, cancellationToken).ConfigureAwait(false);
        var result = new CloudflareOperationalCollectionResult
        {
            CollectedAtUtc = _timeProvider.GetUtcNow(),
            RequestCount = probe.RequestCount,
            Rum = new CloudflareRumSiteState { Requested = !string.IsNullOrWhiteSpace(options.AccountId) }
        };
        if (!probe.Success)
        {
            result.Http = FailedCapability(probe.ErrorCode!, probe.ErrorMessage!);
            result.Firewall = FailedCapability("not-attempted", "Firewall collection was not attempted because zone validation failed.");
            if (result.Rum.Requested)
                result.Rum = FailedRum("not-attempted", "RUM inspection was not attempted because zone validation failed.");
            return result;
        }

        if (probe.NotOlderThanSeconds is > 0 &&
            options.FromUtc < _timeProvider.GetUtcNow().Subtract(TimeSpan.FromSeconds(probe.NotOlderThanSeconds.Value)))
        {
            result.Http = FailedCapability("retention-boundary", "The requested Cloudflare operational range starts before the provider-reported retention boundary.");
            result.Firewall = FailedCapability("not-attempted", "Firewall collection was not attempted because the requested range is outside provider retention.");
            if (result.Rum.Requested)
                result.Rum = FailedRum("not-attempted", "RUM inspection was not attempted because the requested range is outside provider retention.");
            return result;
        }

        string token;
        try { token = await _tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            result.Http = FailedCapability("credential-unavailable", "Cloudflare analytics credential resolution failed.");
            result.Firewall = FailedCapability("credential-unavailable", "Cloudflare analytics credential resolution failed.");
            if (result.Rum.Requested)
                result.Rum = FailedRum("credential-unavailable", "Cloudflare RUM credential resolution failed.");
            return result;
        }

        var http = await CollectHttpOperationsAsync(options, probe, token, cancellationToken).ConfigureAwait(false);
        result.RequestCount += http.RequestCount;
        result.Http = http.State;

        var firewall = await CollectFirewallOperationsAsync(options, probe, token, cancellationToken).ConfigureAwait(false);
        result.RequestCount += firewall.RequestCount;
        result.Firewall = firewall.State;
        result.Hours = CombineBuckets(http.Buckets, firewall.Buckets);
        result.Success = result.Http.Success;

        if (!string.IsNullOrWhiteSpace(options.AccountId))
        {
            var rum = await InspectRumSiteAsync(
                options.AccountId!,
                options.ZoneId,
                NormalizeSiteHost(options.SiteBaseUrl),
                token,
                cancellationToken).ConfigureAwait(false);
            result.Rum = rum.State;
            result.RequestCount += rum.RequestCount;
        }
        return result;
    }

    private async Task<OperationalPartitionResult> CollectHttpOperationsAsync(
        CloudflareOperationalCollectionOptions options,
        CloudflareAnalyticsCapabilityProbeResult probe,
        string token,
        CancellationToken cancellationToken)
    {
        var buckets = new Dictionary<DateTimeOffset, CloudflareHourlyOperationalObservation>();
        var requestCount = 0;
        foreach (var partition in BuildOperationalPartitions(options, probe))
        {
            var response = await SendAsync<CloudflareOperationalData>(HttpOperationalQuery, new
            {
                zoneTag = options.ZoneId.ToLowerInvariant(),
                limit = probe.MaxPageSize,
                filter = BuildTrafficFilter(options.SiteBaseUrl, partition.FromUtc.UtcDateTime, partition.ThroughUtc.UtcDateTime)
            }, token, cancellationToken).ConfigureAwait(false);
            requestCount++;
            if (!TryGetOperationalZone(response, out var zone, out var code, out var message))
                return OperationalPartitionResult.Failed(code!, message!, requestCount);
            if (zone!.Http is not { } rows)
                return OperationalPartitionResult.Failed("invalid-response", "Cloudflare returned no HTTP operational dataset.", requestCount);
            if (rows.Length >= probe.MaxPageSize)
                return OperationalPartitionResult.Failed("row-limit-reached", "Cloudflare reached the HTTP operational row limit, so the requested range cannot be marked complete.", requestCount);
            var partitionBuckets = new Dictionary<DateTimeOffset, CloudflareHourlyOperationalObservation>();
            if (!TryMapHttp(rows, partitionBuckets, out var error))
                return OperationalPartitionResult.Failed("invalid-response", error!, requestCount);
            if (!TryMergeBuckets(buckets, partitionBuckets, out error))
                return OperationalPartitionResult.Failed("invalid-response", error!, requestCount);
        }
        return OperationalPartitionResult.Succeeded(buckets, requestCount);
    }

    private async Task<OperationalPartitionResult> CollectFirewallOperationsAsync(
        CloudflareOperationalCollectionOptions options,
        CloudflareAnalyticsCapabilityProbeResult probe,
        string token,
        CancellationToken cancellationToken)
    {
        var buckets = new Dictionary<DateTimeOffset, CloudflareHourlyOperationalObservation>();
        var requestCount = 0;
        foreach (var partition in BuildOperationalPartitions(options, probe))
        {
            var response = await SendAsync<CloudflareOperationalData>(FirewallOperationalQuery, new
            {
                zoneTag = options.ZoneId.ToLowerInvariant(),
                limit = probe.MaxPageSize,
                filter = BuildFirewallFilter(options.SiteBaseUrl, partition.FromUtc, partition.ThroughUtc)
            }, token, cancellationToken).ConfigureAwait(false);
            requestCount++;
            if (!TryGetOperationalZone(response, out var zone, out var code, out var message))
                return OperationalPartitionResult.Failed(code!, message!, requestCount);
            if (zone!.Firewall is not { } rows)
                return OperationalPartitionResult.Failed("invalid-response", "Cloudflare returned no firewall dataset.", requestCount);
            if (rows.Length >= probe.MaxPageSize)
                return OperationalPartitionResult.Failed("row-limit-reached", "Cloudflare reached the firewall operational row limit, so the requested range cannot be marked complete.", requestCount);
            var partitionBuckets = new Dictionary<DateTimeOffset, CloudflareHourlyOperationalObservation>();
            if (!TryMapFirewall(rows, partitionBuckets, out var error))
                return OperationalPartitionResult.Failed("invalid-response", error!, requestCount);
            if (!TryMergeBuckets(buckets, partitionBuckets, out error))
                return OperationalPartitionResult.Failed("invalid-response", error!, requestCount);
        }
        return OperationalPartitionResult.Succeeded(buckets, requestCount);
    }

    private async Task<(CloudflareRumSiteState State, int RequestCount)> InspectRumSiteAsync(
        string accountId,
        string zoneId,
        string siteHost,
        string token,
        CancellationToken cancellationToken)
    {
        var requestCount = 0;
        try
        {
            for (var page = 1;; page++)
            {
                var endpoint = new Uri(_endpoint, $"accounts/{accountId.ToLowerInvariant()}/rum/site_info/list?per_page=50&page={page.ToString(CultureInfo.InvariantCulture)}");
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                requestCount++;
                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return (FailedRum(MapStatus(response.StatusCode), $"Cloudflare RUM site lookup returned HTTP {(int)response.StatusCode}."), requestCount);
                var envelope = await response.Content.ReadFromJsonAsync<CloudflareRumSitesEnvelope>(JsonOptions, cancellationToken).ConfigureAwait(false);
                if (envelope?.Success != true || envelope.Errors.Length > 0)
                    return (FailedRum("rum-lookup-error", "Cloudflare RUM site lookup returned errors."), requestCount);
                var site = envelope.Result.FirstOrDefault(value =>
                    string.Equals(value.Ruleset?.ZoneTag, zoneId, StringComparison.OrdinalIgnoreCase) &&
                    value.Host is not null &&
                    TryNormalizeHostDimension(value.Host, out var rumHost) &&
                    string.Equals(rumHost, siteHost, StringComparison.Ordinal));
                if (site is not null)
                    return (new CloudflareRumSiteState { Requested = true, Configured = true, Enabled = site.Ruleset?.Enabled == true, AutoInstall = site.AutoInstall == true }, requestCount);

                var totalPages = envelope.ResultInfo?.TotalPages;
                if (totalPages is > 0)
                {
                    if (page >= totalPages.Value)
                        return (new CloudflareRumSiteState { Requested = true }, requestCount);
                    continue;
                }
                if (envelope.Result.Length < 50)
                    return (new CloudflareRumSiteState { Requested = true }, requestCount);
                return (FailedRum("invalid-response", "Cloudflare RUM site lookup omitted pagination metadata for a full page."), requestCount);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (JsonException) { return (FailedRum("invalid-response", "Cloudflare RUM site lookup returned invalid JSON."), requestCount); }
        catch { return (FailedRum("request-failed", "Cloudflare RUM site lookup failed."), requestCount); }
    }

    private static bool TryMapHttp(IEnumerable<CloudflareHttpOperationalGroup?>? rows, IDictionary<DateTimeOffset, CloudflareHourlyOperationalObservation> buckets, out string? error)
    {
        error = null;
        if (rows is null) { error = "Cloudflare returned no HTTP operational dataset."; return false; }
        foreach (var row in rows)
        {
            if (row?.Count is null || row.Average?.SampleInterval is null || row.Sum?.EdgeResponseBytes is null ||
                row.Sum.EdgeResponseBytes.Value > long.MaxValue || row.Dimensions?.HourUtc is null ||
                row.Dimensions.EdgeResponseStatus is null || string.IsNullOrWhiteSpace(row.Dimensions.CacheStatus) ||
                !IsValidSampleInterval(row.Average.SampleInterval.Value) ||
                !TryScaleSampledCount(row.Count.Value, row.Average.SampleInterval.Value, out var count))
            { error = "Cloudflare returned an invalid HTTP operational row."; return false; }
            var hour = NormalizeHour(row.Dimensions.HourUtc.Value);
            var bucket = GetBucket(buckets, hour);
            try
            {
                bucket.Requests = checked(bucket.Requests + count);
                bucket.EdgeResponseBytes = checked(bucket.EdgeResponseBytes + (long)row.Sum.EdgeResponseBytes.Value);
                bucket.MaximumSampleInterval = Math.Max(bucket.MaximumSampleInterval, row.Average.SampleInterval.Value);
                if (IsCached(row.Dimensions.CacheStatus)) bucket.CachedRequests = checked(bucket.CachedRequests + count);
                if (row.Dimensions.EdgeResponseStatus is >= 400 and <= 499) bucket.ClientErrors = checked(bucket.ClientErrors + count);
                if (row.Dimensions.EdgeResponseStatus is >= 500 and <= 599) bucket.ServerErrors = checked(bucket.ServerErrors + count);
            }
            catch (OverflowException)
            {
                error = "Cloudflare returned HTTP operational values outside the supported range.";
                return false;
            }
        }
        return true;
    }

    private static bool TryMapFirewall(IEnumerable<CloudflareFirewallOperationalGroup?>? rows, IDictionary<DateTimeOffset, CloudflareHourlyOperationalObservation> buckets, out string? error)
    {
        error = null;
        if (rows is null) { error = "Cloudflare returned no firewall dataset."; return false; }
        foreach (var row in rows)
        {
            if (row?.Count is null || row.Average?.SampleInterval is null || row.Dimensions?.HourUtc is null || string.IsNullOrWhiteSpace(row.Dimensions.Action) ||
                !IsValidSampleInterval(row.Average.SampleInterval.Value) ||
                !TryScaleSampledCount(row.Count.Value, row.Average.SampleInterval.Value, out var count))
            { error = "Cloudflare returned an invalid firewall row."; return false; }
            var bucket = GetBucket(buckets, NormalizeHour(row.Dimensions.HourUtc.Value));
            try
            {
                bucket.FirewallEvents = checked(bucket.FirewallEvents + count);
                bucket.MaximumSampleInterval = Math.Max(bucket.MaximumSampleInterval, row.Average.SampleInterval.Value);
                if (IsMitigated(row.Dimensions.Action)) bucket.FirewallMitigated = checked(bucket.FirewallMitigated + count);
            }
            catch (OverflowException)
            {
                error = "Cloudflare returned firewall values outside the supported range.";
                return false;
            }
        }
        return true;
    }

    private static IEnumerable<(DateTimeOffset FromUtc, DateTimeOffset ThroughUtc)> BuildOperationalPartitions(
        CloudflareOperationalCollectionOptions options,
        CloudflareAnalyticsCapabilityProbeResult probe)
    {
        var duration = probe.MaxDurationSeconds is > 0
            ? TimeSpan.FromSeconds(probe.MaxDurationSeconds.Value)
            : TimeSpan.FromDays(1);
        for (var start = options.FromUtc; start < options.ThroughUtc;)
        {
            var candidateEnd = start + duration;
            var end = candidateEnd < options.ThroughUtc ? candidateEnd : options.ThroughUtc;
            yield return (start, end);
            start = end;
        }
    }

    private static Dictionary<string, object?> BuildFirewallFilter(string siteBaseUrl, DateTimeOffset fromUtc, DateTimeOffset throughUtc)
    {
        var siteUri = new Uri(siteBaseUrl, UriKind.Absolute);
        var sitePath = NormalizeSitePath(siteUri.AbsolutePath);
        var filter = new Dictionary<string, object?>
        {
            ["datetime_geq"] = FormatUtc(fromUtc),
            ["datetime_lt"] = FormatUtc(throughUtc),
            ["clientRequestHTTPHost"] = siteUri.IdnHost.TrimEnd('.').ToLowerInvariant()
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

    private static bool TryMergeBuckets(
        IDictionary<DateTimeOffset, CloudflareHourlyOperationalObservation> destination,
        IReadOnlyDictionary<DateTimeOffset, CloudflareHourlyOperationalObservation> source,
        out string? error)
    {
        error = null;
        try
        {
            foreach (var pair in source)
            {
                var target = GetBucket(destination, pair.Key);
                var value = pair.Value;
                target.Requests = checked(target.Requests + value.Requests);
                target.CachedRequests = checked(target.CachedRequests + value.CachedRequests);
                target.ClientErrors = checked(target.ClientErrors + value.ClientErrors);
                target.ServerErrors = checked(target.ServerErrors + value.ServerErrors);
                target.EdgeResponseBytes = checked(target.EdgeResponseBytes + value.EdgeResponseBytes);
                target.FirewallEvents = checked(target.FirewallEvents + value.FirewallEvents);
                target.FirewallMitigated = checked(target.FirewallMitigated + value.FirewallMitigated);
                target.MaximumSampleInterval = Math.Max(target.MaximumSampleInterval, value.MaximumSampleInterval);
            }
            return true;
        }
        catch (OverflowException)
        {
            error = "Cloudflare returned operational values outside the supported range.";
            return false;
        }
    }

    private static CloudflareHourlyOperationalObservation[] CombineBuckets(
        IReadOnlyDictionary<DateTimeOffset, CloudflareHourlyOperationalObservation> http,
        IReadOnlyDictionary<DateTimeOffset, CloudflareHourlyOperationalObservation> firewall)
    {
        var combined = new Dictionary<DateTimeOffset, CloudflareHourlyOperationalObservation>();
        _ = TryMergeBuckets(combined, http, out _);
        _ = TryMergeBuckets(combined, firewall, out _);
        return combined.Values.OrderBy(value => value.HourUtc).ToArray();
    }

    private static CloudflareHourlyOperationalObservation GetBucket(IDictionary<DateTimeOffset, CloudflareHourlyOperationalObservation> buckets, DateTimeOffset hour)
    {
        if (!buckets.TryGetValue(hour, out var bucket)) { bucket = new CloudflareHourlyOperationalObservation { HourUtc = hour }; buckets.Add(hour, bucket); }
        return bucket;
    }

    private static bool IsCached(string? status) => status?.Trim().ToLowerInvariant() is "hit" or "revalidated" or "stale" or "updating";
    private static bool IsMitigated(string? action) => action?.Trim().ToLowerInvariant() is "block" or "challenge" or "jschallenge" or "managedchallenge" or "managed_challenge";
    private static bool IsValidSampleInterval(double value) => double.IsFinite(value) && value >= 1d;
    private static DateTimeOffset NormalizeHour(DateTimeOffset value) => new(value.UtcDateTime.Year, value.UtcDateTime.Month, value.UtcDateTime.Day, value.UtcDateTime.Hour, 0, 0, TimeSpan.Zero);
    private static string FormatUtc(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    private static CloudflareOperationalCapabilityState FailedCapability(string code, string message) => new() { ErrorCode = code, ErrorMessage = message };
    private static CloudflareRumSiteState FailedRum(string code, string message) => new() { Requested = true, ErrorCode = code, ErrorMessage = message };

    private void ValidateOperationalOptions(CloudflareOperationalCollectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.SiteId)) throw new ArgumentException("Cloudflare operational collection requires a site identifier.", nameof(options));
        ValidateZoneId(options.ZoneId); _ = NormalizeSiteHost(options.SiteBaseUrl);
        if (options.FromUtc == default || options.ThroughUtc == default || options.FromUtc >= options.ThroughUtc || options.ThroughUtc > _timeProvider.GetUtcNow() || options.ThroughUtc - options.FromUtc > TimeSpan.FromDays(7))
            throw new ArgumentException("Cloudflare operational window must be a closed UTC range no longer than seven days.", nameof(options));
        if (!string.IsNullOrWhiteSpace(options.AccountId) && (options.AccountId.Length != 32 || !options.AccountId.All(Uri.IsHexDigit)))
            throw new ArgumentException("Cloudflare accountId must be a 32-character hexadecimal identifier.", nameof(options));
    }

    private static bool TryGetOperationalZone(
        ApiResult<CloudflareOperationalData> response,
        out CloudflareOperationalZone? zone,
        out string? errorCode,
        out string? errorMessage)
    {
        zone = null;
        errorCode = response.ErrorCode;
        errorMessage = response.ErrorMessage;
        if (!response.Success)
            return false;
        if (response.Value?.Viewer?.Zones is not { Length: 1 } zones || zones[0] is not { } value)
        {
            errorCode = "invalid-response";
            errorMessage = "Cloudflare returned invalid operational analytics data.";
            return false;
        }
        zone = value;
        return true;
    }

    private const string HttpOperationalQuery = """
        query PowerForgeCloudflareHttpOperations($zoneTag: string, $filter: ZoneHttpRequestsAdaptiveGroupsFilter_InputObject, $limit: Int) {
          viewer { zones(filter: { zoneTag: $zoneTag }) {
            http: httpRequestsAdaptiveGroups(filter: $filter, limit: $limit) {
              count avg { sampleInterval } sum { edgeResponseBytes } dimensions { datetimeHour cacheStatus edgeResponseStatus }
            }
          } }
        }
        """;

    private const string FirewallOperationalQuery = """
        query PowerForgeCloudflareFirewallOperations($zoneTag: string, $filter: ZoneFirewallEventsAdaptiveGroupsFilter_InputObject, $limit: Int) {
          viewer { zones(filter: { zoneTag: $zoneTag }) {
            firewall: firewallEventsAdaptiveGroups(filter: $filter, limit: $limit) {
              count avg { sampleInterval } dimensions { datetimeHour action }
            }
          } }
        }
        """;

    private sealed class OperationalPartitionResult
    {
        private OperationalPartitionResult(
            CloudflareOperationalCapabilityState state,
            Dictionary<DateTimeOffset, CloudflareHourlyOperationalObservation> buckets,
            int requestCount)
        {
            State = state;
            Buckets = buckets;
            RequestCount = requestCount;
        }

        public CloudflareOperationalCapabilityState State { get; }
        public Dictionary<DateTimeOffset, CloudflareHourlyOperationalObservation> Buckets { get; }
        public int RequestCount { get; }

        public static OperationalPartitionResult Succeeded(Dictionary<DateTimeOffset, CloudflareHourlyOperationalObservation> buckets, int requestCount) =>
            new(new CloudflareOperationalCapabilityState { Success = true }, buckets, requestCount);

        public static OperationalPartitionResult Failed(string code, string message, int requestCount) =>
            new(FailedCapability(code, message), new Dictionary<DateTimeOffset, CloudflareHourlyOperationalObservation>(), requestCount);
    }
}
