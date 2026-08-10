using System.Text.Json.Serialization;

namespace PowerForge.Web;

/// <summary>Fleet-level provider configuration for Search Intelligence collection.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class WebSearchProviderConfiguration
{
    /// <summary>Current configuration schema version.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Configuration schema version.</summary>
    [JsonPropertyName("schemaVersion"), JsonRequired]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Sites governed by this configuration.</summary>
    [JsonPropertyName("sites"), JsonRequired]
    public WebSearchSiteProviderConfiguration[] Sites { get; set; } = Array.Empty<WebSearchSiteProviderConfiguration>();

    /// <summary>Optional fleet scheduling, backfill, and retention policy.</summary>
    [JsonPropertyName("operations")]
    public WebSearchFleetOperationsConfiguration? Operations { get; set; }
}

/// <summary>Portable policy used by fleet schedulers and retention jobs.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class WebSearchFleetOperationsConfiguration
{
    /// <summary>Oldest reporting date that automatic backfill may request.</summary>
    [JsonPropertyName("backfillStartDate")]
    public DateOnly? BackfillStartDate { get; set; }

    /// <summary>Maximum consecutive daily partitions emitted in one work item.</summary>
    [JsonPropertyName("maxBackfillDaysPerRun")]
    public int MaxBackfillDaysPerRun { get; set; } = 31;

    /// <summary>Delay behind the current UTC date before search data is treated as final.</summary>
    [JsonPropertyName("searchDataLagDays")]
    public int SearchDataLagDays { get; set; } = 3;

    /// <summary>Delay behind the current UTC date before traffic data is collected.</summary>
    [JsonPropertyName("trafficDataLagDays")]
    public int TrafficDataLagDays { get; set; } = 1;

    /// <summary>Minimum age of complete CrUX evidence before another collection is due.</summary>
    [JsonPropertyName("cruxIntervalDays")]
    public int CruxIntervalDays { get; set; } = 7;

    /// <summary>Minimum age of complete Lighthouse evidence before another capture is due.</summary>
    [JsonPropertyName("lighthouseIntervalDays")]
    public int LighthouseIntervalDays { get; set; } = 7;

    /// <summary>Retention window for collected search runs, based on collection time.</summary>
    [JsonPropertyName("searchRunRetentionDays")]
    public int SearchRunRetentionDays { get; set; } = 730;

    /// <summary>Retention window for collected traffic runs, based on collection time.</summary>
    [JsonPropertyName("trafficRunRetentionDays")]
    public int TrafficRunRetentionDays { get; set; } = 400;

    /// <summary>Retention window for collected performance runs, based on collection time.</summary>
    [JsonPropertyName("performanceRunRetentionDays")]
    public int PerformanceRunRetentionDays { get; set; } = 400;
}

/// <summary>Provider registrations for one fleet site.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class WebSearchSiteProviderConfiguration
{
    /// <summary>Stable fleet site identifier.</summary>
    [JsonPropertyName("id"), JsonRequired]
    public string Id { get; set; } = string.Empty;

    /// <summary>Canonical public HTTP(S) base URL for the site without user info, query or fragment.</summary>
    [JsonPropertyName("baseUrl"), JsonRequired]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Providers configured for the site.</summary>
    [JsonPropertyName("providers"), JsonRequired]
    public WebSearchProviderRegistration[] Providers { get; set; } = Array.Empty<WebSearchProviderRegistration>();
}

/// <summary>One provider identity, capability request and non-secret setting set.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class WebSearchProviderRegistration
{
    /// <summary>Stable provider registration identifier within the site.</summary>
    [JsonPropertyName("id"), JsonRequired]
    public string Id { get; set; } = string.Empty;

    /// <summary>Known adapter kind such as google-search-console or cloudflare-analytics.</summary>
    [JsonPropertyName("kind"), JsonRequired]
    public string Kind { get; set; } = string.Empty;

    /// <summary>Whether scheduled collection may use this registration.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Capabilities that the registration is expected to provide.</summary>
    [JsonPropertyName("capabilities"), JsonRequired]
    public string[] Capabilities { get; set; } = Array.Empty<string>();

    /// <summary>Optional environment-backed credential reference. Secret values never belong in this document.</summary>
    [JsonPropertyName("credential")]
    public WebSearchCredentialReference? Credential { get; set; }

    /// <summary>Provider-specific non-secret settings.</summary>
    [JsonPropertyName("settings")]
    public Dictionary<string, string?> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Reference to a credential supplied through the process environment.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class WebSearchCredentialReference
{
    /// <summary>Credential shape expected by the adapter.</summary>
    [JsonPropertyName("kind"), JsonRequired]
    public string Kind { get; set; } = string.Empty;

    /// <summary>Name of the environment variable that supplies the credential or credential-file path.</summary>
    [JsonPropertyName("environmentVariable"), JsonRequired]
    public string EnvironmentVariable { get; set; } = string.Empty;
}

/// <summary>Stable provider capability identifiers.</summary>
public static class WebSearchProviderCapabilities
{
    /// <summary>Daily first-party search analytics.</summary>
    public const string SearchAnalytics = "search.analytics";

    /// <summary>Search-engine sitemap management or inspection.</summary>
    public const string SearchSitemaps = "search.sitemaps";

    /// <summary>Search-engine URL inspection.</summary>
    public const string SearchUrlInspection = "search.url-inspection";

    /// <summary>First-party traffic analytics kept separate from search observations.</summary>
    public const string TrafficAnalytics = "traffic.analytics";

    /// <summary>Lighthouse laboratory performance measurement.</summary>
    public const string PerformanceLighthouse = "performance.lighthouse";

    /// <summary>Chrome UX Report field performance measurement.</summary>
    public const string PerformanceCrux = "performance.crux";
}
