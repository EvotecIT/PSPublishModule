using System.Text.Json.Serialization;

namespace PowerForge.Web;

/// <summary>A versioned provider-neutral batch of laboratory or field performance evidence for one target.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class WebPerformanceObservationBatch
{
    /// <summary>Current performance observation contract version.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>JSON contract version.</summary>
    [JsonPropertyName("schemaVersion"), JsonRequired]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Optional external run identifier; a deterministic value is generated when omitted.</summary>
    [JsonPropertyName("runId")]
    public string? RunId { get; set; }

    /// <summary>Provider registration identifier.</summary>
    [JsonPropertyName("provider"), JsonRequired]
    public string Provider { get; set; } = string.Empty;

    /// <summary>Stable fleet site identifier.</summary>
    [JsonPropertyName("siteId"), JsonRequired]
    public string SiteId { get; set; } = string.Empty;

    /// <summary>Time at which the evidence was collected or imported.</summary>
    [JsonPropertyName("collectedAtUtc"), JsonRequired]
    [JsonConverter(typeof(WebSearchRequiredOffsetDateTimeConverter))]
    public DateTimeOffset CollectedAtUtc { get; set; }

    /// <summary>Acquisition method such as <c>lighthouse-json</c> or <c>api</c>.</summary>
    [JsonPropertyName("sourceKind"), JsonRequired]
    public string SourceKind { get; set; } = string.Empty;

    /// <summary>Run status: <c>complete</c> or <c>partial</c>.</summary>
    [JsonPropertyName("status"), JsonRequired]
    public string Status { get; set; } = "complete";

    /// <summary>Measurement semantics: <c>lab</c> or <c>field</c>.</summary>
    [JsonPropertyName("measurementKind"), JsonRequired]
    public string MeasurementKind { get; set; } = string.Empty;

    /// <summary>Target scope: <c>url</c> or <c>origin</c>.</summary>
    [JsonPropertyName("targetKind"), JsonRequired]
    public string TargetKind { get; set; } = string.Empty;

    /// <summary>Canonical HTTP(S) URL or origin measured by the provider.</summary>
    [JsonPropertyName("targetUrl"), JsonRequired]
    public string TargetUrl { get; set; } = string.Empty;

    /// <summary>Device form factor: <c>all</c>, <c>phone</c>, <c>desktop</c>, or <c>tablet</c>.</summary>
    [JsonPropertyName("formFactor"), JsonRequired]
    public string FormFactor { get; set; } = string.Empty;

    /// <summary>Optional producing tool or API version.</summary>
    [JsonPropertyName("toolVersion")]
    public string? ToolVersion { get; set; }

    /// <summary>Optional hash of the non-secret fleet configuration used for collection.</summary>
    [JsonPropertyName("configurationHash")]
    public string? ConfigurationHash { get; set; }

    /// <summary>Optional non-secret reference to separately retained raw evidence.</summary>
    [JsonPropertyName("evidenceReference")]
    public string? EvidenceReference { get; set; }

    /// <summary>Whether a complete provider query explicitly found no field record for the requested target.</summary>
    [JsonPropertyName("zeroDataConfirmed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ZeroDataConfirmed { get; set; }

    /// <summary>Normalized metric observations.</summary>
    [JsonPropertyName("observations"), JsonRequired]
    public WebPerformanceObservation[] Observations { get; set; } = Array.Empty<WebPerformanceObservation>();
}

/// <summary>One normalized performance metric.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class WebPerformanceObservation
{
    /// <summary>Deterministic identity assigned during normalization.</summary>
    [JsonPropertyName("observationKey")]
    public string ObservationKey { get; set; } = string.Empty;

    /// <summary>Stable metric name.</summary>
    [JsonPropertyName("metric"), JsonRequired]
    public string Metric { get; set; } = string.Empty;

    /// <summary>Numeric metric value.</summary>
    [JsonPropertyName("value"), JsonRequired]
    public double Value { get; set; }

    /// <summary>Stable unit such as <c>milliseconds</c>, <c>score</c>, or <c>unitless</c>.</summary>
    [JsonPropertyName("unit"), JsonRequired]
    public string Unit { get; set; } = string.Empty;

    /// <summary>Percentile represented by a field metric; omitted for laboratory values.</summary>
    [JsonPropertyName("percentile")]
    public int? Percentile { get; set; }

    /// <summary>Inclusive first date of the field aggregation period.</summary>
    [JsonPropertyName("periodStartDate")]
    public DateOnly? PeriodStartDate { get; set; }

    /// <summary>Inclusive last date of the field aggregation period.</summary>
    [JsonPropertyName("periodEndDate")]
    public DateOnly? PeriodEndDate { get; set; }

    /// <summary>Optional provider histogram bins retained for field evidence.</summary>
    [JsonPropertyName("histogram")]
    public WebPerformanceHistogramBin[] Histogram { get; set; } = Array.Empty<WebPerformanceHistogramBin>();
}

/// <summary>One provider histogram bin.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class WebPerformanceHistogramBin
{
    /// <summary>Inclusive lower boundary.</summary>
    [JsonPropertyName("start")]
    public double? Start { get; set; }

    /// <summary>Exclusive upper boundary, omitted for an open-ended bin.</summary>
    [JsonPropertyName("end")]
    public double? End { get; set; }

    /// <summary>Proportion of eligible experiences in this bin.</summary>
    [JsonPropertyName("density"), JsonRequired]
    public double Density { get; set; }
}

/// <summary>Result of importing a performance observation batch.</summary>
public sealed class WebPerformanceObservationImportResult
{
    /// <summary>Normalized run identifier.</summary>
    public string RunId { get; set; } = string.Empty;
    /// <summary>Provider identifier.</summary>
    public string Provider { get; set; } = string.Empty;
    /// <summary>Fleet site identifier.</summary>
    public string SiteId { get; set; } = string.Empty;
    /// <summary>Number of supplied metric observations.</summary>
    public int InputCount { get; set; }
    /// <summary>Number of newly inserted metric observations.</summary>
    public int InsertedCount { get; set; }
    /// <summary>Number of deterministic duplicates ignored.</summary>
    public int DuplicateCount { get; set; }
    /// <summary>Fleet database schema version.</summary>
    public int DatabaseSchemaVersion { get; set; }
}

/// <summary>Filter for retrieving the best available performance evidence.</summary>
public sealed class WebPerformanceObservationQuery
{
    /// <summary>Required fleet site identifier.</summary>
    public string SiteId { get; set; } = string.Empty;
    /// <summary>Optional provider identifier.</summary>
    public string? Provider { get; set; }
    /// <summary>Optional <c>lab</c> or <c>field</c> filter.</summary>
    public string? MeasurementKind { get; set; }
    /// <summary>Optional exact canonical target URL.</summary>
    public string? TargetUrl { get; set; }
    /// <summary>Optional device form factor.</summary>
    public string? FormFactor { get; set; }
}

/// <summary>Performance observations together with selected collection provenance.</summary>
public sealed class WebPerformanceObservationQueryResult
{
    /// <summary>Whether the configured durable store exists.</summary>
    public bool StoreExists { get; set; }
    /// <summary>Whether at least one run matches the requested filters.</summary>
    public bool HasEvidence { get; set; }
    /// <summary>Whether any selected target is represented only by partial evidence.</summary>
    public bool HasPartialEvidence { get; set; }
    /// <summary>Whether a selected complete field query explicitly found no record.</summary>
    public bool HasExplicitZeroEvidence { get; set; }
    /// <summary>Best complete-before-recency evidence grouped with its run provenance.</summary>
    public WebPerformanceObservationEvidenceSet[] EvidenceSets { get; set; } = Array.Empty<WebPerformanceObservationEvidenceSet>();
}

/// <summary>One selected performance run and the metrics that belong to it.</summary>
public sealed class WebPerformanceObservationEvidenceSet
{
    /// <summary>Run, provider, target, and form-factor provenance.</summary>
    public WebPerformanceObservationRunEvidence Run { get; set; } = new();
    /// <summary>Metric observations belonging only to <see cref="Run"/>.</summary>
    public WebPerformanceObservation[] Observations { get; set; } = Array.Empty<WebPerformanceObservation>();
}

/// <summary>Selected run provenance for one performance target and measurement shape.</summary>
public sealed class WebPerformanceObservationRunEvidence
{
    /// <summary>Normalized run identifier.</summary>
    public string RunId { get; set; } = string.Empty;
    /// <summary>Provider identifier.</summary>
    public string Provider { get; set; } = string.Empty;
    /// <summary>Fleet site identifier.</summary>
    public string SiteId { get; set; } = string.Empty;
    /// <summary>Collection or import time.</summary>
    public DateTimeOffset CollectedAtUtc { get; set; }
    /// <summary>Complete or partial run status.</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Lab or field measurement semantics.</summary>
    public string MeasurementKind { get; set; } = string.Empty;
    /// <summary>URL or origin target scope.</summary>
    public string TargetKind { get; set; } = string.Empty;
    /// <summary>Canonical measured target.</summary>
    public string TargetUrl { get; set; } = string.Empty;
    /// <summary>Device form factor.</summary>
    public string FormFactor { get; set; } = string.Empty;
    /// <summary>Producing tool or API version.</summary>
    public string? ToolVersion { get; set; }
    /// <summary>Whether this complete field run explicitly found no record.</summary>
    public bool ZeroDataConfirmed { get; set; }
}
