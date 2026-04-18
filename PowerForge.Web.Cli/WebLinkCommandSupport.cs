using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static class WebLinkCommandSupport
{
    internal static void WriteSummary(
        string? summaryPath,
        string action,
        WebLinkDataSet dataSet,
        LinkValidationResult validation,
        bool taskSuccess,
        WebLinkApacheExportResult? export,
        WebLinkBaselineState? baseline)
    {
        if (string.IsNullOrWhiteSpace(summaryPath))
            return;

        var summaryDirectory = Path.GetDirectoryName(summaryPath);
        if (!string.IsNullOrWhiteSpace(summaryDirectory))
            Directory.CreateDirectory(summaryDirectory);

        var summary = new WebLinkCommandSummary
        {
            GeneratedOn = DateTimeOffset.UtcNow.ToString("O"),
            Action = action,
            Redirects = validation.RedirectCount,
            Shortlinks = validation.ShortlinkCount,
            Errors = validation.ErrorCount,
            Warnings = validation.WarningCount,
            DuplicateWarnings = validation.Issues.Count(static issue => issue.Code == "PFLINK.REDIRECT.DUPLICATE_SAME_TARGET"),
            DuplicateErrors = validation.Issues.Count(static issue => issue.Code == "PFLINK.REDIRECT.DUPLICATE"),
            Success = taskSuccess,
            ValidationSuccess = validation.Success,
            UsedSourceCount = dataSet.UsedSources.Length,
            UsedSources = dataSet.UsedSources,
            MissingSourceCount = dataSet.MissingSources.Length,
            MissingSources = dataSet.MissingSources,
            BaselinePath = baseline?.Path,
            BaselineLoaded = baseline?.Loaded,
            BaselineWarningCount = baseline?.KeyCount,
            BaselineGenerated = baseline?.Generated,
            BaselineUpdated = baseline?.Updated,
            BaselineWrittenPath = baseline?.WrittenPath,
            NewWarningCount = baseline?.NewWarnings.Length,
            NewWarnings = baseline?.NewWarnings,
            Issues = validation.Issues,
            Export = export
        };

        File.WriteAllText(summaryPath, JsonSerializer.Serialize(summary, LinksSummaryJsonContext.WebLinkCommandSummary));
    }

    internal static void WriteLinksApplyReviewSummary(string? summaryPath, WebLinkReviewApplyResult result)
    {
        if (string.IsNullOrWhiteSpace(summaryPath))
            return;

        var summaryDirectory = Path.GetDirectoryName(summaryPath);
        if (!string.IsNullOrWhiteSpace(summaryDirectory))
            Directory.CreateDirectory(summaryDirectory);

        File.WriteAllText(summaryPath, JsonSerializer.Serialize(result, WebCliJson.Context.WebLinkReviewApplyResult));
    }

    internal static void WriteIssueReport(string? reportPath, LinkValidationResult validation)
    {
        if (string.IsNullOrWhiteSpace(reportPath))
            return;

        var reportDirectory = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrWhiteSpace(reportDirectory))
            Directory.CreateDirectory(reportDirectory);

        var lines = new List<string>
        {
            "severity,code,id,related_id,source_host,source_path,source_query,target_url,related_target_url,normalized_target_url,related_normalized_target_url,status,related_status,origin_path,origin_line,related_origin_path,related_origin_line,message"
        };

        foreach (var issue in validation.Issues)
        {
            lines.Add(string.Join(",",
                EscapeCsv(issue.Severity.ToString().ToLowerInvariant()),
                EscapeCsv(issue.Code),
                EscapeCsv(issue.Id),
                EscapeCsv(issue.RelatedId),
                EscapeCsv(issue.SourceHost),
                EscapeCsv(issue.SourcePath),
                EscapeCsv(issue.SourceQuery),
                EscapeCsv(issue.TargetUrl),
                EscapeCsv(issue.RelatedTargetUrl),
                EscapeCsv(issue.NormalizedTargetUrl),
                EscapeCsv(issue.RelatedNormalizedTargetUrl),
                EscapeCsv(issue.Status <= 0 ? string.Empty : issue.Status.ToString(CultureInfo.InvariantCulture)),
                EscapeCsv(issue.RelatedStatus <= 0 ? string.Empty : issue.RelatedStatus.ToString(CultureInfo.InvariantCulture)),
                EscapeCsv(issue.OriginPath),
                EscapeCsv(issue.OriginLine <= 0 ? string.Empty : issue.OriginLine.ToString(CultureInfo.InvariantCulture)),
                EscapeCsv(issue.RelatedOriginPath),
                EscapeCsv(issue.RelatedOriginLine <= 0 ? string.Empty : issue.RelatedOriginLine.ToString(CultureInfo.InvariantCulture)),
                EscapeCsv(issue.Message)));
        }

        File.WriteAllLines(reportPath, lines);
    }

    internal static void WriteDuplicateReport(string? reportPath, LinkValidationResult validation)
    {
        if (string.IsNullOrWhiteSpace(reportPath))
            return;

        var reportDirectory = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrWhiteSpace(reportDirectory))
            Directory.CreateDirectory(reportDirectory);

        var lines = new List<string>
        {
            "severity,code,suggested_action,source_host,source_path,source_query,canonical_id,canonical_status,canonical_target_url,canonical_normalized_target_url,canonical_origin_path,canonical_origin_line,duplicate_id,duplicate_status,duplicate_target_url,duplicate_normalized_target_url,duplicate_origin_path,duplicate_origin_line,message"
        };

        foreach (var issue in validation.Issues.Where(static issue =>
                     issue.Code is "PFLINK.REDIRECT.DUPLICATE" or "PFLINK.REDIRECT.DUPLICATE_SAME_TARGET"))
        {
            lines.Add(string.Join(",",
                EscapeCsv(issue.Severity.ToString().ToLowerInvariant()),
                EscapeCsv(issue.Code),
                EscapeCsv(issue.Code == "PFLINK.REDIRECT.DUPLICATE_SAME_TARGET" ? "dedupe_generated_or_imported_row" : "review_canonical_target"),
                EscapeCsv(issue.SourceHost),
                EscapeCsv(issue.SourcePath),
                EscapeCsv(issue.SourceQuery),
                EscapeCsv(issue.RelatedId),
                EscapeCsv(issue.RelatedStatus <= 0 ? string.Empty : issue.RelatedStatus.ToString(CultureInfo.InvariantCulture)),
                EscapeCsv(issue.RelatedTargetUrl),
                EscapeCsv(issue.RelatedNormalizedTargetUrl),
                EscapeCsv(issue.RelatedOriginPath),
                EscapeCsv(issue.RelatedOriginLine <= 0 ? string.Empty : issue.RelatedOriginLine.ToString(CultureInfo.InvariantCulture)),
                EscapeCsv(issue.Id),
                EscapeCsv(issue.Status <= 0 ? string.Empty : issue.Status.ToString(CultureInfo.InvariantCulture)),
                EscapeCsv(issue.TargetUrl),
                EscapeCsv(issue.NormalizedTargetUrl),
                EscapeCsv(issue.OriginPath),
                EscapeCsv(issue.OriginLine <= 0 ? string.Empty : issue.OriginLine.ToString(CultureInfo.InvariantCulture)),
                EscapeCsv(issue.Message)));
        }

        File.WriteAllLines(reportPath, lines);
    }

    internal static void Write404SuggestionReviewCsv(string? reportPath, WebLink404ReportResult result)
    {
        if (string.IsNullOrWhiteSpace(reportPath))
            return;

        EnsureDirectory(reportPath);
        var lines = new List<string>
        {
            "suggested_action,host,path,count,best_target,best_score,all_targets,referrer,last_seen_at"
        };

        foreach (var suggestion in result.Suggestions.OrderByDescending(static item => item.Count).ThenBy(static item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            var targets = suggestion.Suggestions ?? Array.Empty<WebLink404RouteSuggestion>();
            var best = targets.OrderByDescending(static item => item.Score).FirstOrDefault();
            var action = best is null ? "ignore_or_investigate" : "review_redirect_candidate";
            lines.Add(string.Join(",",
                EscapeCsv(action),
                EscapeCsv(suggestion.Host),
                EscapeCsv(suggestion.Path),
                EscapeCsv(suggestion.Count.ToString(CultureInfo.InvariantCulture)),
                EscapeCsv(best?.TargetPath),
                EscapeCsv(best is null ? string.Empty : best.Score.ToString("0.###", CultureInfo.InvariantCulture)),
                EscapeCsv(string.Join(" | ", targets.Select(static item => $"{item.TargetPath} ({item.Score:0.###})"))),
                EscapeCsv(suggestion.Referrer),
                EscapeCsv(suggestion.LastSeenAt)));
        }

        File.WriteAllLines(reportPath, lines);
    }

    internal static void WriteRedirectReviewCsv(string? reportPath, string redirectJsonPath)
    {
        if (string.IsNullOrWhiteSpace(reportPath) || string.IsNullOrWhiteSpace(redirectJsonPath) || !File.Exists(redirectJsonPath))
            return;

        EnsureDirectory(reportPath);
        var dataSet = WebLinkService.Load(new WebLinkLoadOptions { RedirectsPath = redirectJsonPath });
        var lines = new List<string>
        {
            "enabled,id,source_host,source_path,source_query,target_url,status,match_type,group,source,notes"
        };

        foreach (var redirect in dataSet.Redirects.OrderBy(static item => item.SourceHost ?? string.Empty, StringComparer.OrdinalIgnoreCase).ThenBy(static item => item.SourcePath, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add(string.Join(",",
                EscapeCsv(redirect.Enabled ? "true" : "false"),
                EscapeCsv(redirect.Id),
                EscapeCsv(redirect.SourceHost),
                EscapeCsv(redirect.SourcePath),
                EscapeCsv(redirect.SourceQuery),
                EscapeCsv(redirect.TargetUrl),
                EscapeCsv(redirect.Status.ToString(CultureInfo.InvariantCulture)),
                EscapeCsv(redirect.MatchType.ToString().ToLowerInvariant()),
                EscapeCsv(redirect.Group),
                EscapeCsv(redirect.Source),
                EscapeCsv(redirect.Notes)));
        }

        File.WriteAllLines(reportPath, lines);
    }

    internal static void WriteIgnored404ReviewCsv(string? reportPath, string ignored404JsonPath)
    {
        if (string.IsNullOrWhiteSpace(reportPath) || string.IsNullOrWhiteSpace(ignored404JsonPath) || !File.Exists(ignored404JsonPath))
            return;

        EnsureDirectory(reportPath);
        var rules = ReadIgnored404Rules(ignored404JsonPath);
        var lines = new List<string>
        {
            "host,path,reason,created_at,created_by"
        };

        foreach (var rule in rules.OrderBy(static item => item.Host ?? string.Empty, StringComparer.OrdinalIgnoreCase).ThenBy(static item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add(string.Join(",",
                EscapeCsv(rule.Host),
                EscapeCsv(rule.Path),
                EscapeCsv(rule.Reason),
                EscapeCsv(rule.CreatedAt),
                EscapeCsv(rule.CreatedBy)));
        }

        File.WriteAllLines(reportPath, lines);
    }

    internal static WebLinkBaselineState EvaluateBaseline(
        string baseDir,
        string? baselinePath,
        LinkValidationResult validation,
        bool baselineGenerate,
        bool baselineUpdate,
        bool failOnNewWarnings)
    {
        if ((baselineGenerate || baselineUpdate || failOnNewWarnings) && string.IsNullOrWhiteSpace(baselinePath))
            baselinePath = ".powerforge/link-baseline.json";

        var warningKeys = validation.Issues
            .Where(static issue => issue.Severity == LinkValidationSeverity.Warning)
            .Select(BuildIssueKey)
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var state = new WebLinkBaselineState
        {
            Path = baselinePath,
            CurrentWarningKeys = warningKeys,
            Generated = baselineGenerate,
            Updated = baselineUpdate,
            Merge = baselineUpdate,
            ShouldWrite = baselineGenerate || baselineUpdate
        };

        if (string.IsNullOrWhiteSpace(baselinePath))
            return state;

        state.Loaded = WebVerifyBaselineStore.TryLoadWarningKeys(baseDir, baselinePath, out var resolvedPath, out var baselineKeys);
        state.Path = string.IsNullOrWhiteSpace(resolvedPath) ? baselinePath : resolvedPath;
        state.KeyCount = baselineKeys.Length;

        var baselineSet = state.Loaded
            ? new HashSet<string>(baselineKeys, StringComparer.OrdinalIgnoreCase)
            : null;
        state.NewWarnings = baselineSet is null
            ? Array.Empty<LinkValidationIssue>()
            : validation.Issues
                .Where(static issue => issue.Severity == LinkValidationSeverity.Warning)
                .Where(issue => !baselineSet.Contains(BuildIssueKey(issue)))
                .ToArray();

        return state;
    }

    internal static string BuildValidateSuccessMessage(LinkValidationResult validation, WebLinkBaselineState baseline)
    {
        var message = $"links-validate ok: redirects={validation.RedirectCount}; shortlinks={validation.ShortlinkCount}; warnings={validation.WarningCount}";
        if (!string.IsNullOrWhiteSpace(baseline.Path) && baseline.Loaded)
            message += $"; newWarnings={baseline.NewWarnings.Length}";
        if (!string.IsNullOrWhiteSpace(baseline.WrittenPath))
            message += "; baseline written";
        return message;
    }

    internal static string BuildValidateFailureMessage(LinkValidationResult validation, WebLinkBaselineState baseline, bool failOnNewWarnings)
    {
        if (failOnNewWarnings && !baseline.Loaded)
            return $"links-validate failed: baseline could not be loaded; errors={validation.ErrorCount}; warnings={validation.WarningCount}";
        if (failOnNewWarnings && baseline.NewWarnings.Length > 0)
            return $"links-validate failed: newWarnings={baseline.NewWarnings.Length}; errors={validation.ErrorCount}; warnings={validation.WarningCount}";
        return $"links-validate failed: errors={validation.ErrorCount}; warnings={validation.WarningCount}";
    }

    internal static string BuildIssueKey(LinkValidationIssue issue)
        => string.Join("|",
            issue.Code ?? string.Empty,
            issue.Source ?? string.Empty,
            issue.Id ?? string.Empty,
            issue.SourceHost ?? string.Empty,
            issue.SourcePath ?? string.Empty,
            issue.SourceQuery ?? string.Empty,
            issue.Status.ToString(CultureInfo.InvariantCulture),
            issue.NormalizedTargetUrl ?? issue.TargetUrl ?? string.Empty,
            issue.RelatedId ?? string.Empty,
            issue.RelatedStatus.ToString(CultureInfo.InvariantCulture),
            issue.RelatedNormalizedTargetUrl ?? issue.RelatedTargetUrl ?? string.Empty);

    private static Ignored404Rule[] ReadIgnored404Rules(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var source = document.RootElement;
        if (source.ValueKind == JsonValueKind.Object && source.TryGetProperty("ignored404", out var nested))
            source = nested;
        if (source.ValueKind != JsonValueKind.Array)
            return Array.Empty<Ignored404Rule>();

        return source.Deserialize<Ignored404Rule[]>(LinksSummaryJsonOptions) ?? Array.Empty<Ignored404Rule>();
    }

    private static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }

    private static string EscapeCsv(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && IsCsvFormulaPrefix(text[0]))
            text = "'" + text;
        if (text.Contains('"', StringComparison.Ordinal))
            text = text.Replace("\"", "\"\"", StringComparison.Ordinal);
        return text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + text + "\"" : text;
    }

    private static bool IsCsvFormulaPrefix(char value)
        => value is '=' or '+' or '-' or '@';

    private static readonly JsonSerializerOptions LinksSummaryJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly PowerForgeWebCliJsonContext LinksSummaryJsonContext = new(new JsonSerializerOptions(WebCliJson.Options)
    {
        WriteIndented = true
    });
}

