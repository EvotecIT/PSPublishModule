using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PowerForge.Web;

/// <summary>Collects 28-day Chrome User Experience Report field evidence.</summary>
public sealed class CruxCollector
{
    /// <summary>Provider kind registered in the fleet capability catalog.</summary>
    public const string ProviderKind = "google-crux";
    private const string Endpoint = "https://chromeuxreport.googleapis.com/v1/records:queryRecord";
    private static readonly string[] RequestedMetrics =
    [
        "largest_contentful_paint",
        "interaction_to_next_paint",
        "cumulative_layout_shift",
        "first_contentful_paint",
        "experimental_time_to_first_byte"
    ];
    private static readonly IReadOnlyDictionary<string, (string Name, string Unit)> MetricMap =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["largest_contentful_paint"] = ("largest-contentful-paint", "milliseconds"),
            ["interaction_to_next_paint"] = ("interaction-to-next-paint", "milliseconds"),
            ["cumulative_layout_shift"] = ("cumulative-layout-shift", "unitless"),
            ["first_contentful_paint"] = ("first-contentful-paint", "milliseconds"),
            ["experimental_time_to_first_byte"] = ("time-to-first-byte", "milliseconds")
        };

    private readonly HttpClient _httpClient;
    private readonly ICruxApiKeyProvider _apiKeyProvider;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a collector over an injected HTTP and credential boundary.</summary>
    public CruxCollector(HttpClient httpClient, ICruxApiKeyProvider apiKeyProvider, TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiKeyProvider = apiKeyProvider ?? throw new ArgumentNullException(nameof(apiKeyProvider));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Queries one URL or origin and returns normalized p75 and histogram evidence.</summary>
    public async Task<CruxCollectionResult> CollectAsync(CruxCollectionOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);
        var collectedAt = _timeProvider.GetUtcNow();
        var partial = CreateBatch(options, collectedAt, "partial");
        string apiKey;
        try
        {
            apiKey = await _apiKeyProvider.GetApiKeyAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failure(partial, 0, "credential-unavailable", "CrUX API credential is unavailable.");
        }
        if (string.IsNullOrWhiteSpace(apiKey))
            return Failure(partial, 0, "credential-unavailable", "CrUX API credential is unavailable.");

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        try
        {
            request.Headers.Add("X-Goog-Api-Key", apiKey.Trim());
        }
        catch (FormatException)
        {
            return Failure(partial, 0, "credential-unavailable", "CrUX API credential is unavailable.");
        }
        request.Content = JsonContent.Create(CreateRequestBody(options));
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return Failure(partial, 1, "provider-unavailable", "CrUX API request did not complete.");
        }
        using (response)
        {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var zero = CreateBatch(options, collectedAt, "complete");
            zero.ZeroDataConfirmed = true;
            return new CruxCollectionResult
            {
                Success = true,
                RequestCount = 1,
                Batch = WebPerformanceObservationNormalizer.Normalize(zero)
            };
        }
        if (!response.IsSuccessStatusCode)
            return Failure(partial, 1, "provider-unavailable", $"CrUX API returned HTTP {(int)response.StatusCode}.");

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            WebPerformanceJsonBoundary.ValidateNoDuplicateObjectMembers(document.RootElement, "CrUX response");
            var complete = ParseResponse(document.RootElement, options, collectedAt);
            return new CruxCollectionResult
            {
                Success = true,
                RequestCount = 1,
                Batch = WebPerformanceObservationNormalizer.Normalize(complete)
            };
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return Failure(partial, 1, "invalid-response", ex.Message);
        }
        }
    }

    private static object CreateRequestBody(CruxCollectionOptions options)
    {
        var formFactor = options.FormFactor.Trim().ToLowerInvariant() switch
        {
            "all" => null,
            "phone" => "PHONE",
            "desktop" => "DESKTOP",
            "tablet" => "TABLET",
            _ => throw new ArgumentException("CrUX formFactor must be all, phone, desktop, or tablet.", nameof(options))
        };
        var body = new Dictionary<string, object?>
        {
            [options.TargetKind.Trim().Equals("origin", StringComparison.OrdinalIgnoreCase) ? "origin" : "url"] = options.TargetUrl,
            ["metrics"] = RequestedMetrics
        };
        if (formFactor is not null)
            body["formFactor"] = formFactor;
        return body;
    }

    private static WebPerformanceObservationBatch ParseResponse(JsonElement root, CruxCollectionOptions options, DateTimeOffset collectedAt)
    {
        var record = RequiredObject(root, "record");
        var key = RequiredObject(record, "key");
        var responseTarget = RequiredString(key, options.TargetKind.Trim().Equals("origin", StringComparison.OrdinalIgnoreCase) ? "origin" : "url");
        var targetKind = options.TargetKind.Trim().ToLowerInvariant();
        if (!WebPerformanceObservationNormalizer.CanonicalizeTarget(responseTarget, targetKind)
                .Equals(WebPerformanceObservationNormalizer.CanonicalizeTarget(options.TargetUrl, targetKind), StringComparison.Ordinal))
            throw new ArgumentException("CrUX response target does not match the requested target.", nameof(root));
        var requestedFormFactor = options.FormFactor.Trim().ToLowerInvariant();
        var hasResponseFormFactor = key.TryGetProperty("formFactor", out var responseFormFactorElement);
        if (requestedFormFactor == "all" && hasResponseFormFactor ||
            requestedFormFactor != "all" && (!hasResponseFormFactor || responseFormFactorElement.ValueKind != JsonValueKind.String ||
                !responseFormFactorElement.GetString()!.Equals(requestedFormFactor.ToUpperInvariant(), StringComparison.Ordinal)))
        {
            throw new ArgumentException("CrUX response form factor does not match the requested dimension.", nameof(root));
        }
        var period = RequiredObject(record, "collectionPeriod");
        var start = ParseDate(RequiredObject(period, "firstDate"));
        var end = ParseDate(RequiredObject(period, "lastDate"));
        var metrics = RequiredObject(record, "metrics");
        var observations = new List<WebPerformanceObservation>();
        foreach (var mapping in MetricMap)
        {
            if (!metrics.TryGetProperty(mapping.Key, out var metric))
                continue;
            if (metric.ValueKind != JsonValueKind.Object)
                throw new ArgumentException($"CrUX metric '{mapping.Key}' must be an object.", nameof(root));
            var percentiles = RequiredObject(metric, "percentiles");
            var value = RequiredFiniteNumber(percentiles, "p75");
            var histogramElement = RequiredArray(metric, "histogram");
            var bins = histogramElement.EnumerateArray().Select(ParseBin).ToArray();
            observations.Add(new WebPerformanceObservation
            {
                Metric = mapping.Value.Name,
                Value = value,
                Unit = mapping.Value.Unit,
                Percentile = 75,
                PeriodStartDate = start,
                PeriodEndDate = end,
                Histogram = bins
            });
        }
        if (observations.Count == 0)
            throw new ArgumentException("CrUX response does not contain any requested performance metrics.", nameof(root));
        return CreateBatch(options, collectedAt, "complete", observations.ToArray());
    }

    private static WebPerformanceHistogramBin ParseBin(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("CrUX histogram contains a non-object bin.", nameof(value));
        return new WebPerformanceHistogramBin
        {
            Start = OptionalFiniteNumber(value, "start"),
            End = OptionalFiniteNumber(value, "end"),
            Density = RequiredFiniteNumber(value, "density")
        };
    }

    private static WebPerformanceObservationBatch CreateBatch(
        CruxCollectionOptions options,
        DateTimeOffset collectedAt,
        string status,
        WebPerformanceObservation[]? observations = null) => new()
    {
        Provider = options.ProviderId,
        SiteId = options.SiteId,
        CollectedAtUtc = collectedAt,
        SourceKind = "api",
        Status = status,
        MeasurementKind = "field",
        TargetKind = options.TargetKind,
        TargetUrl = options.TargetUrl,
        FormFactor = options.FormFactor,
        ToolVersion = "crux-api-v1",
        ConfigurationHash = options.ConfigurationHash,
        EvidenceReference = options.EvidenceReference,
        Observations = observations ?? Array.Empty<WebPerformanceObservation>()
    };

    private static CruxCollectionResult Failure(WebPerformanceObservationBatch batch, int requests, string code, string message) => new()
    {
        Success = false,
        RequestCount = requests,
        ErrorCode = code,
        ErrorMessage = message,
        Batch = WebPerformanceObservationNormalizer.Normalize(batch)
    };

    private static void ValidateOptions(CruxCollectionOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ProviderId) || string.IsNullOrWhiteSpace(options.SiteId) ||
            string.IsNullOrWhiteSpace(options.SiteBaseUrl) || string.IsNullOrWhiteSpace(options.TargetUrl))
            throw new ArgumentException("CrUX collection requires provider, site, site base URL, and target URL.", nameof(options));
        var targetKind = options.TargetKind.Trim().ToLowerInvariant();
        if (targetKind is not ("url" or "origin"))
            throw new ArgumentException("CrUX targetKind must be url or origin.", nameof(options));
        if (!WebPerformanceObservationNormalizer.TargetBelongsToSite(options.TargetUrl, options.SiteBaseUrl))
            throw new ArgumentException("CrUX target does not belong to the configured fleet site.", nameof(options));
        _ = WebPerformanceObservationNormalizer.Normalize(new WebPerformanceObservationBatch
        {
            Provider = options.ProviderId,
            SiteId = options.SiteId,
            CollectedAtUtc = DateTimeOffset.UtcNow,
            SourceKind = "validation",
            Status = "partial",
            MeasurementKind = "field",
            TargetKind = targetKind,
            TargetUrl = options.TargetUrl,
            FormFactor = options.FormFactor,
            Observations = Array.Empty<WebPerformanceObservation>()
        });
    }

    private static JsonElement RequiredObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new ArgumentException($"CrUX response requires object '{name}'.", nameof(parent));
        return value;
    }

    private static JsonElement RequiredArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new ArgumentException($"CrUX response requires array '{name}'.", nameof(parent));
        return value;
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new ArgumentException($"CrUX response requires string '{name}'.", nameof(parent));
        return value.GetString()!;
    }

    private static double RequiredFiniteNumber(JsonElement parent, string name) =>
        OptionalFiniteNumber(parent, name) is double value
            ? value
            : throw new ArgumentException($"CrUX response requires finite numeric '{name}'.", nameof(parent));

    private static double? OptionalFiniteNumber(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        var parsed = value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var numberValue) => numberValue,
            JsonValueKind.String when double.TryParse(value.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var stringValue) => stringValue,
            _ => double.NaN
        };
        if (!double.IsFinite(parsed))
            throw new ArgumentException($"CrUX response has invalid numeric '{name}'.", nameof(parent));
        return parsed;
    }

    private static DateOnly ParseDate(JsonElement value)
    {
        var year = RequiredInt(value, "year");
        var month = RequiredInt(value, "month");
        var day = RequiredInt(value, "day");
        return new DateOnly(year, month, day);
    }

    private static int RequiredInt(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
            throw new ArgumentException($"CrUX response requires integer '{name}'.", nameof(parent));
        return number;
    }
}
