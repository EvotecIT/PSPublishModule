namespace PowerForge.Web;

/// <summary>Opt-in network validation of links to explicitly owned domains.</summary>
public sealed class WebOwnedLinkAuditOptions
{
    /// <summary>Enables network checks. Disabled by default, including when domains are listed.</summary>
    public bool Enabled { get; set; }
    /// <summary>Exact hostnames (optionally including a port); subdomains require separate entries.</summary>
    public WebOwnedDomain[] Domains { get; set; } = Array.Empty<WebOwnedDomain>();
    /// <summary>Severity for confirmed HTTP 404/410 responses: warning or error. Unknown results are warnings.</summary>
    public string Severity { get; set; } = "warning";
    /// <summary>Timeout for each request, including reading a sitemap body.</summary>
    public int TimeoutSeconds { get; set; } = 10;
    /// <summary>Total network time budget for the audit.</summary>
    public int MaxDurationSeconds { get; set; } = 60;
    /// <summary>Maximum decompressed bytes per sitemap document.</summary>
    public int MaxSitemapBytes { get; set; } = 5 * 1024 * 1024;
    /// <summary>Maximum sitemap documents fetched across all domains, including indexes.</summary>
    public int MaxSitemaps { get; set; } = 20;
    /// <summary>Maximum distinct URLs checked over HTTP when absent from sitemaps.</summary>
    public int MaxLinkChecks { get; set; } = 100;
}

/// <summary>An owned domain whose sitemap and linked resources may be requested by an audit.</summary>
public sealed class WebOwnedDomain
{
    /// <summary>Exact hostname, optionally with a port; for example evotec.xyz. No scheme or path.</summary>
    public string Domain { get; set; } = string.Empty;
    /// <summary>Optional HTTP(S) sitemap URL on this domain. Defaults to https://{Domain}/sitemap.xml.</summary>
    public string? SitemapUrl { get; set; }
}
