using System.Text.Json.Serialization;

namespace PowerForge.Web;

/// <summary>A versioned import batch containing normalized search performance observations.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class WebSearchObservationBatch
{
    /// <summary>Current JSON contract version.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>JSON contract version.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Optional caller-supplied run identifier. A deterministic identifier is generated when omitted.</summary>
    public string? RunId { get; set; }

    /// <summary>Provider identifier, for example <c>google-search-console</c> or <c>bing-webmaster</c>.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Stable fleet site identifier.</summary>
    public string SiteId { get; set; } = string.Empty;

    /// <summary>Time at which the provider collection or export completed.</summary>
    public DateTimeOffset CollectedAtUtc { get; set; }

    /// <summary>Acquisition method such as <c>api</c>, <c>csv-import</c>, or <c>fixture</c>.</summary>
    public string SourceKind { get; set; } = "import";

    /// <summary>Run status. Supported values are <c>complete</c> and <c>partial</c>.</summary>
    public string Status { get; set; } = "complete";

    /// <summary>Optional hash of the fleet/provider configuration used by the collection run.</summary>
    public string? ConfigurationHash { get; set; }

    /// <summary>Optional non-secret reference to separately retained raw evidence.</summary>
    public string? EvidenceReference { get; set; }

    /// <summary>Search performance observations in this run.</summary>
    public WebSearchObservation[] Observations { get; set; } = Array.Empty<WebSearchObservation>();
}

/// <summary>A provider-neutral daily search performance observation.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class WebSearchObservation
{
    /// <summary>Deterministic identity assigned during normalization.</summary>
    public string ObservationKey { get; set; } = string.Empty;

    /// <summary>Provider identifier inherited from the batch when omitted.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Fleet site identifier inherited from the batch when omitted.</summary>
    public string SiteId { get; set; } = string.Empty;

    /// <summary>Provider reporting date.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Canonical page URL when the provider row includes a page dimension.</summary>
    public string? Page { get; set; }

    /// <summary>Search query when the provider row includes a query dimension.</summary>
    public string? Query { get; set; }

    /// <summary>Provider country dimension, normally an ISO country code.</summary>
    public string? Country { get; set; }

    /// <summary>Provider device dimension.</summary>
    public string? Device { get; set; }

    /// <summary>Provider search type or surface dimension.</summary>
    public string? SearchType { get; set; }

    /// <summary>Clicks reported for this row.</summary>
    public long Clicks { get; set; }

    /// <summary>Impressions reported for this row.</summary>
    public long Impressions { get; set; }

    /// <summary>Provider-reported CTR as a value from zero to one. It is derived from counts when omitted.</summary>
    public double? ClickThroughRate { get; set; }

    /// <summary>Provider-reported average position.</summary>
    public double? AveragePosition { get; set; }

    /// <summary>Optional non-secret raw evidence reference overriding the batch reference.</summary>
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
