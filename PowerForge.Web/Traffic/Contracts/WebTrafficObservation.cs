using System.Text.Json.Serialization;

namespace PowerForge.Web;

/// <summary>A versioned provider-neutral batch of website traffic observations.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class WebTrafficObservationBatch
{
    /// <summary>Current traffic observation contract version.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>JSON contract version.</summary>
    [JsonPropertyName("schemaVersion"), JsonRequired]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Optional external run identifier; a deterministic value is generated when omitted.</summary>
    [JsonPropertyName("runId")]
    public string? RunId { get; set; }

    /// <summary>Provider identifier.</summary>
    [JsonPropertyName("provider"), JsonRequired]
    public string Provider { get; set; } = string.Empty;

    /// <summary>Stable fleet site identifier.</summary>
    [JsonPropertyName("siteId"), JsonRequired]
    public string SiteId { get; set; } = string.Empty;

    /// <summary>Time at which collection completed.</summary>
    [JsonPropertyName("collectedAtUtc"), JsonRequired]
    [JsonConverter(typeof(WebSearchRequiredOffsetDateTimeConverter))]
    public DateTimeOffset CollectedAtUtc { get; set; }

    /// <summary>Acquisition method such as <c>api</c> or <c>fixture</c>.</summary>
    [JsonPropertyName("sourceKind"), JsonRequired]
    public string SourceKind { get; set; } = "api";

    /// <summary>Run status: <c>complete</c> or <c>partial</c>.</summary>
    [JsonPropertyName("status"), JsonRequired]
    public string Status { get; set; } = "complete";

    /// <summary>Optional hash of the non-secret fleet configuration used for collection.</summary>
    [JsonPropertyName("configurationHash")]
    public string? ConfigurationHash { get; set; }

    /// <summary>Optional non-secret reference to separately retained evidence.</summary>
    [JsonPropertyName("evidenceReference")]
    public string? EvidenceReference { get; set; }

    /// <summary>Durable daily provider coverage.</summary>
    [JsonPropertyName("collectionCoverage"), JsonRequired]
    public WebTrafficObservationCollectionCoverage CollectionCoverage { get; set; } = new();

    /// <summary>Whether every requested daily partition completed and explicitly returned no rows.</summary>
    [JsonPropertyName("zeroDataConfirmed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ZeroDataConfirmed { get; set; }

    /// <summary>Traffic observations in this run.</summary>
    [JsonPropertyName("observations"), JsonRequired]
    public WebTrafficObservation[] Observations { get; set; } = Array.Empty<WebTrafficObservation>();
}

/// <summary>Durable coverage for consecutive daily traffic collection.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class WebTrafficObservationCollectionCoverage
{
    /// <summary>Inclusive first reporting date requested.</summary>
    [JsonPropertyName("fromDate"), JsonRequired]
    public DateOnly FromDate { get; set; }

    /// <summary>Inclusive last reporting date requested.</summary>
    [JsonPropertyName("throughDate"), JsonRequired]
    public DateOnly ThroughDate { get; set; }

    /// <summary>Consecutive daily partitions completed before any failure.</summary>
    [JsonPropertyName("completedDates"), JsonRequired]
    public DateOnly[] CompletedDates { get; set; } = Array.Empty<DateOnly>();

    /// <summary>First daily partition that did not complete.</summary>
    [JsonPropertyName("failedDate")]
    public DateOnly? FailedDate { get; set; }

    /// <summary>Stable non-secret failure category.</summary>
    [JsonPropertyName("failureCategory")]
    public string? FailureCategory { get; set; }
}

/// <summary>A provider-neutral daily website traffic observation.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class WebTrafficObservation
{
    /// <summary>Deterministic identity assigned during normalization.</summary>
    [JsonPropertyName("observationKey")]
    public string ObservationKey { get; set; } = string.Empty;

    /// <summary>Provider identifier inherited from the batch when omitted.</summary>
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    /// <summary>Fleet site identifier inherited from the batch when omitted.</summary>
    [JsonPropertyName("siteId")]
    public string SiteId { get; set; } = string.Empty;

    /// <summary>Provider reporting date.</summary>
    [JsonPropertyName("date"), JsonRequired]
    public DateOnly Date { get; set; }

    /// <summary>Canonical lowercase HTTP host.</summary>
    [JsonPropertyName("host"), JsonRequired]
    public string Host { get; set; } = string.Empty;

    /// <summary>Request path beginning with a slash.</summary>
    [JsonPropertyName("path"), JsonRequired]
    public string Path { get; set; } = string.Empty;

    /// <summary>Estimated end-user HTTP request count reported by the provider.</summary>
    [JsonPropertyName("requests"), JsonRequired]
    public long Requests { get; set; }

    /// <summary>Estimated visit count reported by the provider.</summary>
    [JsonPropertyName("visits"), JsonRequired]
    public long Visits { get; set; }

    /// <summary>Estimated edge response bytes reported by the provider.</summary>
    [JsonPropertyName("edgeResponseBytes"), JsonRequired]
    public long EdgeResponseBytes { get; set; }

    /// <summary>Provider sampling interval. Values above one identify adaptively sampled estimates.</summary>
    [JsonPropertyName("sampleInterval"), JsonRequired]
    public double SampleInterval { get; set; } = 1d;

    /// <summary>Optional non-secret evidence reference overriding the batch value.</summary>
    [JsonPropertyName("evidenceReference")]
    public string? EvidenceReference { get; set; }
}

/// <summary>Result of importing traffic observations.</summary>
public sealed class WebTrafficObservationImportResult
{
    /// <summary>Normalized run identifier.</summary>
    public string RunId { get; set; } = string.Empty;
    /// <summary>Provider identifier.</summary>
    public string Provider { get; set; } = string.Empty;
    /// <summary>Fleet site identifier.</summary>
    public string SiteId { get; set; } = string.Empty;
    /// <summary>Number of supplied observations.</summary>
    public int InputCount { get; set; }
    /// <summary>Number of newly inserted observations.</summary>
    public int InsertedCount { get; set; }
    /// <summary>Number of deterministic duplicates ignored.</summary>
    public int DuplicateCount { get; set; }
    /// <summary>Fleet database schema version.</summary>
    public int DatabaseSchemaVersion { get; set; }
}

/// <summary>Filter for retrieving normalized traffic observations.</summary>
public sealed class WebTrafficObservationQuery
{
    /// <summary>Required fleet site identifier.</summary>
    public string SiteId { get; set; } = string.Empty;
    /// <summary>Optional provider identifier.</summary>
    public string? Provider { get; set; }
    /// <summary>Optional inclusive start date.</summary>
    public DateOnly? FromDate { get; set; }
    /// <summary>Optional inclusive end date.</summary>
    public DateOnly? ThroughDate { get; set; }
}

/// <summary>Traffic observations together with the selected collection evidence that makes their completeness interpretable.</summary>
public sealed class WebTrafficObservationQueryResult
{
    /// <summary>Whether the configured durable store exists.</summary>
    public bool StoreExists { get; set; }
    /// <summary>Whether at least one collection run covers the requested filters.</summary>
    public bool HasEvidence { get; set; }
    /// <summary>Whether any selected date is represented only by partial collection evidence.</summary>
    public bool HasPartialEvidence { get; set; }
    /// <summary>Whether a bounded requested range contains dates with no matching selected run evidence.</summary>
    public bool HasCoverageGaps { get; set; }
    /// <summary>Dates missing from a bounded requested range.</summary>
    public DateOnly[] MissingDates { get; set; } = Array.Empty<DateOnly>();
    /// <summary>Whether a selected complete run explicitly confirms zero rows for its entire requested slice.</summary>
    public bool HasExplicitZeroEvidence { get; set; }
    /// <summary>Runs selected as the best available evidence for one or more reporting dates.</summary>
    public WebTrafficObservationRunEvidence[] SelectedRuns { get; set; } = Array.Empty<WebTrafficObservationRunEvidence>();
    /// <summary>Observations belonging to the selected run for each reporting date.</summary>
    public WebTrafficObservation[] Observations { get; set; } = Array.Empty<WebTrafficObservation>();
}

/// <summary>Collection-run provenance selected for one or more traffic reporting dates.</summary>
public sealed class WebTrafficObservationRunEvidence
{
    /// <summary>Stable normalized run identifier.</summary>
    public string RunId { get; set; } = string.Empty;
    /// <summary>Provider identifier.</summary>
    public string Provider { get; set; } = string.Empty;
    /// <summary>Fleet site identifier.</summary>
    public string SiteId { get; set; } = string.Empty;
    /// <summary>Collection completion time.</summary>
    public DateTimeOffset CollectedAtUtc { get; set; }
    /// <summary>Complete or partial run status.</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Whether the whole requested slice was explicitly confirmed empty.</summary>
    public bool ZeroDataConfirmed { get; set; }
    /// <summary>Coverage recorded by the selected run.</summary>
    public WebTrafficObservationCollectionCoverage CollectionCoverage { get; set; } = new();
    /// <summary>Reporting dates for which this run won revision selection.</summary>
    public DateOnly[] SelectedDates { get; set; } = Array.Empty<DateOnly>();
}
