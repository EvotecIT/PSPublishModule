namespace PowerForge.Web;

/// <summary>Per-section navigation audit profile.</summary>
public sealed class WebAuditNavProfile
{
    /// <summary>Glob match pattern for site-relative HTML path (for example "api/**").</summary>
    public string Match { get; set; } = string.Empty;
    /// <summary>Optional CSS selector used to locate nav for matching pages.</summary>
    public string? Selector { get; set; }
    /// <summary>Optional override for whether nav is required on matching pages.</summary>
    public bool? Required { get; set; }
    /// <summary>Optional additional required links for matching pages.</summary>
    public string[] RequiredLinks { get; set; } = Array.Empty<string>();
    /// <summary>When true, nav checks are skipped for matching pages.</summary>
    public bool Ignore { get; set; }
}

