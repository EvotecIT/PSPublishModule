using System.Text.Json.Serialization;

namespace PowerForge.Web;

/// <summary>Options for a bounded Cloudflare operational pulse.</summary>
public sealed class CloudflareOperationalCollectionOptions
{
    /// <summary>Stable fleet site identifier.</summary>
    public string SiteId { get; set; } = string.Empty;
    /// <summary>Owning Cloudflare zone identifier.</summary>
    public string ZoneId { get; set; } = string.Empty;
    /// <summary>Owning site URL used to constrain HTTP and WAF observations.</summary>
    public string SiteBaseUrl { get; set; } = string.Empty;
    /// <summary>Inclusive UTC beginning of the operational window.</summary>
    public DateTimeOffset FromUtc { get; set; }
    /// <summary>Exclusive UTC end of the operational window.</summary>
    public DateTimeOffset ThroughUtc { get; set; }
    /// <summary>Optional Cloudflare account identifier used to inspect RUM configuration.</summary>
    public string? AccountId { get; set; }
}

/// <summary>One normalized hourly Cloudflare operational observation.</summary>
public sealed class CloudflareHourlyOperationalObservation
{
    /// <summary>Beginning of the UTC hour.</summary>
    public DateTimeOffset HourUtc { get; set; }
    /// <summary>Estimated end-user request count.</summary>
    public long Requests { get; set; }
    /// <summary>Estimated cache-hit request count.</summary>
    public long CachedRequests { get; set; }
    /// <summary>Estimated 4xx response count.</summary>
    public long ClientErrors { get; set; }
    /// <summary>Estimated 5xx response count.</summary>
    public long ServerErrors { get; set; }
    /// <summary>Estimated edge response bytes.</summary>
    public long EdgeResponseBytes { get; set; }
    /// <summary>Estimated WAF/security event count.</summary>
    public long FirewallEvents { get; set; }
    /// <summary>Estimated blocked/challenged WAF event count.</summary>
    public long FirewallMitigated { get; set; }
    /// <summary>Largest adaptive sample interval contributing to the hour.</summary>
    public double MaximumSampleInterval { get; set; } = 1d;
    /// <summary>Cache-hit percentage when request evidence is available.</summary>
    public double CacheHitPercent => Requests <= 0 ? 0d : CachedRequests * 100d / Requests;
}

/// <summary>Cloudflare Web Analytics/RUM installation state for the owning zone.</summary>
public sealed class CloudflareRumSiteState
{
    /// <summary>Whether account-scoped RUM inspection was requested.</summary>
    public bool Requested { get; set; }
    /// <summary>Whether a matching Web Analytics site was found.</summary>
    public bool Configured { get; set; }
    /// <summary>Whether the matching ruleset is enabled.</summary>
    public bool Enabled { get; set; }
    /// <summary>Whether Cloudflare automatically injects the RUM beacon.</summary>
    public bool AutoInstall { get; set; }
    /// <summary>Stable failure category when inspection was unavailable.</summary>
    public string? ErrorCode { get; set; }
    /// <summary>Sanitized failure description.</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>Independent capability state within a Cloudflare operational pulse.</summary>
public sealed class CloudflareOperationalCapabilityState
{
    /// <summary>Whether the capability returned complete evidence.</summary>
    public bool Success { get; set; }
    /// <summary>Stable failure category.</summary>
    public string? ErrorCode { get; set; }
    /// <summary>Sanitized failure description.</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>Cloudflare operational pulse with independently degradable datasets.</summary>
public sealed class CloudflareOperationalCollectionResult
{
    /// <summary>Whether zone traffic completed. Optional WAF/RUM gaps do not erase traffic evidence.</summary>
    public bool Success { get; set; }
    /// <summary>Capture time.</summary>
    public DateTimeOffset CollectedAtUtc { get; set; }
    /// <summary>Number of provider requests attempted.</summary>
    public int RequestCount { get; set; }
    /// <summary>HTTP/cache/status dataset state.</summary>
    public CloudflareOperationalCapabilityState Http { get; set; } = new();
    /// <summary>WAF/security dataset state.</summary>
    public CloudflareOperationalCapabilityState Firewall { get; set; } = new();
    /// <summary>RUM configuration state.</summary>
    public CloudflareRumSiteState Rum { get; set; } = new();
    /// <summary>Normalized hourly observations.</summary>
    public CloudflareHourlyOperationalObservation[] Hours { get; set; } = Array.Empty<CloudflareHourlyOperationalObservation>();
}

internal sealed class CloudflareOperationalData { [JsonPropertyName("viewer")] public CloudflareOperationalViewer? Viewer { get; set; } }
internal sealed class CloudflareOperationalViewer { [JsonPropertyName("zones")] public CloudflareOperationalZone?[] Zones { get; set; } = Array.Empty<CloudflareOperationalZone?>(); }
internal sealed class CloudflareOperationalZone
{
    [JsonPropertyName("http")] public CloudflareHttpOperationalGroup?[]? Http { get; set; }
    [JsonPropertyName("firewall")] public CloudflareFirewallOperationalGroup?[]? Firewall { get; set; }
}
internal sealed class CloudflareHttpOperationalGroup
{
    [JsonPropertyName("count")] public ulong? Count { get; set; }
    [JsonPropertyName("avg")] public CloudflareTrafficAverage? Average { get; set; }
    [JsonPropertyName("sum")] public CloudflareTrafficSum? Sum { get; set; }
    [JsonPropertyName("dimensions")] public CloudflareHttpOperationalDimensions? Dimensions { get; set; }
}
internal sealed class CloudflareHttpOperationalDimensions
{
    [JsonPropertyName("datetimeHour")] public DateTimeOffset? HourUtc { get; set; }
    [JsonPropertyName("cacheStatus")] public string? CacheStatus { get; set; }
    [JsonPropertyName("edgeResponseStatus")] public int? EdgeResponseStatus { get; set; }
}
internal sealed class CloudflareFirewallOperationalGroup
{
    [JsonPropertyName("count")] public ulong? Count { get; set; }
    [JsonPropertyName("avg")] public CloudflareTrafficAverage? Average { get; set; }
    [JsonPropertyName("dimensions")] public CloudflareFirewallOperationalDimensions? Dimensions { get; set; }
}
internal sealed class CloudflareFirewallOperationalDimensions
{
    [JsonPropertyName("datetimeHour")] public DateTimeOffset? HourUtc { get; set; }
    [JsonPropertyName("action")] public string? Action { get; set; }
}
internal sealed class CloudflareRumSitesEnvelope
{
    [JsonPropertyName("success")] public bool? Success { get; set; }
    [JsonPropertyName("result")] public CloudflareRumSiteInfo[] Result { get; set; } = Array.Empty<CloudflareRumSiteInfo>();
    [JsonPropertyName("errors")] public CloudflareApiError[] Errors { get; set; } = Array.Empty<CloudflareApiError>();
    [JsonPropertyName("result_info")] public CloudflareRumResultInfo? ResultInfo { get; set; }
}
internal sealed class CloudflareRumResultInfo
{
    [JsonPropertyName("total_pages")] public int? TotalPages { get; set; }
}
internal sealed class CloudflareRumSiteInfo
{
    [JsonPropertyName("auto_install")] public bool? AutoInstall { get; set; }
    [JsonPropertyName("ruleset")] public CloudflareRumRuleset? Ruleset { get; set; }
}
internal sealed class CloudflareRumRuleset
{
    [JsonPropertyName("enabled")] public bool? Enabled { get; set; }
    [JsonPropertyName("zone_tag")] public string? ZoneTag { get; set; }
}
