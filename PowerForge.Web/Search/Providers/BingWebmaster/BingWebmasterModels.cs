using System.Text.Json.Serialization;

namespace PowerForge.Web;

/// <summary>Options for collecting daily Bing Webmaster search-performance observations.</summary>
public sealed class BingWebmasterCollectionOptions
{
    /// <summary>Stable provider registration identifier written to the observation batch.</summary>
    public string ProviderId { get; set; } = "bing-webmaster";

    /// <summary>Stable fleet site identifier written to the observation batch.</summary>
    public string SiteId { get; set; } = string.Empty;

    /// <summary>Exact verified Bing Webmaster site URL.</summary>
    public string SiteUrl { get; set; } = string.Empty;

    /// <summary>Owning fleet site boundary that the verified property must match exactly.</summary>
    public string SiteBaseUrl { get; set; } = string.Empty;

    /// <summary>Inclusive first reporting date.</summary>
    public DateOnly FromDate { get; set; }

    /// <summary>Inclusive last reporting date.</summary>
    public DateOnly ThroughDate { get; set; }

    /// <summary>Search surface attached to normalized observations.</summary>
    public string SearchType { get; set; } = "web";

    /// <summary>Optional deterministic provider configuration identity.</summary>
    public string? ConfigurationHash { get; set; }

    /// <summary>Optional non-secret reference to separately retained raw evidence.</summary>
    public string? EvidenceReference { get; set; }
}

/// <summary>Authenticated access state for a configured Bing Webmaster site.</summary>
public sealed class BingWebmasterSiteProbeResult
{
    /// <summary>Whether the credential can see the exact configured site and it is verified.</summary>
    public bool Success { get; set; }

    /// <summary>Configured Bing Webmaster site URL.</summary>
    public string SiteUrl { get; set; } = string.Empty;

    /// <summary>Whether Bing reports the matching site as verified.</summary>
    public bool Verified { get; set; }

    /// <summary>Capabilities implemented by this collector.</summary>
    public string[] AvailableCapabilities { get; set; } = Array.Empty<string>();

    /// <summary>Stable failure category when the probe does not succeed.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Credential-safe failure summary when the probe does not succeed.</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>Result of one Bing Webmaster collection run.</summary>
public sealed class BingWebmasterCollectionResult
{
    /// <summary>Whether the site probe and all requested API reads succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>Authenticated site probe.</summary>
    public BingWebmasterSiteProbeResult Probe { get; set; } = new();

    /// <summary>Complete or partial provider-neutral observation batch.</summary>
    public WebSearchObservationBatch Batch { get; set; } = new();

    /// <summary>Number of reporting dates covered by a complete run.</summary>
    public int CompletedDateCount { get; set; }

    /// <summary>Number of Bing API requests attempted.</summary>
    public int RequestCount { get; set; }

    /// <summary>Stable failure category for a partial run.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Credential-safe failure summary for a partial run.</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>Provides a Bing Webmaster API key without exposing it to configuration output.</summary>
public interface IBingWebmasterApiKeyProvider
{
    /// <summary>Gets the non-empty API key for a Bing request.</summary>
    Task<string> GetApiKeyAsync(CancellationToken cancellationToken = default);
}

internal sealed class BingWebmasterEnvelope<T>
{
    [JsonPropertyName("d")]
    public T[]? Values { get; set; }
}

internal sealed class BingWebmasterSite
{
    [JsonPropertyName("Url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("IsVerified")]
    public bool IsVerified { get; set; }
}

internal abstract class BingWebmasterSearchStat
{
    [JsonPropertyName("Date")]
    public string? Date { get; set; }

    [JsonPropertyName("Clicks")]
    public long? Clicks { get; set; }

    [JsonPropertyName("Impressions")]
    public long? Impressions { get; set; }

    [JsonPropertyName("AvgImpressionPosition")]
    public double? AverageImpressionPosition { get; set; }
}

internal sealed class BingWebmasterQueryStat : BingWebmasterSearchStat
{
    [JsonPropertyName("Query")]
    public string? Query { get; set; }
}

internal sealed class BingWebmasterPageStat : BingWebmasterSearchStat
{
    [JsonPropertyName("Page")]
    public string? Page { get; set; }

    [JsonPropertyName("Query")]
    public string? Query { get; set; }
}

internal sealed class BingWebmasterTrafficStat
{
    [JsonPropertyName("Date")]
    public string? Date { get; set; }

    [JsonPropertyName("Clicks")]
    public long? Clicks { get; set; }

    [JsonPropertyName("Impressions")]
    public long? Impressions { get; set; }
}

internal sealed class BingWebmasterError
{
    [JsonPropertyName("ErrorCode")]
    public int? ErrorCode { get; set; }

    [JsonPropertyName("Message")]
    public string? Message { get; set; }
}
