namespace PowerForge.Web;

/// <summary>Options for comparing legacy sitemap URLs with a newly generated site map.</summary>
public sealed class WebSitemapMigrationOptions
{
    /// <summary>Legacy public URLs, typically loaded from one or more legacy sitemap files.</summary>
    public string[] LegacyUrls { get; set; } = Array.Empty<string>();
    /// <summary>New public URLs, typically loaded from the generated sitemap.</summary>
    public string[] NewUrls { get; set; } = Array.Empty<string>();
    /// <summary>Optional generated site root used to resolve routes not present in the sitemap.</summary>
    public string? NewSiteRoot { get; set; }
    /// <summary>When true, generate AMP aliases for resolved blog/category/tag canonical routes.</summary>
    public bool IncludeSyntheticAmpRedirects { get; set; } = true;
    /// <summary>When true, map each legacy origin's /amp/ listing root to /blog/ when that target exists.</summary>
    public bool IncludeAmpListingRoots { get; set; }
}

/// <summary>Result from sitemap migration comparison.</summary>
public sealed class WebSitemapMigrationResult
{
    /// <summary>UTC timestamp when the comparison result was generated.</summary>
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Number of distinct legacy URLs considered.</summary>
    public int LegacyUrlCount { get; set; }
    /// <summary>Number of distinct new URLs considered.</summary>
    public int NewUrlCount { get; set; }
    /// <summary>Number of legacy URLs missing from the new URL set.</summary>
    public int MissingLegacyCount { get; set; }
    /// <summary>Number of redirect rows safe to export.</summary>
    public int RedirectCount { get; set; }
    /// <summary>Number of rows that require manual review.</summary>
    public int ReviewCount { get; set; }
    /// <summary>All candidate mappings found for legacy URLs missing from the new sitemap.</summary>
    public WebSitemapMigrationCandidate[] Candidates { get; set; } = Array.Empty<WebSitemapMigrationCandidate>();
    /// <summary>Redirect rows safe to export to CSV or link-service inputs.</summary>
    public WebSitemapMigrationRedirectRow[] Redirects { get; set; } = Array.Empty<WebSitemapMigrationRedirectRow>();
    /// <summary>Rows that require manual migration review.</summary>
    public WebSitemapMigrationReviewRow[] Reviews { get; set; } = Array.Empty<WebSitemapMigrationReviewRow>();
}

/// <summary>Candidate mapping between a legacy URL and a new target.</summary>
public sealed class WebSitemapMigrationCandidate
{
    /// <summary>Original legacy URL.</summary>
    public string LegacyUrl { get; set; } = string.Empty;
    /// <summary>Resolved target URL, when one could be found.</summary>
    public string TargetUrl { get; set; } = string.Empty;
    /// <summary>Machine-readable reason for the selected mapping.</summary>
    public string MatchKind { get; set; } = string.Empty;
    /// <summary>Whether a maintainer should review this candidate before export.</summary>
    public bool NeedsReview { get; set; }
    /// <summary>Human-readable explanation for the match.</summary>
    public string Notes { get; set; } = string.Empty;
}

/// <summary>Apache/link-service compatible redirect export row.</summary>
public sealed class WebSitemapMigrationRedirectRow
{
    /// <summary>Legacy source URL.</summary>
    public string LegacyUrl { get; set; } = string.Empty;
    /// <summary>Target URL for the redirect.</summary>
    public string TargetUrl { get; set; } = string.Empty;
    /// <summary>Redirect status code.</summary>
    public int Status { get; set; } = 301;
    /// <summary>Machine-readable reason for the redirect.</summary>
    public string MatchKind { get; set; } = string.Empty;
    /// <summary>Human-readable explanation for the redirect.</summary>
    public string Notes { get; set; } = string.Empty;
}

/// <summary>Redirect review row for legacy URLs that could not be mapped safely.</summary>
public sealed class WebSitemapMigrationReviewRow
{
    /// <summary>Legacy source URL needing review.</summary>
    public string LegacyUrl { get; set; } = string.Empty;
    /// <summary>Candidate target URL, when one exists.</summary>
    public string TargetUrl { get; set; } = string.Empty;
    /// <summary>Machine-readable reason for review.</summary>
    public string MatchKind { get; set; } = string.Empty;
    /// <summary>Human-readable explanation for review.</summary>
    public string Notes { get; set; } = string.Empty;
}
