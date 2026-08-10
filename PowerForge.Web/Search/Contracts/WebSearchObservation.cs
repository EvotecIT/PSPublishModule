using System.Text.Json.Serialization;

namespace PowerForge.Web;

/// <summary>A versioned import batch containing normalized search performance observations.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class WebSearchObservationBatch
{
    private WebSearchObservation[] _observations = Array.Empty<WebSearchObservation>();
    private WebSearchObservationCollectionCoverage? _collectionCoverage;
    private bool _zeroDataConfirmed;

    /// <summary>Current JSON contract version.</summary>
    public const int CurrentSchemaVersion = 3;

    /// <summary>Oldest JSON contract version that remains import-compatible.</summary>
    public const int MinimumSupportedSchemaVersion = 1;

    /// <summary>JSON contract version.</summary>
    [JsonPropertyName("schemaVersion"), JsonRequired]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Optional caller-supplied run identifier. A deterministic identifier is generated when omitted.</summary>
    [JsonPropertyName("runId")]
    public string? RunId { get; set; }

    /// <summary>Provider identifier, for example <c>google-search-console</c> or <c>bing-webmaster</c>.</summary>
    [JsonPropertyName("provider"), JsonRequired]
    public string Provider { get; set; } = string.Empty;

    /// <summary>Stable fleet site identifier.</summary>
    [JsonPropertyName("siteId"), JsonRequired]
    public string SiteId { get; set; } = string.Empty;

    /// <summary>Time at which the provider collection or export completed.</summary>
    [JsonPropertyName("collectedAtUtc"), JsonRequired]
    [JsonConverter(typeof(WebSearchRequiredOffsetDateTimeConverter))]
    public DateTimeOffset CollectedAtUtc { get; set; }

    /// <summary>Acquisition method such as <c>api</c>, <c>csv-import</c>, or <c>fixture</c>.</summary>
    [JsonPropertyName("sourceKind"), JsonRequired]
    public string SourceKind { get; set; } = "import";

    /// <summary>Run status. Supported values are <c>complete</c> and <c>partial</c>.</summary>
    [JsonPropertyName("status"), JsonRequired]
    public string Status { get; set; } = "complete";

    /// <summary>Optional hash of the fleet/provider configuration used by the collection run.</summary>
    [JsonPropertyName("configurationHash")]
    public string? ConfigurationHash { get; set; }

    /// <summary>Optional non-secret reference to separately retained raw evidence.</summary>
    [JsonPropertyName("evidenceReference")]
    public string? EvidenceReference { get; set; }

    /// <summary>Optional durable description of the requested and completed provider collection partitions.</summary>
    [JsonPropertyName("collectionCoverage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WebSearchObservationCollectionCoverage? CollectionCoverage
    {
        get => _collectionCoverage;
        set
        {
            _collectionCoverage = value;
            CollectionCoverageSpecified = true;
        }
    }

    /// <summary>Whether collectionCoverage was explicitly supplied rather than omitted.</summary>
    [JsonIgnore]
    internal bool CollectionCoverageSpecified { get; private set; }

    /// <summary>Whether a successful provider request explicitly confirmed that the requested slice contained no rows.</summary>
    [JsonPropertyName("zeroDataConfirmed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ZeroDataConfirmed
    {
        get => _zeroDataConfirmed;
        set
        {
            _zeroDataConfirmed = value;
            ZeroDataConfirmedSpecified = true;
        }
    }

    /// <summary>Whether zeroDataConfirmed was explicitly supplied rather than omitted.</summary>
    [JsonIgnore]
    internal bool ZeroDataConfirmedSpecified { get; private set; }

    /// <summary>Search performance observations in this run.</summary>
    [JsonPropertyName("observations"), JsonRequired]
    public WebSearchObservation[] Observations
    {
        get => _observations;
        set
        {
            ObservationsWasNull = value is null;
            _observations = value ?? Array.Empty<WebSearchObservation>();
        }
    }

    /// <summary>Whether the JSON contract explicitly supplied a null observations value.</summary>
    [JsonIgnore]
    internal bool ObservationsWasNull { get; private set; }
}

/// <summary>Durable coverage metadata for a bounded provider collection request.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class WebSearchObservationCollectionCoverage
{
    private string? _mode;

    /// <summary>
    /// Coverage acquisition mode. Version 2 omits this value and means <c>daily</c>.
    /// Version 3 supports <c>daily</c> and <c>snapshot</c>.
    /// </summary>
    [JsonPropertyName("mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mode
    {
        get => _mode;
        set
        {
            ModeSpecified = true;
            _mode = value;
        }
    }

    /// <summary>Whether mode was explicitly supplied, including an explicit null JSON value.</summary>
    [JsonIgnore]
    internal bool ModeSpecified { get; private set; }

    /// <summary>Inclusive first reporting date requested from the provider.</summary>
    [JsonPropertyName("fromDate"), JsonRequired]
    public DateOnly FromDate { get; set; }

    /// <summary>Inclusive last reporting date requested from the provider.</summary>
    [JsonPropertyName("throughDate"), JsonRequired]
    public DateOnly ThroughDate { get; set; }

    /// <summary>Provider search surface covered by the request.</summary>
    [JsonPropertyName("searchType")]
    public string? SearchType { get; set; }

    /// <summary>
    /// Dates whose daily partitions completed in <c>daily</c> mode, or dates explicitly present in a successful
    /// provider response in <c>snapshot</c> mode.
    /// </summary>
    [JsonPropertyName("completedDates"), JsonRequired]
    public DateOnly[] CompletedDates { get; set; } = Array.Empty<DateOnly>();

    /// <summary>First daily partition that did not complete, when the batch is partial.</summary>
    [JsonPropertyName("failedDate")]
    public DateOnly? FailedDate { get; set; }

    /// <summary>Stable non-secret failure category for a partial collection.</summary>
    [JsonPropertyName("failureCategory")]
    public string? FailureCategory { get; set; }
}

/// <summary>A provider-neutral daily search performance observation.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class WebSearchObservation
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

    /// <summary>Canonical page URL when the provider row includes a page dimension.</summary>
    [JsonPropertyName("page")]
    public string? Page { get; set; }

    /// <summary>Search query when the provider row includes a query dimension.</summary>
    [JsonPropertyName("query")]
    public string? Query { get; set; }

    /// <summary>Provider country dimension, normally an ISO country code.</summary>
    [JsonPropertyName("country")]
    public string? Country { get; set; }

    /// <summary>Provider device dimension.</summary>
    [JsonPropertyName("device")]
    public string? Device { get; set; }

    /// <summary>Provider search type or surface dimension.</summary>
    [JsonPropertyName("searchType")]
    public string? SearchType { get; set; }

    /// <summary>Clicks reported for this row.</summary>
    [JsonPropertyName("clicks"), JsonRequired]
    public long Clicks { get; set; }

    /// <summary>Impressions reported for this row.</summary>
    [JsonPropertyName("impressions"), JsonRequired]
    public long Impressions { get; set; }

    /// <summary>Provider-reported CTR as a value from zero to one. It is derived from counts when omitted.</summary>
    [JsonPropertyName("clickThroughRate")]
    public double? ClickThroughRate { get; set; }

    /// <summary>Provider-reported average position.</summary>
    [JsonPropertyName("averagePosition")]
    public double? AveragePosition { get; set; }

    /// <summary>Optional non-secret raw evidence reference overriding the batch reference.</summary>
    [JsonPropertyName("evidenceReference")]
    public string? EvidenceReference { get; set; }
}

/// <summary>Result of importing an observation batch into durable storage.</summary>
public sealed class WebSearchObservationImportResult
{
    /// <summary>Normalized run identifier.</summary>
    public string RunId { get; set; } = string.Empty;

    /// <summary>Provider identifier.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Fleet site identifier.</summary>
    public string SiteId { get; set; } = string.Empty;

    /// <summary>Number of observations supplied by the normalized batch.</summary>
    public int InputCount { get; set; }

    /// <summary>Number of observations inserted for the first time.</summary>
    public int InsertedCount { get; set; }

    /// <summary>Number of already-stored observations ignored by deterministic identity.</summary>
    public int DuplicateCount { get; set; }

    /// <summary>Database schema version used for the import.</summary>
    public int DatabaseSchemaVersion { get; set; }
}

/// <summary>Filter used to retrieve normalized search observations.</summary>
public sealed class WebSearchObservationQuery
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
