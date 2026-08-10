using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

/// <summary>Collects verified-site Bing Webmaster query and page statistics through the JSON REST surface.</summary>
public sealed partial class BingWebmasterCollector
{
    /// <summary>Fleet provider kind handled by this collector.</summary>
    public const string ProviderKind = "bing-webmaster";

    /// <summary>Capabilities currently implemented by this collector.</summary>
    public static readonly IReadOnlySet<string> AvailableCapabilities =
        new HashSet<string>([WebSearchProviderCapabilities.SearchAnalytics], StringComparer.OrdinalIgnoreCase);

    private static readonly Uri DefaultApiBaseUri = new("https://ssl.bing.com/webmaster/api.svc/json/");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly IBingWebmasterApiKeyProvider _apiKeyProvider;
    private readonly Uri _apiBaseUri;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a Bing Webmaster collector.</summary>
    public BingWebmasterCollector(
        HttpClient httpClient,
        IBingWebmasterApiKeyProvider apiKeyProvider,
        Uri? apiBaseUri = null,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiKeyProvider = apiKeyProvider ?? throw new ArgumentNullException(nameof(apiKeyProvider));
        _apiBaseUri = apiBaseUri ?? DefaultApiBaseUri;
        _timeProvider = timeProvider ?? TimeProvider.System;
        if (!_apiBaseUri.IsAbsoluteUri || _apiBaseUri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Bing Webmaster API base URI must be absolute HTTPS.", nameof(apiBaseUri));
    }

    /// <summary>Verifies that the credential can see the exact configured Bing site.</summary>
    public async Task<BingWebmasterSiteProbeResult> ProbeAsync(
        string siteUrl,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeSiteUrl(siteUrl, out var normalizedSite))
            throw new ArgumentException("Bing Webmaster site URL must be an absolute HTTP(S) URL without user info, query or fragment.", nameof(siteUrl));

        string apiKey;
        try
        {
            apiKey = await _apiKeyProvider.GetApiKeyAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return ProbeFailure(siteUrl, "credential-unavailable", "Bing Webmaster credential resolution failed.");
        }

        try
        {
            var response = await SendAsync<BingWebmasterSite>("GetUserSites", null, apiKey, cancellationToken)
                .ConfigureAwait(false);
            if (!response.Success)
                return ProbeFailure(siteUrl, response.ErrorCode!, response.ErrorMessage!);

            var matching = response.Values.FirstOrDefault(site =>
                TryNormalizeSiteUrl(site.Url, out var candidate) &&
                string.Equals(candidate, normalizedSite, StringComparison.Ordinal));
            if (matching is null)
                return ProbeFailure(siteUrl, "site-not-visible", "The configured Bing Webmaster site is not visible to this credential.");
            if (!matching.IsVerified)
                return ProbeFailure(siteUrl, "site-not-verified", "The configured Bing Webmaster site is visible but not verified.");

            return new BingWebmasterSiteProbeResult
            {
                Success = true,
                SiteUrl = siteUrl,
                Verified = true,
                AvailableCapabilities = AvailableCapabilities.OrderBy(value => value, StringComparer.Ordinal).ToArray()
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return ProbeFailure(siteUrl, "request-failed", "Bing Webmaster site probing failed.");
        }
    }

    /// <summary>Collects query and page statistics for the requested reporting range.</summary>
    public async Task<BingWebmasterCollectionResult> CollectAsync(
        BingWebmasterCollectionOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        var probe = await ProbeAsync(options.SiteUrl, cancellationToken).ConfigureAwait(false);
        if (!probe.Success)
        {
            var probeRequestCount = string.Equals(probe.ErrorCode, "credential-unavailable", StringComparison.Ordinal) ? 0 : 1;
            return BuildFailure(options, probe, probeRequestCount, probe.ErrorCode!, probe.ErrorMessage!);
        }

        string apiKey;
        try
        {
            apiKey = await _apiKeyProvider.GetApiKeyAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return BuildFailure(options, probe, 1, "credential-unavailable", "Bing Webmaster credential resolution failed.");
        }

        var observations = new List<WebSearchObservation>();
        var requestCount = 1;
        var parameters = new Dictionary<string, string> { ["siteUrl"] = options.SiteUrl };

        var queryResponse = await SendAsync<BingWebmasterQueryStat>(
            "GetQueryStats", parameters, apiKey, cancellationToken).ConfigureAwait(false);
        requestCount++;
        if (!queryResponse.Success)
            return BuildFailure(options, probe, requestCount, queryResponse.ErrorCode!, queryResponse.ErrorMessage!);
        if (!TryMapStats(queryResponse.Values, value => value.Query, options, pageDimension: false, out var queryObservations))
            return BuildFailure(options, probe, requestCount, "invalid-response", "Bing Webmaster returned invalid query statistics.");
        observations.AddRange(queryObservations);

        var pageResponse = await SendAsync<BingWebmasterPageStat>(
            "GetPageStats", parameters, apiKey, cancellationToken).ConfigureAwait(false);
        requestCount++;
        if (!pageResponse.Success)
            return BuildFailure(options, probe, requestCount, pageResponse.ErrorCode!, pageResponse.ErrorMessage!, observations);
        if (!TryMapStats(pageResponse.Values, value => value.Page, options, pageDimension: true, out var pageObservations))
            return BuildFailure(options, probe, requestCount, "invalid-response", "Bing Webmaster returned invalid page statistics.", observations);
        observations.AddRange(pageObservations);

        var trafficResponse = await SendAsync<BingWebmasterTrafficStat>(
            "GetRankAndTrafficStats", parameters, apiKey, cancellationToken).ConfigureAwait(false);
        requestCount++;
        if (!trafficResponse.Success)
            return BuildFailure(options, probe, requestCount, trafficResponse.ErrorCode!, trafficResponse.ErrorMessage!, observations);

        var requestedDates = EnumerateDates(options.FromDate, options.ThroughDate);
        var traffic = new List<(DateOnly Date, long Clicks, long Impressions)>();
        foreach (var value in trafficResponse.Values)
        {
            var date = TryParseProviderDate(value.Date);
            if (!date.HasValue || !value.Clicks.HasValue || !value.Impressions.HasValue ||
                value.Clicks.Value < 0 || value.Impressions.Value < 0 || value.Clicks.Value > value.Impressions.Value)
            {
                return BuildFailure(options, probe, requestCount, "invalid-response", "Bing Webmaster returned invalid traffic statistics.", observations);
            }
            if (date.Value >= options.FromDate && date.Value <= options.ThroughDate)
                traffic.Add((date.Value, value.Clicks.Value, value.Impressions.Value));
        }
        var trafficByDate = traffic
            .GroupBy(item => item.Date)
            .ToDictionary(group => group.Key, group => group.ToArray());
        if (trafficByDate.Any(pair => pair.Value.Length != 1))
            return BuildFailure(options, probe, requestCount, "invalid-response", "Bing Webmaster returned duplicate traffic dates.", observations);
        var zeroConfirmed = observations.Count == 0 &&
                            trafficByDate.Count == requestedDates.Length &&
                            requestedDates.All(date =>
                                trafficByDate.TryGetValue(date, out var rows) &&
                                rows.Length == 1 &&
                                rows[0].Clicks == 0 &&
                                rows[0].Impressions == 0);
        if (observations.Count == 0 && !zeroConfirmed)
        {
            return BuildFailure(
                options,
                probe,
                requestCount,
                "dimension-data-unavailable",
                "Bing returned no dated page or query rows, so a complete search observation run cannot be proven.",
                observations,
                trafficByDate.Keys);
        }

        var completedDates = observations.Select(observation => observation.Date)
            .Concat(trafficByDate.Keys)
            .Distinct()
            .OrderBy(date => date)
            .ToArray();

        var batch = new WebSearchObservationBatch
        {
            SchemaVersion = WebSearchObservationBatch.CurrentSchemaVersion,
            Provider = options.ProviderId,
            SiteId = options.SiteId,
            CollectedAtUtc = _timeProvider.GetUtcNow(),
            SourceKind = "api",
            Status = "complete",
            ConfigurationHash = options.ConfigurationHash,
            EvidenceReference = options.EvidenceReference,
            ZeroDataConfirmed = zeroConfirmed,
            CollectionCoverage = new WebSearchObservationCollectionCoverage
            {
                Mode = "snapshot",
                FromDate = options.FromDate,
                ThroughDate = options.ThroughDate,
                SearchType = options.SearchType,
                CompletedDates = completedDates
            },
            Observations = observations.ToArray()
        };
        try
        {
            batch = WebSearchObservationNormalizer.Normalize(batch);
        }
        catch (ArgumentException)
        {
            return BuildFailure(
                options,
                probe,
                requestCount,
                "invalid-response",
                "Bing Webmaster returned search rows that violate the observation contract.");
        }

        return new BingWebmasterCollectionResult
        {
            Success = true,
            Probe = probe,
            CompletedDateCount = completedDates.Length,
            RequestCount = requestCount,
            Batch = batch
        };
    }

    private async Task<ApiResponse<T>> SendAsync<T>(
        string method,
        IReadOnlyDictionary<string, string>? parameters,
        string apiKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = new List<string> { "apikey=" + Uri.EscapeDataString(apiKey) };
            if (parameters is not null)
            {
                query.AddRange(parameters.Select(pair =>
                    Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
            }

            var requestUri = new Uri(_apiBaseUri, method + "?" + string.Join("&", query));
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var error = await TryReadErrorAsync(response, cancellationToken).ConfigureAwait(false);
                return ApiResponse<T>.Failure(
                    MapStatusCode(response.StatusCode),
                    error ?? $"Bing Webmaster returned HTTP {(int)response.StatusCode}.");
            }

            var payload = await response.Content
                .ReadFromJsonAsync<BingWebmasterEnvelope<T>>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return ApiResponse<T>.Succeeded(payload?.Values ?? Array.Empty<T>());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException)
        {
            return ApiResponse<T>.Failure("invalid-response", "Bing Webmaster returned invalid JSON.");
        }
        catch
        {
            return ApiResponse<T>.Failure("request-failed", "Bing Webmaster request failed.");
        }
    }

    private static bool TryMapStats<TStat>(
        IEnumerable<TStat> values,
        Func<TStat, string?> getDimension,
        BingWebmasterCollectionOptions options,
        bool pageDimension,
        out WebSearchObservation[] observations)
        where TStat : BingWebmasterSearchStat
    {
        var mapped = new List<WebSearchObservation>();
        foreach (var value in values)
        {
            var dimension = getDimension(value);
            var date = TryParseProviderDate(value.Date);
            if (!date.HasValue || string.IsNullOrWhiteSpace(dimension) ||
                !value.Clicks.HasValue || !value.Impressions.HasValue ||
                value.Clicks.Value < 0 || value.Impressions.Value < 0 || value.Clicks.Value > value.Impressions.Value ||
                value.AverageImpressionPosition is double position && (!double.IsFinite(position) || position < 0d))
            {
                observations = Array.Empty<WebSearchObservation>();
                return false;
            }
            if (pageDimension &&
                (!Uri.TryCreate(dimension, UriKind.Absolute, out var pageUri) ||
                 (pageUri.Scheme != Uri.UriSchemeHttp && pageUri.Scheme != Uri.UriSchemeHttps)))
            {
                observations = Array.Empty<WebSearchObservation>();
                return false;
            }
            if (date.Value < options.FromDate || date.Value > options.ThroughDate)
                continue;

            mapped.Add(new WebSearchObservation
            {
                Date = date.Value,
                Page = pageDimension ? dimension : null,
                Query = pageDimension ? null : dimension,
                SearchType = options.SearchType,
                Clicks = value.Clicks.Value,
                Impressions = value.Impressions.Value,
                AveragePosition = value.Impressions.Value > 0 ? value.AverageImpressionPosition : null,
                EvidenceReference = options.EvidenceReference
            });
        }
        observations = mapped.ToArray();
        return true;
    }

    private BingWebmasterCollectionResult BuildFailure(
        BingWebmasterCollectionOptions options,
        BingWebmasterSiteProbeResult probe,
        int requestCount,
        string errorCode,
        string errorMessage,
        IReadOnlyCollection<WebSearchObservation>? observations = null,
        IEnumerable<DateOnly>? additionalCompletedDates = null)
    {
        var retainedObservations = observations?.ToArray() ?? Array.Empty<WebSearchObservation>();
        var completedDates = retainedObservations.Select(observation => observation.Date)
            .Concat(additionalCompletedDates ?? Array.Empty<DateOnly>())
            .Distinct()
            .OrderBy(date => date)
            .ToArray();
        var batch = WebSearchObservationNormalizer.Normalize(new WebSearchObservationBatch
        {
            SchemaVersion = WebSearchObservationBatch.CurrentSchemaVersion,
            Provider = options.ProviderId,
            SiteId = options.SiteId,
            CollectedAtUtc = _timeProvider.GetUtcNow(),
            SourceKind = "api",
            Status = "partial",
            ConfigurationHash = options.ConfigurationHash,
            EvidenceReference = options.EvidenceReference,
            CollectionCoverage = new WebSearchObservationCollectionCoverage
            {
                Mode = "snapshot",
                FromDate = options.FromDate,
                ThroughDate = options.ThroughDate,
                SearchType = options.SearchType,
                CompletedDates = completedDates,
                FailureCategory = errorCode
            },
            Observations = retainedObservations
        });
        return new BingWebmasterCollectionResult
        {
            Success = false,
            Probe = probe,
            CompletedDateCount = completedDates.Length,
            RequestCount = requestCount,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Batch = batch
        };
    }

    private static BingWebmasterSiteProbeResult ProbeFailure(string siteUrl, string code, string message) => new()
    {
        Success = false,
        SiteUrl = siteUrl,
        ErrorCode = code,
        ErrorMessage = message,
        AvailableCapabilities = AvailableCapabilities.OrderBy(value => value, StringComparer.Ordinal).ToArray()
    };

    private static async Task<string?> TryReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<BingWebmasterError>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(error?.Message) ? null : "Bing Webmaster rejected the request.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static string MapStatusCode(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest => "authentication-or-request-rejected",
        HttpStatusCode.TooManyRequests => "rate-limited",
        _ when (int)statusCode >= 500 => "provider-unavailable",
        _ => "request-rejected"
    };

