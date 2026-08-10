namespace PowerForge.Web;

/// <summary>Options for one CrUX field-data query.</summary>
public sealed class CruxCollectionOptions
{
    /// <summary>Provider registration identifier.</summary>
    public string ProviderId { get; set; } = string.Empty;
    /// <summary>Fleet site identifier.</summary>
    public string SiteId { get; set; } = string.Empty;
    /// <summary>Configured fleet site base URL used for ownership validation.</summary>
    public string SiteBaseUrl { get; set; } = string.Empty;
    /// <summary>Requested scope: URL or origin.</summary>
    public string TargetKind { get; set; } = "origin";
    /// <summary>Requested HTTP(S) URL or origin.</summary>
    public string TargetUrl { get; set; } = string.Empty;
    /// <summary>All, phone, desktop, or tablet.</summary>
    public string FormFactor { get; set; } = "all";
    /// <summary>Optional non-secret configuration fingerprint.</summary>
    public string? ConfigurationHash { get; set; }
    /// <summary>Optional non-secret raw-evidence reference.</summary>
    public string? EvidenceReference { get; set; }
}

/// <summary>CrUX request outcome and normalized field evidence.</summary>
public sealed class CruxCollectionResult
{
    /// <summary>Whether a complete record or explicit no-data response was obtained.</summary>
    public bool Success { get; set; }
    /// <summary>Number of provider requests issued.</summary>
    public int RequestCount { get; set; }
    /// <summary>Stable failure category.</summary>
    public string? ErrorCode { get; set; }
    /// <summary>Sanitized failure detail.</summary>
    public string? ErrorMessage { get; set; }
    /// <summary>Complete or partial provider-neutral evidence batch.</summary>
    public WebPerformanceObservationBatch Batch { get; set; } = new();
}
