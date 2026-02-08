using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static string BuildAuditSummary(WebAuditResult result)
    {
        var parts = new List<string>
        {
            $"pages {result.PageCount}",
            $"links {result.LinkCount}",
            $"assets {result.AssetCount}"
        };

        if (result.HtmlSelectedFileCount > 0 && result.HtmlFileCount > 0 && result.HtmlSelectedFileCount != result.HtmlFileCount)
            parts.Insert(0, $"html-scope {result.HtmlSelectedFileCount}/{result.HtmlFileCount}");

        if (result.BrokenLinkCount > 0)
            parts.Add($"broken-links {result.BrokenLinkCount}");
        if (result.MissingAssetCount > 0)
            parts.Add($"missing-assets {result.MissingAssetCount}");
        parts.Add($"nav-checked {result.NavCheckedCount}");
        if (result.NavIgnoredCount > 0)
            parts.Add($"nav-ignored {result.NavIgnoredCount}");
        parts.Add($"nav-coverage {result.NavCoveragePercent:0.0}%");
        if (result.NavMismatchCount > 0)
            parts.Add($"nav-mismatches {result.NavMismatchCount}");
        if (result.RequiredRouteCount > 0)
            parts.Add($"required-routes {result.RequiredRouteCount}");
        if (result.MissingRequiredRouteCount > 0)
            parts.Add($"missing-required-routes {result.MissingRequiredRouteCount}");
        if (result.WarningCount > 0)
            parts.Add($"warnings {result.WarningCount}");
        if (result.NewIssueCount > 0)
            parts.Add($"new {result.NewIssueCount}");
        if (!string.IsNullOrWhiteSpace(result.SarifPath))
            parts.Add("sarif");

        return $"Audit ok {string.Join(", ", parts)}";
    }

    private static string BuildDoctorSummary(
        WebVerifyResult? verify,
        WebAuditResult? audit,
        bool buildExecuted,
        bool verifyExecuted,
        bool auditExecuted,
        string[]? policyFailures = null)
    {
        var parts = new List<string>();
        parts.Add(buildExecuted ? "build" : "no-build");
        parts.Add(verifyExecuted ? "verify" : "no-verify");
        parts.Add(auditExecuted ? "audit" : "no-audit");

        if (verify is not null)
            parts.Add($"verify {verify.Errors.Length}e/{verify.Warnings.Length}w");
        if (audit is not null)
            parts.Add($"audit {audit.ErrorCount}e/{audit.WarningCount}w");
        if (audit is not null && !string.IsNullOrWhiteSpace(audit.SummaryPath))
            parts.Add("summary");
        if (audit is not null && !string.IsNullOrWhiteSpace(audit.SarifPath))
            parts.Add("sarif");
        if (policyFailures is { Length: > 0 })
            parts.Add($"verify-policy {policyFailures.Length}");

        return $"Doctor ok {string.Join(", ", parts)}";
    }

    private static string BuildAuditFailureSummary(WebAuditResult result, int previewCount)
    {
        var safePreviewCount = Math.Clamp(previewCount, 0, 50);
        var headline = BuildAuditFailureHeadline(result);
        var parts = new List<string>
        {
            string.IsNullOrWhiteSpace(headline)
                ? $"Audit failed ({result.Errors.Length} errors)"
                : $"Audit failed ({result.Errors.Length} errors): {TruncateForLog(headline, 180)}"
        };

        if (!string.IsNullOrWhiteSpace(result.SummaryPath))
            parts.Add($"summary {result.SummaryPath}");
        if (!string.IsNullOrWhiteSpace(result.SarifPath))
            parts.Add($"sarif {result.SarifPath}");

        if (safePreviewCount <= 0 || result.Errors.Length == 0)
            return string.Join(", ", parts);

        var preview = result.Errors
            .Where(static error => !string.IsNullOrWhiteSpace(error))
            .Take(safePreviewCount)
            .Select(error => TruncateForLog(error, 220))
            .ToArray();

        if (preview.Length == 0)
            return string.Join(", ", parts);

        var previewText = string.Join(" | ", preview);
        var remaining = result.Errors.Length - preview.Length;
        if (remaining > 0)
            previewText += $" | +{remaining} more";

        parts.Add($"sample: {previewText}");

        if (result.Issues.Length > 0)
        {
            var issuePreviewCount = Math.Min(safePreviewCount, 5);
            if (issuePreviewCount > 0)
            {
                var candidateIssues = result.Issues
                    .Where(static issue => !IsGateIssue(issue))
                    .ToArray();
                if (candidateIssues.Length == 0)
                    candidateIssues = result.Issues;

                var issueSample = candidateIssues
                    .Where(static issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase))
                    .Take(issuePreviewCount)
                    .ToArray();

                if (issueSample.Length == 0)
                {
                    issueSample = candidateIssues
                        .Where(static issue => !string.IsNullOrWhiteSpace(issue.Message))
                        .Take(issuePreviewCount)
                        .ToArray();
                }

                if (issueSample.Length > 0)
                {
                    var issueText = string.Join(" | ", issueSample.Select(FormatIssueForLog));
                    var issueRemaining = result.Issues.Length - issueSample.Length;
                    if (issueRemaining > 0)
                        issueText += $" | +{issueRemaining} more issues";

                    parts.Add($"issues: {issueText}");
                }
            }
        }

        return string.Join(", ", parts);
    }

    private static string? BuildAuditFailureHeadline(WebAuditResult result)
    {
        // Prefer a non-gate error issue because it's usually the root cause (vs. "gate failed").
        var issue = result.Issues
            .FirstOrDefault(static i =>
                string.Equals(i.Severity, "error", StringComparison.OrdinalIgnoreCase) &&
                !IsGateIssue(i) &&
                !string.IsNullOrWhiteSpace(i.Message));
        if (issue is not null)
            return FormatIssueForLog(issue);

        // Fall back to any error string.
        var error = result.Errors.FirstOrDefault(static e => !string.IsNullOrWhiteSpace(e));
        if (!string.IsNullOrWhiteSpace(error))
            return error;

        // Last resort: any issue message.
        var any = result.Issues.FirstOrDefault(static i => !string.IsNullOrWhiteSpace(i.Message));
        if (any is not null)
            return FormatIssueForLog(any);

        return null;
    }

    private static bool IsGateIssue(WebAuditIssue issue)
    {
        if (string.Equals(issue.Category, "gate", StringComparison.OrdinalIgnoreCase))
            return true;

        return issue.Message.StartsWith("Audit gate failed", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatIssueForLog(WebAuditIssue issue)
    {
        var severity = string.IsNullOrWhiteSpace(issue.Severity) ? "warning" : issue.Severity;
        var category = string.IsNullOrWhiteSpace(issue.Category) ? "general" : issue.Category;
        var location = string.IsNullOrWhiteSpace(issue.Path) ? string.Empty : $" {issue.Path}";
        var message = string.IsNullOrWhiteSpace(issue.Message) ? "issue reported" : issue.Message;
        return TruncateForLog($"[{severity}] [{category}]{location} {message}", 220);
    }

    private static string TruncateForLog(string text, int maxLength)
    {
        var normalized = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (normalized.Length <= maxLength)
            return normalized;

        return normalized[..Math.Max(0, maxLength - 3)] + "...";
    }
}
