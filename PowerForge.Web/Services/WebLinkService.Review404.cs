using System;
using System.IO;
using System.Text.Json;

namespace PowerForge.Web;

public static partial class WebLinkService
{
    /// <summary>Runs the static 404 review workflow and writes review artifacts.</summary>
    public static WebLink404ReviewResult Review404(WebLink404ReviewOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.SiteRoot))
            throw new ArgumentException("SiteRoot is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.ReportPath))
            throw new ArgumentException("ReportPath is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.RedirectCandidatesPath))
            throw new ArgumentException("RedirectCandidatesPath is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.Ignored404CandidatesPath))
            throw new ArgumentException("Ignored404CandidatesPath is required.", nameof(options));

        var reportPath = Path.GetFullPath(options.ReportPath);
        var redirectCandidatesPath = Path.GetFullPath(options.RedirectCandidatesPath);
        var ignored404CandidatesPath = Path.GetFullPath(options.Ignored404CandidatesPath);

        var report = Generate404Report(new WebLink404ReportOptions
        {
            SiteRoot = options.SiteRoot,
            SourcePath = options.SourcePath,
            Ignored404Path = options.Ignored404Path,
            AllowMissingSource = options.AllowMissingSource,
            MaxSuggestions = options.MaxSuggestions,
            MinimumScore = options.MinimumScore,
            IncludeAsset404s = options.IncludeAsset404s
        });
        Write404ReportJson(reportPath, report);

        var promote = Promote404Suggestions(new WebLink404PromoteOptions
        {
            SourcePath = reportPath,
            OutputPath = redirectCandidatesPath,
            Enabled = options.EnableRedirectCandidates,
            MinimumScore = options.PromoteMinimumScore,
            MinimumCount = options.PromoteMinimumCount,
            Status = options.PromoteStatus,
            Group = options.PromoteGroup,
            MergeWithExisting = false,
            ReplaceExisting = false
        });

        var ignored = Ignore404Suggestions(new WebLink404IgnoreOptions
        {
            SourcePath = reportPath,
            OutputPath = ignored404CandidatesPath,
            OnlyWithoutSuggestions = true,
            Reason = string.IsNullOrWhiteSpace(options.IgnoreReason)
                ? "No generated route suggestion in 404 report."
                : options.IgnoreReason,
            CreatedBy = options.CreatedBy,
            MergeWithExisting = false,
            ReplaceExisting = false
        });

        WriteJsonIfRequested(options.PromoteSummaryPath, promote);
        WriteJsonIfRequested(options.IgnoreSummaryPath, ignored);

        return new WebLink404ReviewResult
        {
            ReportPath = reportPath,
            RedirectCandidatesPath = redirectCandidatesPath,
            Ignored404CandidatesPath = ignored404CandidatesPath,
            Report = report,
            Promote = promote,
            Ignore = ignored
        };
    }

    private static void Write404ReportJson(string outputPath, WebLink404ReportResult report)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, ShortlinkImportJsonOptions), Utf8NoBom);
    }

    private static void WriteJsonIfRequested<T>(string? outputPath, T payload)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            return;

        var resolved = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(resolved);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(resolved, JsonSerializer.Serialize(payload, ShortlinkImportJsonOptions), Utf8NoBom);
    }
}

/// <summary>Options for the static 404 review workflow.</summary>
public sealed class WebLink404ReviewOptions
{
    /// <summary>Generated site root used for route discovery.</summary>
    public string SiteRoot { get; set; } = string.Empty;
    /// <summary>Apache log or CSV observation source.</summary>
    public string? SourcePath { get; set; }
    /// <summary>Committed ignored-404 rules used to filter observations.</summary>
    public string? Ignored404Path { get; set; }
    /// <summary>Allow missing log source and write empty artifacts.</summary>
    public bool AllowMissingSource { get; set; }
    /// <summary>Include asset 404s in the report.</summary>
    public bool IncludeAsset404s { get; set; }
    /// <summary>Maximum route suggestions per missing path.</summary>
    public int MaxSuggestions { get; set; } = 3;
    /// <summary>Minimum route suggestion score to include in the report.</summary>
    public double MinimumScore { get; set; } = 0.35d;
    /// <summary>Output 404 suggestion report JSON path.</summary>
    public string ReportPath { get; set; } = string.Empty;
    /// <summary>Output redirect candidate JSON path.</summary>
    public string RedirectCandidatesPath { get; set; } = string.Empty;
    /// <summary>Output ignored-404 candidate JSON path.</summary>
    public string Ignored404CandidatesPath { get; set; } = string.Empty;
    /// <summary>Optional redirect candidate summary path.</summary>
    public string? PromoteSummaryPath { get; set; }
    /// <summary>Optional ignored-404 candidate summary path.</summary>
    public string? IgnoreSummaryPath { get; set; }
    /// <summary>Enable promoted redirect candidates immediately.</summary>
    public bool EnableRedirectCandidates { get; set; }
    /// <summary>Minimum suggestion score for redirect candidates.</summary>
    public double PromoteMinimumScore { get; set; } = 0.65d;
    /// <summary>Minimum 404 count for redirect candidates.</summary>
    public int PromoteMinimumCount { get; set; } = 1;
    /// <summary>HTTP status for promoted redirect candidates.</summary>
    public int PromoteStatus { get; set; } = 301;
    /// <summary>Group assigned to promoted redirect candidates.</summary>
    public string? PromoteGroup { get; set; } = "404-suggestions";
    /// <summary>Reason assigned to ignored-404 candidates.</summary>
    public string? IgnoreReason { get; set; } = "No generated route suggestion in 404 report.";
    /// <summary>Creator identifier for ignored-404 candidates.</summary>
    public string? CreatedBy { get; set; }
}

/// <summary>Result from the static 404 review workflow.</summary>
public sealed class WebLink404ReviewResult
{
    /// <summary>Resolved 404 suggestion report JSON path.</summary>
    public string ReportPath { get; set; } = string.Empty;
    /// <summary>Resolved redirect candidate JSON path.</summary>
    public string RedirectCandidatesPath { get; set; } = string.Empty;
    /// <summary>Resolved ignored-404 candidate JSON path.</summary>
    public string Ignored404CandidatesPath { get; set; } = string.Empty;
    /// <summary>Generated 404 suggestion report.</summary>
    public WebLink404ReportResult Report { get; set; } = new();
    /// <summary>Redirect candidate promotion summary.</summary>
    public WebLink404PromoteResult Promote { get; set; } = new();
    /// <summary>Ignored-404 candidate summary.</summary>
    public WebLink404IgnoreResult Ignore { get; set; } = new();
}
