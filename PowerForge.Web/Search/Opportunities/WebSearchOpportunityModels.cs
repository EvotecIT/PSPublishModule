namespace PowerForge.Web;

/// <summary>Thresholds and filters used by the deterministic search opportunity analyzer.</summary>
public sealed class WebSearchOpportunityOptions
{
    /// <summary>Required fleet site identifier.</summary>
    public string SiteId { get; set; } = string.Empty;

    /// <summary>Optional provider identifier.</summary>
    public string? Provider { get; set; }

    /// <summary>Optional inclusive observation start date.</summary>
    public DateOnly? FromDate { get; set; }

    /// <summary>Optional inclusive observation end date.</summary>
    public DateOnly? ThroughDate { get; set; }

    /// <summary>Minimum impressions required for an opportunity. Defaults to 100.</summary>
    public long MinimumImpressions { get; set; } = 100;

    /// <summary>Best position included by the weak-page rule. Defaults to 8.</summary>
    public double WeakPageMinimumPosition { get; set; } = 8d;

    /// <summary>Worst position included by the weak-page rule. Defaults to 20.</summary>
    public double WeakPageMaximumPosition { get; set; } = 20d;

    /// <summary>Worst position included by the CTR rule. Defaults to 10.</summary>
    public double CtrMaximumPosition { get; set; } = 10d;

    /// <summary>CTR below which an otherwise visible page is considered underperforming. Defaults to 0.02.</summary>
    public double MinimumClickThroughRate { get; set; } = 0.02d;
}

/// <summary>An explainable opportunity derived from immutable search observations.</summary>
public sealed class WebSearchOpportunity
{
    /// <summary>Deterministic opportunity identifier.</summary>
    public string OpportunityId { get; set; } = string.Empty;

    /// <summary>Stable rule identifier.</summary>
    public string RuleId { get; set; } = string.Empty;

    /// <summary>Rule implementation version.</summary>
    public int RuleVersion { get; set; } = 1;

    /// <summary>Provider identifier.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Fleet site identifier.</summary>
    public string SiteId { get; set; } = string.Empty;

    /// <summary>Canonical page URL.</summary>
    public string Page { get; set; } = string.Empty;

    /// <summary>Search query when the opportunity is query-specific.</summary>
    public string? Query { get; set; }

    /// <summary>Country dimension when present.</summary>
    public string? Country { get; set; }

    /// <summary>Device dimension when present.</summary>
    public string? Device { get; set; }

    /// <summary>Search type or surface dimension when present.</summary>
    public string? SearchType { get; set; }

    /// <summary>First observation date included in the evidence window.</summary>
    public DateOnly FromDate { get; set; }

    /// <summary>Last observation date included in the evidence window.</summary>
    public DateOnly ThroughDate { get; set; }

    /// <summary>Total clicks in the evidence window.</summary>
    public long Clicks { get; set; }

    /// <summary>Total impressions in the evidence window.</summary>
    public long Impressions { get; set; }

    /// <summary>Click-through rate calculated from total clicks and impressions.</summary>
    public double ClickThroughRate { get; set; }

    /// <summary>Impression-weighted average position.</summary>
    public double AveragePosition { get; set; }

    /// <summary>Deterministic priority score from zero to 100.</summary>
    public double Score { get; set; }

    /// <summary>Evidence confidence from zero to one based on volume and distinct observed-day coverage.</summary>
    public double Confidence { get; set; }

    /// <summary>Human-readable explanation naming the evidence and threshold that triggered the rule.</summary>
    public string Explanation { get; set; } = string.Empty;

    /// <summary>Suggested next investigation, not a guaranteed ranking outcome.</summary>
    public string Recommendation { get; set; } = string.Empty;

    /// <summary>Observation identities that support this opportunity.</summary>
    public string[] EvidenceObservationKeys { get; set; } = Array.Empty<string>();
}

/// <summary>A deterministic report over a bounded set of search observations.</summary>
public sealed class WebSearchOpportunityReport
{
    /// <summary>Report schema version.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Time at which the caller generated the report.</summary>
    public DateTimeOffset GeneratedAtUtc { get; set; }

    /// <summary>Fleet site identifier.</summary>
    public string SiteId { get; set; } = string.Empty;

    /// <summary>Optional provider filter.</summary>
    public string? Provider { get; set; }

    /// <summary>Number of normalized observations considered.</summary>
    public int ObservationCount { get; set; }

    /// <summary>First date represented by the considered observations.</summary>
    public DateOnly? FromDate { get; set; }

    /// <summary>Last date represented by the considered observations.</summary>
    public DateOnly? ThroughDate { get; set; }

    /// <summary>Explainable opportunities ordered by descending score.</summary>
    public WebSearchOpportunity[] Opportunities { get; set; } = Array.Empty<WebSearchOpportunity>();
}
