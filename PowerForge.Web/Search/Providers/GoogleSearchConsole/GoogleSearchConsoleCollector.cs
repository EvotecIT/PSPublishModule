using System.Globalization;
using System.Collections.Frozen;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google.Apis.Auth.OAuth2.Responses;

namespace PowerForge.Web;

/// <summary>Collects finalized Google Search Console Search Analytics data into the provider-neutral observation contract.</summary>
public sealed class GoogleSearchConsoleCollector
{
    /// <summary>Google's current maximum Search Analytics page size.</summary>
    public const int MaximumRowLimit = 25_000;

    /// <summary>Maximum Search Analytics rows Google exposes for one date and search type.</summary>
    public const int MaximumRowsPerDate = 50_000;

    /// <summary>Maximum daily partitions retained in one in-memory collection batch.</summary>
    public const int MaximumCollectionDateCount = 7;

    /// <summary>Provider kind implemented by this collector.</summary>
    public const string ProviderKind = "google-search-console";

    private const int MaximumErrorBodyCharacters = 2_000;
    private static readonly Uri SitesEndpoint = new("https://www.googleapis.com/webmasters/v3/sites");
    private static readonly string[] DimensionsWithQuery = ["date", "page", "query", "country", "device"];
    private static readonly string[] DimensionsWithoutQuery = ["date", "page", "country", "device"];
    private static readonly string[] FinalityDimensions = ["date"];
    private static readonly string[] SearchTypes = ["web", "image", "video", "news", "discover", "googleNews"];
    private static readonly IReadOnlySet<string> CollectorCapabilities =
        new[] { WebSearchProviderCapabilities.SearchAnalytics }.ToFrozenSet(StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> ReadablePermissionLevels =
        new[] { "siteOwner", "siteFullUser", "siteRestrictedUser" }.ToFrozenSet(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IGoogleSearchConsoleAccessTokenProvider _accessTokenProvider;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a collector over caller-owned HTTP and credential boundaries.</summary>
    public GoogleSearchConsoleCollector(
        HttpClient httpClient,
        IGoogleSearchConsoleAccessTokenProvider accessTokenProvider,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _accessTokenProvider = accessTokenProvider ?? throw new ArgumentNullException(nameof(accessTokenProvider));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Capabilities currently implemented by the collector.</summary>
    public static IReadOnlySet<string> AvailableCapabilities => CollectorCapabilities;

    /// <summary>Checks that the configured credential can see the exact Search Console property.</summary>
    public async Task<GoogleSearchConsolePropertyProbeResult> ProbeAsync(
        string property,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(property))
            throw new ArgumentException("Google Search Console property is required.", nameof(property));

        var normalizedProperty = NormalizeProperty(property);
        try
        {
            using var request = await CreateRequestAsync(HttpMethod.Get, SitesEndpoint, cancellationToken).ConfigureAwait(false);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return ProbeFailure(normalizedProperty, ClassifyHttpError(response.StatusCode), await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false));

            var payload = await response.Content
                .ReadFromJsonAsync<GoogleSearchConsoleSitesResponse>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (payload is null || payload.SiteEntries is null || payload.SiteEntries.Any(static entry => entry is null))
                throw new InvalidOperationException("Google Search Console returned invalid property entries.");
            var matchingSite = payload.SiteEntries
                .FirstOrDefault(entry => PropertiesEqual(entry.SiteUrl, normalizedProperty));
            if (matchingSite is null)
            {
                return ProbeFailure(
                    normalizedProperty,
                    "property-unavailable",
                    "The configured credential cannot see the exact Google Search Console property.");
            }
            if (!ReadablePermissionLevels.Contains(matchingSite.PermissionLevel))
            {
                var failure = ProbeFailure(
                    normalizedProperty,
                    "property-unverified",
                    "The configured credential can see the property but does not have a verified readable permission level.");
                failure.PermissionLevel = matchingSite.PermissionLevel;
                return failure;
            }

            return new GoogleSearchConsolePropertyProbeResult
            {
                Success = true,
                Property = normalizedProperty,
                PermissionLevel = matchingSite.PermissionLevel,
                AvailableCapabilities = CollectorCapabilities.ToArray()
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProbeFailure(normalizedProperty, "request-timeout", "The Google Search Console property probe timed out.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidOperationException or TokenResponseException)
        {
            return ProbeFailure(normalizedProperty, ClassifyException(ex), SafeFailureMessage(ex, "Google Search Console property probe failed."));
        }
    }

    /// <summary>Collects one daily query at a time and preserves observations already received if a later page fails.</summary>
    public async Task<GoogleSearchConsoleCollectionResult> CollectAsync(
        GoogleSearchConsoleCollectionOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        var probe = await ProbeAsync(options.Property, cancellationToken).ConfigureAwait(false);
        var observations = new List<WebSearchObservation>();
        var requestCount = 0;
        var completedDates = new List<DateOnly>();

        if (!probe.Success)
        {
            return BuildResult(
                options,
                probe,
                observations,
                requestCount,
                completedDates,
                options.FromDate,
                probe.ErrorCode ?? "property-probe-failed",
                probe.ErrorMessage ?? "Google Search Console property probe failed.");
        }

        DateOnly? firstIncompleteDate;
        requestCount++;
        try
        {
            var endpoint = BuildSearchAnalyticsEndpoint(probe.Property);
            var finalityBody = new GoogleSearchConsoleQueryRequest
            {
                StartDate = options.FromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                EndDate = options.ThroughDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Dimensions = FinalityDimensions,
                Type = options.SearchType,
                DataState = "all",
                RowLimit = MaximumRowLimit,
                StartRow = 0,
                DimensionFilterGroups = CreatePageFilterGroups(options.SiteBaseUrl)
            };
            using var request = await CreateRequestAsync(HttpMethod.Post, endpoint, cancellationToken).ConfigureAwait(false);
            request.Content = JsonContent.Create(finalityBody, options: JsonOptions);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return BuildResult(
                    options,
                    probe,
                    observations,
                    requestCount,
                    completedDates,
                    options.FromDate,
                    ClassifyHttpError(response.StatusCode),
                    await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false));
            }

            var payload = await response.Content
                .ReadFromJsonAsync<GoogleSearchConsoleQueryResponse>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (payload is null)
                throw new InvalidOperationException("Google Search Console returned an empty final-data payload.");
            firstIncompleteDate = ParseFirstIncompleteDate(payload.Metadata?.FirstIncompleteDate, options);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return BuildResult(options, probe, observations, requestCount, completedDates, options.FromDate, "request-timeout", "The Google Search Console final-data probe timed out.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidOperationException or FormatException or TokenResponseException)
        {
            return BuildResult(
                options,
                probe,
                observations,
                requestCount,
                completedDates,
                options.FromDate,
                ClassifyException(ex),
                SafeFailureMessage(ex, "Google Search Console final-data probe failed."));
        }

        var date = options.FromDate;
        var dimensions = GetDimensions(options.SearchType);
        while (true)
        {
            if (firstIncompleteDate is DateOnly incompleteDate && date >= incompleteDate)
            {
                return BuildResult(
                    options,
                    probe,
                    observations,
                    requestCount,
                    completedDates,
                    date,
                    "data-not-final",
                    $"Google Search Console data is not final from {incompleteDate:yyyy-MM-dd}.");
            }

            var startRow = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                requestCount++;
                var endpoint = BuildSearchAnalyticsEndpoint(probe.Property);
                var requestRowLimit = Math.Min(options.RowLimit, MaximumRowsPerDate - startRow);
                var body = new GoogleSearchConsoleQueryRequest
                {
                    StartDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    EndDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Dimensions = dimensions,
                    Type = options.SearchType,
                    DataState = "final",
                    RowLimit = requestRowLimit,
                    StartRow = startRow,
                    DimensionFilterGroups = CreatePageFilterGroups(options.SiteBaseUrl)
                };

                try
                {
                    using var request = await CreateRequestAsync(HttpMethod.Post, endpoint, cancellationToken).ConfigureAwait(false);
                    request.Content = JsonContent.Create(body, options: JsonOptions);
                    using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        return BuildResult(
                            options,
                            probe,
                            observations,
                            requestCount,
                            completedDates,
                            date,
                            ClassifyHttpError(response.StatusCode),
                            await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false));
                    }

                    var payload = await response.Content
                        .ReadFromJsonAsync<GoogleSearchConsoleQueryResponse>(JsonOptions, cancellationToken)
                        .ConfigureAwait(false);
                    if (payload is null || payload.Rows is null)
                        throw new InvalidOperationException("Google Search Console returned a null analytics payload.");
                    var rows = payload.Rows;
                    if (rows.Length == 0)
                        break;

                    foreach (var row in rows)
                    {
                        if (row is null)
                            throw new InvalidOperationException("Google Search Console returned a null analytics row.");
                        observations.Add(MapRow(row, dimensions, date, options, probe.Property));
                    }
                    if (rows.Length < requestRowLimit || startRow + rows.Length >= MaximumRowsPerDate)
                        break;
                    startRow = checked(startRow + requestRowLimit);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return BuildResult(options, probe, observations, requestCount, completedDates, date, "request-timeout", "The Google Search Console collection request timed out.");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidOperationException or OverflowException or FormatException or TokenResponseException)
                {
                    return BuildResult(
                        options,
                        probe,
                        observations,
                        requestCount,
                        completedDates,
                        date,
                        ClassifyException(ex),
                        SafeFailureMessage(ex, "Google Search Console collection failed."));
                }
            }

            completedDates.Add(date);
            if (date == options.ThroughDate)
                break;
            date = date.AddDays(1);
        }

        return BuildResult(options, probe, observations, requestCount, completedDates, null, null, null);
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, Uri endpoint, CancellationToken cancellationToken)
    {
        var token = await _accessTokenProvider.GetAccessTokenAsync(endpoint, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Google Search Console authentication returned an empty access token.");
        var request = new HttpRequestMessage(method, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private GoogleSearchConsoleCollectionResult BuildResult(
        GoogleSearchConsoleCollectionOptions options,
        GoogleSearchConsolePropertyProbeResult probe,
        List<WebSearchObservation> observations,
        int requestCount,
        IReadOnlyCollection<DateOnly> completedDates,
        DateOnly? failedDate,
        string? errorCode,
        string? errorMessage)
    {
        var success = errorCode is null;
        return new GoogleSearchConsoleCollectionResult
        {
            Success = success,
            Probe = probe,
            CompletedDateCount = completedDates.Count,
            RequestCount = requestCount,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Batch = new WebSearchObservationBatch
            {
                Provider = options.ProviderId,
                SiteId = options.SiteId,
                CollectedAtUtc = _timeProvider.GetUtcNow(),
                SourceKind = "api",
                Status = success ? "complete" : "partial",
                ConfigurationHash = options.ConfigurationHash,
                EvidenceReference = options.EvidenceReference,
                CollectionCoverage = new WebSearchObservationCollectionCoverage
                {
                    Mode = "daily",
                    FromDate = options.FromDate,
                    ThroughDate = options.ThroughDate,
                    SearchType = options.SearchType,
                    CompletedDates = completedDates.OrderBy(date => date).ToArray(),
                    FailedDate = failedDate,
                    FailureCategory = errorCode
                },
                ZeroDataConfirmed = success && observations.Count == 0,
                Observations = observations.ToArray()
            }
        };
    }

    private static DateOnly? ParseFirstIncompleteDate(string? value, GoogleSearchConsoleCollectionOptions options)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
            date < options.FromDate ||
            date > options.ThroughDate)
        {
            throw new InvalidOperationException("Google Search Console returned invalid final-data metadata.");
        }

        return date;
    }

    private static WebSearchObservation MapRow(
        GoogleSearchConsoleQueryRow row,
        IReadOnlyList<string> dimensions,
        DateOnly requestedDate,
        GoogleSearchConsoleCollectionOptions options,
        string property)
    {
        if (row.Keys is null || row.Keys.Length != dimensions.Count)
            throw new InvalidOperationException("Google Search Console returned a row with an unexpected dimension set.");
        if (!DateOnly.TryParseExact(row.Keys[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var rowDate) ||
            rowDate != requestedDate)
        {
            throw new InvalidOperationException("Google Search Console returned a row outside the requested daily partition.");
        }

        if (row.Clicks is null || row.Impressions is null || row.ClickThroughRate is null || row.Position is null)
            throw new InvalidOperationException("Google Search Console returned a row with missing metrics.");
        var clicks = ConvertCount(row.Clicks.Value, "clicks");
        var impressions = ConvertCount(row.Impressions.Value, "impressions");
        if (clicks > impressions)
            throw new InvalidOperationException("Google Search Console returned clicks greater than impressions.");
        if (!double.IsFinite(row.ClickThroughRate.Value) || row.ClickThroughRate.Value is < 0d or > 1d)
            throw new InvalidOperationException("Google Search Console returned an invalid click-through rate.");
        if (!double.IsFinite(row.Position.Value) || row.Position.Value < 0d)
            throw new InvalidOperationException("Google Search Console returned an invalid average position.");

        var dimensionValues = dimensions
            .Select((dimension, index) => (Dimension: dimension, Value: NullIfEmpty(row.Keys[index])))
            .ToDictionary(item => item.Dimension, item => item.Value, StringComparer.Ordinal);
        var page = dimensionValues["page"];
        if (page is null || !PageBelongsToSite(page, options.SiteBaseUrl))
            throw new InvalidOperationException("Google Search Console returned a page outside the configured fleet site.");

        return new WebSearchObservation
        {
            Provider = options.ProviderId,
            SiteId = options.SiteId,
            Date = rowDate,
            Page = page,
            Query = dimensionValues.GetValueOrDefault("query"),
            Country = dimensionValues["country"],
            Device = dimensionValues["device"],
            SearchType = options.SearchType,
            Clicks = clicks,
            Impressions = impressions,
            ClickThroughRate = row.ClickThroughRate.Value,
            AveragePosition = impressions == 0 ? null : row.Position.Value,
            EvidenceReference = options.EvidenceReference ?? $"gsc:{property}:{rowDate:yyyy-MM-dd}:{options.SearchType}"
        };
    }

    private static string[] GetDimensions(string searchType) =>
        searchType is "discover" or "googleNews" ? DimensionsWithoutQuery : DimensionsWithQuery;

    private static GoogleSearchConsoleDimensionFilterGroup[] CreatePageFilterGroups(string siteBaseUrl)
    {
        var site = new Uri(WebSearchProviderConfigurationFingerprint.NormalizeUrl(siteBaseUrl), UriKind.Absolute);
        var authority = site.GetLeftPart(UriPartial.Authority);
        var path = site.AbsolutePath;
        var expression = path == "/"
            ? "^" + Regex.Escape(authority) + "(?:/|\\?|$)"
            : "^" + Regex.Escape(authority + path.TrimEnd('/')) + "(?:/|\\?|$)";
        return
        [
            new GoogleSearchConsoleDimensionFilterGroup
            {
                Filters =
                [
                    new GoogleSearchConsoleDimensionFilter { Expression = expression }
                ]
            }
        ];
    }

    private static bool PageBelongsToSite(string page, string siteBaseUrl)
    {
        if (!Uri.TryCreate(page, UriKind.Absolute, out var pageUri) ||
            !Uri.TryCreate(siteBaseUrl, UriKind.Absolute, out var siteUri) ||
            pageUri.Scheme is not ("http" or "https") ||
            siteUri.Scheme is not ("http" or "https") ||
            !pageUri.Scheme.Equals(siteUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !pageUri.IdnHost.TrimEnd('.').Equals(siteUri.IdnHost.TrimEnd('.'), StringComparison.OrdinalIgnoreCase) ||
            pageUri.Port != siteUri.Port ||
            !string.IsNullOrEmpty(pageUri.UserInfo) ||
            !string.IsNullOrEmpty(pageUri.Fragment))
        {
            return false;
        }

        var sitePath = siteUri.AbsolutePath;
        if (!sitePath.EndsWith('/'))
            sitePath += "/";
        var exactSitePath = sitePath.Length == 1 ? "/" : sitePath.TrimEnd('/');
        return pageUri.AbsolutePath.Equals(exactSitePath, StringComparison.Ordinal) ||
               pageUri.AbsolutePath.StartsWith(sitePath, StringComparison.Ordinal);
    }

    private static long ConvertCount(double value, string metric)
    {
        if (!double.IsFinite(value) || value < 0d || value > long.MaxValue || Math.Truncate(value) != value)
            throw new InvalidOperationException($"Google Search Console returned an invalid {metric} count.");
        return checked((long)value);
    }

    private static Uri BuildSearchAnalyticsEndpoint(string property)
    {
        var encodedProperty = Uri.EscapeDataString(property);
        return new Uri($"https://www.googleapis.com/webmasters/v3/sites/{encodedProperty}/searchAnalytics/query");
    }

    private static void ValidateOptions(GoogleSearchConsoleCollectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ProviderId))
            throw new ArgumentException("Google Search Console provider id is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.SiteId))
            throw new ArgumentException("Google Search Console site id is required.", nameof(options));
        if (!Uri.TryCreate(options.SiteBaseUrl, UriKind.Absolute, out var siteUri) ||
            siteUri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Google Search Console site base URL must be an absolute HTTP(S) URL.", nameof(options));
        }
        if (string.IsNullOrWhiteSpace(options.Property))
            throw new ArgumentException("Google Search Console property is required.", nameof(options));
        if (options.FromDate == default || options.ThroughDate == default || options.FromDate > options.ThroughDate)
            throw new ArgumentException("Google Search Console date range is invalid.", nameof(options));
        var dateCount = options.ThroughDate.DayNumber - options.FromDate.DayNumber + 1;
        if (dateCount > MaximumCollectionDateCount)
            throw new ArgumentOutOfRangeException(nameof(options), $"Google Search Console collection is limited to {MaximumCollectionDateCount} daily partitions per run.");
        if (!SearchTypes.Contains(options.SearchType, StringComparer.Ordinal))
            throw new ArgumentException("Google Search Console search type is not supported.", nameof(options));
        if (options.RowLimit is < 1 or > MaximumRowLimit)
            throw new ArgumentOutOfRangeException(nameof(options), $"Google Search Console row limit must be between 1 and {MaximumRowLimit}.");
    }

    private static string NormalizeProperty(string property) =>
        WebSearchProviderSecretPolicy.NormalizeFingerprintSettingValue(ProviderKind, "property", property)?.Trim()
        ?? property.Trim();

    private static bool PropertiesEqual(string? left, string right) =>
        string.Equals(NormalizeProperty(left ?? string.Empty), right, StringComparison.Ordinal);

    private static GoogleSearchConsolePropertyProbeResult ProbeFailure(string property, string code, string message) => new()
    {
        Success = false,
        Property = property,
        AvailableCapabilities = CollectorCapabilities.ToArray(),
        ErrorCode = code,
        ErrorMessage = message
    };

    private static string ClassifyHttpError(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => "authentication-failed",
        HttpStatusCode.Forbidden => "permission-denied",
        HttpStatusCode.TooManyRequests => "quota-exceeded",
        _ when (int)statusCode >= 500 => "provider-unavailable",
        _ => "provider-request-failed"
    };

    private static string ClassifyException(Exception exception) => exception switch
    {
        JsonException => "provider-response-invalid",
        TokenResponseException => "authentication-failed",
        HttpRequestException or IOException => "provider-unavailable",
        FormatException or OverflowException => "provider-response-invalid",
        _ when exception.Message.Contains("authentication", StringComparison.OrdinalIgnoreCase) => "authentication-failed",
        _ => "provider-response-invalid"
    };

    private static string SafeFailureMessage(Exception exception, string fallback) => exception switch
    {
        HttpRequestException when !string.IsNullOrWhiteSpace(exception.Message) => $"{fallback} {exception.Message}",
        JsonException => "Google Search Console returned an invalid JSON response.",
        TokenResponseException => fallback,
        _ => fallback
    };

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (json.Length > MaximumErrorBodyCharacters)
                json = json[..MaximumErrorBodyCharacters];
            var error = JsonSerializer.Deserialize<GoogleSearchConsoleErrorResponse>(json, JsonOptions)?.Error?.Message;
            return string.IsNullOrWhiteSpace(error)
                ? $"Google Search Console request failed with HTTP {(int)response.StatusCode}."
                : $"Google Search Console request failed with HTTP {(int)response.StatusCode}: {error.Trim()}";
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return $"Google Search Console request failed with HTTP {(int)response.StatusCode}.";
        }
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
