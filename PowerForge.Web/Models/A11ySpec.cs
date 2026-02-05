namespace PowerForge.Web;

/// <summary>Accessibility labels and strings used by templates.</summary>
public sealed class A11ySpec
{
    /// <summary>Skip link label.</summary>
    public string? SkipLinkLabel { get; set; }
    /// <summary>Label for external links.</summary>
    public string? ExternalLinkLabel { get; set; }
    /// <summary>Label for the navigation toggle.</summary>
    public string? NavToggleLabel { get; set; }
    /// <summary>Label for search inputs.</summary>
    public string? SearchLabel { get; set; }
}

/// <summary>Rules applied to external links.</summary>
public sealed class LinkRulesSpec
{
    /// <summary>Rel attribute for external links.</summary>
    public string? ExternalRel { get; set; }
    /// <summary>Target attribute for external links.</summary>
    public string? ExternalTarget { get; set; }
    /// <summary>When true, indicates external links should show an icon.</summary>
    public bool AddExternalIcon { get; set; }
}

/// <summary>Analytics configuration for the site.</summary>
public sealed class AnalyticsSpec
{
    /// <summary>Enables analytics output.</summary>
    public bool Enabled { get; set; }
    /// <summary>Analytics provider.</summary>
    public AnalyticsProvider Provider { get; set; } = AnalyticsProvider.None;
    /// <summary>Collector endpoint for first‑party analytics.</summary>
    public string? Endpoint { get; set; }
    /// <summary>When true, respect Do‑Not‑Track headers.</summary>
    public bool RespectDnt { get; set; } = true;
    /// <summary>Sampling rate (0..1).</summary>
    public double SampleRate { get; set; } = 1.0;
}
