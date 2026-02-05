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
    /// <summary>Total duplicate IDs detected.</summary>
    public int DuplicateIdCount { get; set; }
    /// <summary>Total rendered pages checked.</summary>
    public int RenderedPageCount { get; set; }
    /// <summary>Total console errors detected during rendered checks.</summary>
    public int RenderedConsoleErrorCount { get; set; }
    /// <summary>Total console warnings detected during rendered checks.</summary>
    public int RenderedConsoleWarningCount { get; set; }
    /// <summary>Total failed network requests during rendered checks.</summary>
    public int RenderedFailedRequestCount { get; set; }
    /// <summary>Limited set of errors (if any).</summary>
    public string[] Errors { get; set; } = Array.Empty<string>();
    /// <summary>Limited set of warnings (if any).</summary>
    public string[] Warnings { get; set; } = Array.Empty<string>();
}
