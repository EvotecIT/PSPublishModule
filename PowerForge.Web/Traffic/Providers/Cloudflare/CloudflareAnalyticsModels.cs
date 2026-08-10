using System.Text.Json.Serialization;

namespace PowerForge.Web;

/// <summary>Resolves a Cloudflare API token without placing it in configuration or output.</summary>
public interface ICloudflareAnalyticsTokenProvider
{
    /// <summary>Returns the token for the current collection request.</summary>
    Task<string> GetTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>Options for bounded daily Cloudflare traffic collection.</summary>
public sealed class CloudflareAnalyticsCollectionOptions
{
    /// <summary>Fleet provider identifier.</summary>
    public string ProviderId { get; set; } = "cloudflare";
    /// <summary>Fleet site identifier.</summary>
    public string SiteId { get; set; } = string.Empty;
    /// <summary>Cloudflare zone identifier.</summary>
    public string ZoneId { get; set; } = string.Empty;
    /// <summary>Owning fleet site base URL whose host must belong to the configured zone.</summary>
    public string SiteBaseUrl { get; set; } = string.Empty;
    /// <summary>Inclusive first reporting date.</summary>
    public DateOnly FromDate { get; set; }
    /// <summary>Inclusive last reporting date.</summary>
    public DateOnly ThroughDate { get; set; }
    /// <summary>Optional non-secret configuration fingerprint.</summary>
    public string? ConfigurationHash { get; set; }
    /// <summary>Optional non-secret evidence reference.</summary>
    public string? EvidenceReference { get; set; }
}

/// <summary>Observed Cloudflare dataset capability and plan-specific query boundaries.</summary>
public sealed class CloudflareAnalyticsCapabilityProbeResult
{
    /// <summary>Whether the configured zone and dataset are usable.</summary>
    public bool Success { get; set; }
    /// <summary>Whether the HTTP request adaptive group dataset is enabled.</summary>
    public bool DatasetEnabled { get; set; }
    /// <summary>Canonical zone name verified against the owning fleet site.</summary>
    public string? ZoneName { get; set; }
    /// <summary>Number of Cloudflare HTTP requests attempted by the probe.</summary>
    public int RequestCount { get; set; }
    /// <summary>Effective maximum rows requested by the collector.</summary>
    public int MaxPageSize { get; set; }
    /// <summary>Provider-reported maximum query duration, when available.</summary>
    public int? MaxDurationSeconds { get; set; }
    /// <summary>Provider-reported retention boundary, when available.</summary>
    public int? NotOlderThanSeconds { get; set; }
    /// <summary>Stable failure category.</summary>
    public string? ErrorCode { get; set; }
    /// <summary>Sanitized failure description.</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>Cloudflare traffic collection outcome.</summary>
public sealed class CloudflareAnalyticsCollectionResult
{
    /// <summary>Whether every requested daily partition completed.</summary>
    public bool Success { get; set; }
    /// <summary>Total Cloudflare HTTP request count, including zone ownership and capability probes.</summary>
    public int RequestCount { get; set; }
    /// <summary>Number of completed daily partitions.</summary>
    public int CompletedDateCount { get; set; }
    /// <summary>Capability probe evidence.</summary>
    public CloudflareAnalyticsCapabilityProbeResult Probe { get; set; } = new();
    /// <summary>Stable failure category for partial collection.</summary>
    public string? ErrorCode { get; set; }
    /// <summary>Sanitized failure description for partial collection.</summary>
    public string? ErrorMessage { get; set; }
    /// <summary>Complete or partial normalized traffic batch.</summary>
    public WebTrafficObservationBatch Batch { get; set; } = new();
}

internal sealed class CloudflareGraphQlEnvelope<T>
{
    [JsonPropertyName("data")]
    public T? Data { get; set; }

    [JsonPropertyName("errors")]
    public CloudflareGraphQlError[]? Errors { get; set; }
}

internal sealed class CloudflareGraphQlError
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

internal sealed class CloudflareApiEnvelope<T>
{
    [JsonPropertyName("success")]
    public bool? Success { get; set; }

    [JsonPropertyName("result")]
    public T? Result { get; set; }

    [JsonPropertyName("errors")]
    public CloudflareApiError[] Errors { get; set; } = Array.Empty<CloudflareApiError>();
}

internal sealed class CloudflareApiError
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

internal sealed class CloudflareZoneDetails
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal sealed class CloudflareCapabilityData
{
    [JsonPropertyName("viewer")]
    public CloudflareCapabilityViewer? Viewer { get; set; }
}

internal sealed class CloudflareCapabilityViewer
{
    [JsonPropertyName("zones")]
    public CloudflareCapabilityZone[] Zones { get; set; } = Array.Empty<CloudflareCapabilityZone>();
}

internal sealed class CloudflareCapabilityZone
{
    [JsonPropertyName("settings")]
    public CloudflareAnalyticsSettings? Settings { get; set; }
}

internal sealed class CloudflareAnalyticsSettings
{
    [JsonPropertyName("httpRequestsAdaptiveGroups")]
    public CloudflareDatasetSettings? HttpRequestsAdaptiveGroups { get; set; }
}

internal sealed class CloudflareDatasetSettings
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    [JsonPropertyName("maxPageSize")]
    public int? MaxPageSize { get; set; }

    [JsonPropertyName("maxDuration")]
    public int? MaxDuration { get; set; }

    [JsonPropertyName("notOlderThan")]
    public int? NotOlderThan { get; set; }
}

internal sealed class CloudflareTrafficData
{
    [JsonPropertyName("viewer")]
    public CloudflareTrafficViewer? Viewer { get; set; }
}

internal sealed class CloudflareTrafficViewer
{
    [JsonPropertyName("zones")]
    public CloudflareTrafficZone[] Zones { get; set; } = Array.Empty<CloudflareTrafficZone>();
}

internal sealed class CloudflareTrafficZone
{
    [JsonPropertyName("traffic")]
    public CloudflareTrafficGroup[]? Traffic { get; set; }
}

internal sealed class CloudflareTrafficGroup
{
    [JsonPropertyName("count")]
    public ulong? Count { get; set; }

    [JsonPropertyName("avg")]
    public CloudflareTrafficAverage? Average { get; set; }

    [JsonPropertyName("sum")]
    public CloudflareTrafficSum? Sum { get; set; }

    [JsonPropertyName("dimensions")]
    public CloudflareTrafficDimensions? Dimensions { get; set; }
}

internal sealed class CloudflareTrafficAverage
{
    [JsonPropertyName("sampleInterval")]
    public double? SampleInterval { get; set; }
}

internal sealed class CloudflareTrafficSum
{
    [JsonPropertyName("visits")]
    public ulong? Visits { get; set; }

    [JsonPropertyName("edgeResponseBytes")]
    public ulong? EdgeResponseBytes { get; set; }
}

internal sealed class CloudflareTrafficDimensions
{
    [JsonPropertyName("date")]
    public DateOnly? Date { get; set; }

    [JsonPropertyName("clientRequestHTTPHost")]
    public string? Host { get; set; }

    [JsonPropertyName("clientRequestPath")]
    public string? Path { get; set; }
}
