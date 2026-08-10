using System.Globalization;

namespace PowerForge.Web;

/// <summary>Validates and canonicalizes provider-neutral laboratory and field performance evidence.</summary>
public static class WebPerformanceObservationNormalizer
{
    private static readonly IReadOnlyDictionary<string, string> LabMetricUnits = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["performance-score"] = "score",
        ["first-contentful-paint"] = "milliseconds",
        ["largest-contentful-paint"] = "milliseconds",
        ["cumulative-layout-shift"] = "unitless",
        ["total-blocking-time"] = "milliseconds",
        ["speed-index"] = "milliseconds"
    };

    private static readonly IReadOnlyDictionary<string, string> FieldMetricUnits = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["largest-contentful-paint"] = "milliseconds",
        ["interaction-to-next-paint"] = "milliseconds",
        ["cumulative-layout-shift"] = "unitless",
        ["first-contentful-paint"] = "milliseconds",
        ["time-to-first-byte"] = "milliseconds"
    };

    /// <summary>Returns a normalized copy with deterministic run and observation identities.</summary>
    public static WebPerformanceObservationBatch Normalize(WebPerformanceObservationBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.SchemaVersion != WebPerformanceObservationBatch.CurrentSchemaVersion)
            throw new ArgumentException($"Unsupported performance observation schema version '{batch.SchemaVersion}'.", nameof(batch));

        var provider = RequiredIdentifier(batch.Provider, "provider");
        var siteId = RequiredIdentifier(batch.SiteId, "siteId");
        var sourceKind = RequiredIdentifier(batch.SourceKind, "sourceKind");
        var status = RequiredIdentifier(batch.Status, "status");
        var measurementKind = RequiredIdentifier(batch.MeasurementKind, "measurementKind");
        var targetKind = RequiredIdentifier(batch.TargetKind, "targetKind");
        var formFactor = RequiredIdentifier(batch.FormFactor, "formFactor");
        if (status is not ("complete" or "partial"))
            throw new ArgumentException("Performance status must be 'complete' or 'partial'.", nameof(batch));
        if (measurementKind is not ("lab" or "field"))
            throw new ArgumentException("Performance measurementKind must be 'lab' or 'field'.", nameof(batch));
        if (targetKind is not ("url" or "origin") || measurementKind == "lab" && targetKind != "url")
            throw new ArgumentException("Laboratory evidence requires a URL target; field evidence supports URL or origin targets.", nameof(batch));
        if (formFactor is not ("all" or "phone" or "desktop" or "tablet") || measurementKind == "lab" && formFactor is not ("phone" or "desktop"))
            throw new ArgumentException("Performance formFactor is not valid for the measurement kind.", nameof(batch));
        if (batch.CollectedAtUtc == default)
            throw new ArgumentException("Performance observation batch requires collectedAtUtc.", nameof(batch));

        var targetUrl = CanonicalizeTarget(batch.TargetUrl, targetKind);
        var metrics = measurementKind == "lab" ? LabMetricUnits : FieldMetricUnits;
        var observations = (batch.Observations ?? Array.Empty<WebPerformanceObservation>())
            .Select((value, index) => NormalizeObservation(value, measurementKind, metrics, index))
            .OrderBy(value => value.Metric, StringComparer.Ordinal)
            .ToArray();
        if (observations.Select(value => value.Metric).Distinct(StringComparer.Ordinal).Count() != observations.Length)
            throw new ArgumentException("A performance batch cannot contain the same metric more than once.", nameof(batch));
        if (status == "complete" && observations.Length == 0 && !batch.ZeroDataConfirmed)
            throw new ArgumentException("A complete empty performance batch must explicitly confirm no provider data.", nameof(batch));
        if (batch.ZeroDataConfirmed && (measurementKind != "field" || status != "complete" || observations.Length != 0))
            throw new ArgumentException("zeroDataConfirmed is valid only for a complete empty field batch.", nameof(batch));

        var normalized = new WebPerformanceObservationBatch
        {
            SchemaVersion = batch.SchemaVersion,
            Provider = provider,
            SiteId = siteId,
            CollectedAtUtc = batch.CollectedAtUtc.ToUniversalTime(),
            SourceKind = sourceKind,
            Status = status,
            MeasurementKind = measurementKind,
            TargetKind = targetKind,
            TargetUrl = targetUrl,
            FormFactor = formFactor,
            ToolVersion = Optional(batch.ToolVersion),
            ConfigurationHash = Optional(batch.ConfigurationHash),
            EvidenceReference = Optional(batch.EvidenceReference),
            ZeroDataConfirmed = batch.ZeroDataConfirmed,
            Observations = observations
        };
        normalized.RunId = Optional(batch.RunId) ?? RunFingerprint(normalized);
        foreach (var observation in normalized.Observations)
            observation.ObservationKey = WebSearchIdentityHasher.Compute(
                normalized.Provider,
                normalized.SiteId,
                normalized.RunId,
                MetricFingerprint(observation));
        return normalized;
    }

    /// <summary>Returns whether a target URL belongs to the configured fleet site host.</summary>
    public static bool TargetBelongsToSite(string targetUrl, string siteBaseUrl)
    {
        var target = new Uri(CanonicalizeTarget(targetUrl, "url"), UriKind.Absolute);
        var site = new Uri(CanonicalizeTarget(siteBaseUrl, "url"), UriKind.Absolute);
        var targetHost = target.IdnHost.TrimEnd('.');
        var siteHost = site.IdnHost.TrimEnd('.');
        return targetHost.Equals(siteHost, StringComparison.OrdinalIgnoreCase) ||
               targetHost.EndsWith("." + siteHost, StringComparison.OrdinalIgnoreCase);
    }

    private static WebPerformanceObservation NormalizeObservation(
        WebPerformanceObservation value,
        string measurementKind,
        IReadOnlyDictionary<string, string> allowedMetrics,
        int index)
    {
        ArgumentNullException.ThrowIfNull(value);
        var metric = RequiredIdentifier(value.Metric, $"observations[{index}].metric");
        if (!allowedMetrics.TryGetValue(metric, out var expectedUnit))
            throw new ArgumentException($"Metric '{metric}' is not valid for {measurementKind} performance evidence.", nameof(value));
        var unit = RequiredIdentifier(value.Unit, $"observations[{index}].unit");
        if (!unit.Equals(expectedUnit, StringComparison.Ordinal))
            throw new ArgumentException($"Metric '{metric}' requires unit '{expectedUnit}'.", nameof(value));
        if (!double.IsFinite(value.Value) || value.Value < 0 || metric == "performance-score" && value.Value > 1)
            throw new ArgumentException($"Metric '{metric}' requires a finite value in its valid range.", nameof(value));

        var histogram = (value.Histogram ?? Array.Empty<WebPerformanceHistogramBin>())
            .Select((bin, binIndex) => NormalizeBin(bin, metric, binIndex))
            .ToArray();
        if (measurementKind == "lab")
        {
            if (value.Percentile is not null || value.PeriodStartDate is not null || value.PeriodEndDate is not null || histogram.Length != 0)
                throw new ArgumentException("Laboratory metrics cannot claim field percentiles, periods, or histograms.", nameof(value));
        }
        else
        {
            if (value.Percentile != 75 || value.PeriodStartDate is not DateOnly start || value.PeriodEndDate is not DateOnly end || start > end)
                throw new ArgumentException("Field metrics require a p75 value and a valid aggregation period.", nameof(value));
            if (end.DayNumber - start.DayNumber != 27)
                throw new ArgumentException("CrUX field metrics require the provider's 28-day aggregation period.", nameof(value));
            if (histogram.Length == 0)
                throw new ArgumentException("Field metrics require provider histogram evidence.", nameof(value));
            var density = histogram.Sum(bin => bin.Density);
            if (density < 0.999d || density > 1.001d)
                throw new ArgumentException($"Metric '{metric}' histogram densities must sum to one.", nameof(value));
        }

        return new WebPerformanceObservation
        {
            Metric = metric,
            Value = value.Value == 0d ? 0d : value.Value,
            Unit = unit,
            Percentile = value.Percentile,
            PeriodStartDate = value.PeriodStartDate,
            PeriodEndDate = value.PeriodEndDate,
            Histogram = histogram
        };
    }

    private static WebPerformanceHistogramBin NormalizeBin(WebPerformanceHistogramBin bin, string metric, int index)
    {
        ArgumentNullException.ThrowIfNull(bin);
        if (bin.Start is double start && (!double.IsFinite(start) || start < 0) ||
            bin.End is double end && (!double.IsFinite(end) || end < 0) ||
            bin.Start is double lower && bin.End is double upper && lower >= upper ||
            !double.IsFinite(bin.Density) || bin.Density < 0 || bin.Density > 1)
        {
            throw new ArgumentException($"Metric '{metric}' histogram bin {index} is invalid.", nameof(bin));
        }
        return new WebPerformanceHistogramBin
        {
            Start = bin.Start is 0d ? 0d : bin.Start,
            End = bin.End is 0d ? 0d : bin.End,
            Density = bin.Density == 0d ? 0d : bin.Density
        };
    }

    /// <summary>Returns a canonical HTTP(S) URL or origin suitable for durable identity.</summary>
    public static string CanonicalizeTarget(string? value, string targetKind)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Performance target must be an absolute HTTP(S) URL without user info or fragment.", nameof(value));
        if (targetKind == "origin" && (uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query)))
            throw new ArgumentException("Origin performance targets cannot contain a path or query.", nameof(value));
        var builder = new UriBuilder(uri)
        {
            Host = uri.IdnHost.TrimEnd('.').ToLowerInvariant(),
            Fragment = string.Empty
        };
        if (targetKind == "origin")
            builder.Path = "/";
        return builder.Uri.AbsoluteUri;
    }

    private static string RunFingerprint(WebPerformanceObservationBatch batch) => WebSearchIdentityHasher.Compute(
        batch.SchemaVersion.ToString(CultureInfo.InvariantCulture), batch.Provider, batch.SiteId,
        batch.CollectedAtUtc.ToString("O", CultureInfo.InvariantCulture), batch.SourceKind, batch.Status,
        batch.MeasurementKind, batch.TargetKind, batch.TargetUrl, batch.FormFactor, batch.ToolVersion,
        batch.ConfigurationHash, batch.EvidenceReference, batch.ZeroDataConfirmed ? "zero-data-confirmed" : null,
        string.Join("|", batch.Observations.Select(MetricFingerprint)));

    private static string MetricFingerprint(WebPerformanceObservation value) => WebSearchIdentityHasher.Compute(
        value.Metric, value.Value.ToString("R", CultureInfo.InvariantCulture), value.Unit,
        value.Percentile?.ToString(CultureInfo.InvariantCulture), value.PeriodStartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        value.PeriodEndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        string.Join(";", value.Histogram.Select(bin => string.Join(",", bin.Start?.ToString("R", CultureInfo.InvariantCulture),
            bin.End?.ToString("R", CultureInfo.InvariantCulture), bin.Density.ToString("R", CultureInfo.InvariantCulture)))));

    private static string RequiredIdentifier(string? value, string field) =>
        Optional(value)?.ToLowerInvariant() ?? throw new ArgumentException($"Performance observation batch requires {field}.", field);

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
