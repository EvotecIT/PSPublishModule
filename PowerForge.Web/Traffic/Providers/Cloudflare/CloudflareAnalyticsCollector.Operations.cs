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
            return result;
        }

        string token;
        try { token = await _tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            result.Http = FailedCapability("credential-unavailable", "Cloudflare analytics credential resolution failed.");
            result.Firewall = FailedCapability("credential-unavailable", "Cloudflare analytics credential resolution failed.");
            return result;
        }

        var buckets = new Dictionary<DateTimeOffset, CloudflareHourlyOperationalObservation>();
        var httpResponse = await SendAsync<CloudflareOperationalData>(HttpOperationalQuery, new
        {
            zoneTag = options.ZoneId.ToLowerInvariant(),
            filter = BuildTrafficFilter(options.SiteBaseUrl, options.FromUtc.UtcDateTime, options.ThroughUtc.UtcDateTime)
        }, token, cancellationToken).ConfigureAwait(false);
        result.RequestCount++;
        if (!TryGetOperationalZone(httpResponse, out var httpZone, out var httpCode, out var httpMessage))
            result.Http = FailedCapability(httpCode!, httpMessage!);
        else
            result.Http = TryMapHttp(httpZone!.Http, buckets, out var httpError)
                ? new CloudflareOperationalCapabilityState { Success = true }
                : FailedCapability("invalid-response", httpError!);

        var firewallResponse = await SendAsync<CloudflareOperationalData>(FirewallOperationalQuery, new
        {
            zoneTag = options.ZoneId.ToLowerInvariant(),
            filter = new Dictionary<string, object?>
            {
                ["datetime_geq"] = FormatUtc(options.FromUtc),
                ["datetime_lt"] = FormatUtc(options.ThroughUtc),
                ["clientRequestHTTPHost"] = new Uri(options.SiteBaseUrl).IdnHost.TrimEnd('.').ToLowerInvariant()
            }
        }, token, cancellationToken).ConfigureAwait(false);
        result.RequestCount++;
        if (!TryGetOperationalZone(firewallResponse, out var firewallZone, out var firewallCode, out var firewallMessage))
            result.Firewall = FailedCapability(firewallCode!, firewallMessage!);
        else
            result.Firewall = TryMapFirewall(firewallZone!.Firewall, buckets, out var firewallError)
                ? new CloudflareOperationalCapabilityState { Success = true }
                : FailedCapability("invalid-response", firewallError!);
        result.Hours = buckets.Values.OrderBy(value => value.HourUtc).ToArray();
        result.Success = result.Http.Success;

        if (!string.IsNullOrWhiteSpace(options.AccountId))
        {
            result.Rum = await InspectRumSiteAsync(options.AccountId!, options.ZoneId, token, cancellationToken).ConfigureAwait(false);
            result.RequestCount++;
        }
        return result;
    }

    private async Task<CloudflareRumSiteState> InspectRumSiteAsync(string accountId, string zoneId, string token, CancellationToken cancellationToken)
    {
        try
        {
            var endpoint = new Uri(_endpoint, $"accounts/{accountId.ToLowerInvariant()}/rum/site_info/list?per_page=50");
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return FailedRum(MapStatus(response.StatusCode), $"Cloudflare RUM site lookup returned HTTP {(int)response.StatusCode}.");
            var envelope = await response.Content.ReadFromJsonAsync<CloudflareRumSitesEnvelope>(JsonOptions, cancellationToken).ConfigureAwait(false);
            if (envelope?.Success != true || envelope.Errors.Length > 0)
                return FailedRum("rum-lookup-error", "Cloudflare RUM site lookup returned errors.");
            var site = envelope.Result.FirstOrDefault(value => string.Equals(value.Ruleset?.ZoneTag, zoneId, StringComparison.OrdinalIgnoreCase));
            return new CloudflareRumSiteState
            {
                Requested = true,
                Configured = site is not null,
                Enabled = site?.Ruleset?.Enabled == true,
                AutoInstall = site?.AutoInstall == true
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (JsonException) { return FailedRum("invalid-response", "Cloudflare RUM site lookup returned invalid JSON."); }
        catch { return FailedRum("request-failed", "Cloudflare RUM site lookup failed."); }
    }

    private static bool TryMapHttp(IEnumerable<CloudflareHttpOperationalGroup?>? rows, IDictionary<DateTimeOffset, CloudflareHourlyOperationalObservation> buckets, out string? error)
    {
        error = null;
        if (rows is null) { error = "Cloudflare returned no HTTP operational dataset."; return false; }
        foreach (var row in rows)
        {
            if (row?.Count is null || row.Average?.SampleInterval is null || row.Sum?.EdgeResponseBytes is null ||
                row.Sum.EdgeResponseBytes.Value > long.MaxValue || row.Dimensions?.HourUtc is null ||
                row.Dimensions.EdgeResponseStatus is null || !IsValidSampleInterval(row.Average.SampleInterval.Value) ||
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
            if (row?.Count is null || row.Average?.SampleInterval is null || row.Dimensions?.HourUtc is null ||
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

    private static CloudflareHourlyOperationalObservation GetBucket(IDictionary<DateTimeOffset, CloudflareHourlyOperationalObservation> buckets, DateTimeOffset hour)
    {
        if (!buckets.TryGetValue(hour, out var bucket)) { bucket = new CloudflareHourlyOperationalObservation { HourUtc = hour }; buckets.Add(hour, bucket); }
        return bucket;
    }

    private static bool IsCached(string? status) => status?.Trim().ToLowerInvariant() is "hit" or "revalidated" or "updating";
    private static bool IsMitigated(string? action) => action?.Trim().ToLowerInvariant() is "block" or "challenge" or "jschallenge" or "managed_challenge";
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
        query PowerForgeCloudflareHttpOperations($zoneTag: string, $filter: ZoneHttpRequestsAdaptiveGroupsFilter_InputObject) {
          viewer { zones(filter: { zoneTag: $zoneTag }) {
            http: httpRequestsAdaptiveGroups(filter: $filter, limit: 10000) {
              count avg { sampleInterval } sum { edgeResponseBytes } dimensions { datetimeHour cacheStatus edgeResponseStatus }
            }
          } }
        }
        """;

    private const string FirewallOperationalQuery = """
        query PowerForgeCloudflareFirewallOperations($zoneTag: string, $filter: ZoneFirewallEventsAdaptiveGroupsFilter_InputObject) {
          viewer { zones(filter: { zoneTag: $zoneTag }) {
            firewall: firewallEventsAdaptiveGroups(filter: $filter, limit: 10000) {
              count avg { sampleInterval } dimensions { datetimeHour action }
            }
          } }
        }
        """;
}
