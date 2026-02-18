namespace PowerForge.Web;

/// <summary>Result payload for SEO doctor checks.</summary>
public sealed class WebSeoDoctorResult
{
    /// <summary>Overall doctor status.</summary>
    public bool Success { get; set; }
    /// <summary>Total HTML files discovered under site root.</summary>
    public int HtmlFileCount { get; set; }
    /// <summary>Total HTML files selected for checks (after include/exclude/max filters).</summary>
    public int HtmlSelectedFileCount { get; set; }
    /// <summary>Total pages scanned.</summary>
    public int PageCount { get; set; }
    /// <summary>Total orphan page candidates detected.</summary>
    public int OrphanPageCount { get; set; }
    /// <summary>Total issues.</summary>
    public int IssueCount { get; set; }
    /// <summary>Total errors emitted by doctor.</summary>
    public int ErrorCount { get; set; }
    /// <summary>Total warnings emitted by doctor.</summary>
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
    /// <summary>Optional JSON report path written by pipeline task.</summary>
    public string? ReportPath { get; set; }
    /// <summary>Optional markdown summary path written by pipeline task.</summary>
    public string? SummaryPath { get; set; }
    /// <summary>Structured doctor issues.</summary>
    public WebSeoDoctorIssue[] Issues { get; set; } = Array.Empty<WebSeoDoctorIssue>();
    /// <summary>Doctor errors.</summary>
    public string[] Errors { get; set; } = Array.Empty<string>();
    /// <summary>Doctor warnings.</summary>
    public string[] Warnings { get; set; } = Array.Empty<string>();
}

