namespace PowerForge.Web;

/// <summary>Compact audit summary payload written to disk.</summary>
public sealed class WebAuditSummary
{
    /// <summary>Audit success flag.</summary>
    public bool Success { get; set; }
    /// <summary>UTC timestamp for summary generation.</summary>
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Total HTML pages scanned.</summary>
    public int PageCount { get; set; }
    /// <summary>Total files discovered under site root (all extensions).</summary>
    public int TotalFileCount { get; set; }
    /// <summary>Total internal links checked.</summary>
    public int LinkCount { get; set; }
    /// <summary>Total broken internal links.</summary>
    public int BrokenLinkCount { get; set; }
    /// <summary>Total local assets checked.</summary>
    public int AssetCount { get; set; }
    /// <summary>Total missing local assets.</summary>
    public int MissingAssetCount { get; set; }
    /// <summary>Total nav mismatches detected.</summary>
    public int NavMismatchCount { get; set; }
    /// <summary>Total pages where nav was checked.</summary>
    public int NavCheckedCount { get; set; }
    /// <summary>Total pages skipped from nav checks by ignore patterns.</summary>
    public int NavIgnoredCount { get; set; }
    /// <summary>Total pages considered for nav checks (checked + ignored).</summary>
    public int NavTotalCount { get; set; }
    /// <summary>Percentage of nav-covered pages among nav-considered pages.</summary>
    public double NavCoveragePercent { get; set; }
    /// <summary>Total duplicate IDs detected.</summary>
    public int DuplicateIdCount { get; set; }
    /// <summary>Total required routes requested by audit options.</summary>
    public int RequiredRouteCount { get; set; }
    /// <summary>Total required routes missing from generated output.</summary>
    public int MissingRequiredRouteCount { get; set; }
    /// <summary>Total rendered pages checked.</summary>
    public int RenderedPageCount { get; set; }
    /// <summary>Total console errors detected during rendered checks.</summary>
    public int RenderedConsoleErrorCount { get; set; }
    /// <summary>Total console warnings detected during rendered checks.</summary>
    public int RenderedConsoleWarningCount { get; set; }
    /// <summary>Total failed network requests during rendered checks.</summary>
    public int RenderedFailedRequestCount { get; set; }
    /// <summary>Total errors emitted by audit.</summary>
    public int ErrorCount { get; set; }
    /// <summary>Total warnings emitted by audit.</summary>
    public int WarningCount { get; set; }
    /// <summary>Total issues considered new vs baseline.</summary>
    public int NewIssueCount { get; set; }
    /// <summary>Total new errors considered new vs baseline.</summary>
    public int NewErrorCount { get; set; }
    /// <summary>Total new warnings considered new vs baseline.</summary>
    public int NewWarningCount { get; set; }
    /// <summary>Resolved baseline path if baseline file was used.</summary>
    public string? BaselinePath { get; set; }
    /// <summary>Number of issue keys loaded from baseline.</summary>
    public int BaselineIssueCount { get; set; }
    /// <summary>Limited set of structured issues.</summary>
    public WebAuditIssue[] Issues { get; set; } = Array.Empty<WebAuditIssue>();
    /// <summary>Limited set of errors (if any).</summary>
    public string[] Errors { get; set; } = Array.Empty<string>();
    /// <summary>Limited set of warnings (if any).</summary>
    public string[] Warnings { get; set; } = Array.Empty<string>();
}