internal sealed class WebLinkCommandSummary
{
    public string GeneratedOn { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public int Redirects { get; set; }
    public int Shortlinks { get; set; }
    public int Errors { get; set; }
    public int Warnings { get; set; }
    public int DuplicateWarnings { get; set; }
    public int DuplicateErrors { get; set; }
    public bool Success { get; set; }
    public bool ValidationSuccess { get; set; }
    public int UsedSourceCount { get; set; }
    public string[] UsedSources { get; set; } = Array.Empty<string>();
    public int MissingSourceCount { get; set; }
    public string[] MissingSources { get; set; } = Array.Empty<string>();
    public string? BaselinePath { get; set; }
    public bool? BaselineLoaded { get; set; }
    public int? BaselineWarningCount { get; set; }
    public bool? BaselineGenerated { get; set; }
    public bool? BaselineUpdated { get; set; }
    public string? BaselineWrittenPath { get; set; }
    public int? NewWarningCount { get; set; }
    public LinkValidationIssue[]? NewWarnings { get; set; }
    public LinkValidationIssue[] Issues { get; set; } = Array.Empty<LinkValidationIssue>();
    public WebLinkApacheExportResult? Export { get; set; }
}

internal sealed class WebLinkBaselineState
{
    public string? Path { get; set; }
    public bool Loaded { get; set; }
    public int KeyCount { get; set; }
    public bool Generated { get; set; }
    public bool Updated { get; set; }
    public bool Merge { get; set; }
    public bool ShouldWrite { get; set; }
    public string? WrittenPath { get; set; }
    public string[] CurrentWarningKeys { get; set; } = Array.Empty<string>();
    public LinkValidationIssue[] NewWarnings { get; set; } = Array.Empty<LinkValidationIssue>();
}
