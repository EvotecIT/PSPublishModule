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
    /// <summary>Total pages missing meta descriptions.</summary>
    public int PagesMissingDescription { get; set; }
    /// <summary>Total pages with short meta descriptions.</summary>
    public int PagesWithShortDescription { get; set; }
    /// <summary>Total pages with long meta descriptions.</summary>
    public int PagesWithLongDescription { get; set; }
    /// <summary>Total pages missing visible h1 headings.</summary>
    public int PagesMissingH1 { get; set; }
    /// <summary>Total pages with multiple visible h1 headings.</summary>
    public int PagesWithMultipleH1 { get; set; }
    /// <summary>Total pages with visible images missing alt attributes.</summary>
    public int PagesWithMissingAlt { get; set; }
    /// <summary>Total pages with visible images that have empty alt text.</summary>
    public int PagesWithEmptyAlt { get; set; }
    /// <summary>Total visible images missing alt attributes.</summary>
    public int TotalMissingAlt { get; set; }
    /// <summary>Total visible images that have empty alt text.</summary>
    public int TotalEmptyAlt { get; set; }
    /// <summary>Total source Markdown files containing empty image alt text.</summary>
    public int SourceMarkdownFilesWithEmptyAlt { get; set; }
    /// <summary>Total source Markdown empty image alt occurrences.</summary>
    public int TotalSourceMarkdownEmptyAlt { get; set; }
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
    /// <summary>Per-page backlog metrics.</summary>
    public WebSeoDoctorPageMetric[] PageMetrics { get; set; } = Array.Empty<WebSeoDoctorPageMetric>();
    /// <summary>Source Markdown empty-alt backlog metrics.</summary>
    public WebSeoDoctorSourceMarkdownMetric[] SourceMarkdownMetrics { get; set; } = Array.Empty<WebSeoDoctorSourceMarkdownMetric>();
    /// <summary>Doctor errors.</summary>
    public string[] Errors { get; set; } = Array.Empty<string>();
    /// <summary>Doctor warnings.</summary>
    public string[] Warnings { get; set; } = Array.Empty<string>();
}