    private static void ValidateOptions(BingWebmasterCollectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ProviderId))
            throw new ArgumentException("Bing Webmaster collection requires a provider identifier.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.SiteId))
            throw new ArgumentException("Bing Webmaster collection requires a site identifier.", nameof(options));
        if (!TryNormalizeSiteUrl(options.SiteUrl, out _))
            throw new ArgumentException("Bing Webmaster site URL must be an absolute HTTP(S) URL without user info, query or fragment.", nameof(options));
        if (options.FromDate == default || options.ThroughDate == default || options.FromDate > options.ThroughDate)
            throw new ArgumentException("Bing Webmaster collection date range is invalid.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.SearchType))
            throw new ArgumentException("Bing Webmaster collection requires a search type.", nameof(options));
        if (!string.Equals(options.SearchType, "web", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Bing Webmaster query and page statistics support only the web search type.", nameof(options));
    }

    private static DateOnly[] EnumerateDates(DateOnly fromDate, DateOnly throughDate) =>
        Enumerable.Range(0, throughDate.DayNumber - fromDate.DayNumber + 1)
            .Select(fromDate.AddDays)
            .ToArray();

    private static DateOnly? TryParseProviderDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var match = ProviderDateRegex().Match(value);
        if (!match.Success || !long.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds))
            return null;
        try
        {
            var instant = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
            if (match.Groups[2].Success)
            {
                var hours = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                var minutes = int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
                if (hours > 14 || minutes > 59 || (hours == 14 && minutes != 0))
                    return null;
                var offset = new TimeSpan(hours, minutes, 0);
                if (match.Groups[2].Value == "-")
                    offset = -offset;
                instant = instant.ToOffset(offset);
            }
            return DateOnly.FromDateTime(instant.DateTime);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static bool TryNormalizeSiteUrl(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var builder = new UriBuilder(uri)
        {
            Host = uri.IdnHost.TrimEnd('.').ToLowerInvariant(),
            Fragment = string.Empty,
            Query = string.Empty
        };
        if (uri.IsDefaultPort)
            builder.Port = -1;
        var path = builder.Path.TrimEnd('/');
        builder.Path = path.Length == 0 ? "/" : path + "/";
        normalized = builder.Uri.AbsoluteUri;
        return true;
    }

    [GeneratedRegex("^/Date\\((-?[0-9]+)(?:([+-])([0-9]{2})([0-9]{2}))?\\)/$", RegexOptions.CultureInvariant)]
    private static partial Regex ProviderDateRegex();

    private sealed class ApiResponse<T>
    {
        public bool Success { get; private init; }
        public T[] Values { get; private init; } = Array.Empty<T>();
        public string? ErrorCode { get; private init; }
        public string? ErrorMessage { get; private init; }

        public static ApiResponse<T> Succeeded(T[] values) => new() { Success = true, Values = values };
        public static ApiResponse<T> Failure(string code, string message) => new()
        {
            ErrorCode = code,
            ErrorMessage = message
        };
    }
}
