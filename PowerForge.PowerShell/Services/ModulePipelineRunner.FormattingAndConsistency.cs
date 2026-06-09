using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private void LogFormattingIssues(string rootPath, FormatterResult[] results, string label)
    {
        var issues = CollectFormattingIssues(rootPath, results);
        if (issues.Length == 0) return;

        var errorCount = 0;
        var skippedCount = 0;
        foreach (var i in issues)
        {
            if (i.IsError) errorCount++;
            else skippedCount++;
        }

        _logger.Error($"Formatting issues for {label}: {errorCount} error(s), {skippedCount} skipped.");

        const int maxItems = 20;
        var shown = 0;
        foreach (var issue in issues.Where(i => i.IsError).Concat(issues.Where(i => !i.IsError)))
        {
            if (shown++ >= maxItems) break;
            var prefix = issue.IsError ? "ERROR" : "SKIP";
            var line = $"{prefix}: {issue.Path} - {issue.Message}";
            if (issue.IsError) _logger.Error(line);
            else _logger.Warn(line);
        }

        if (issues.Length > maxItems)
            _logger.Warn($"Formatting issues: {issues.Length - maxItems} more not shown.");
    }

    private static string BuildFormattingFailureMessage(
        string label,
        string rootPath,
        FormattingSummary summary,
        FormatterResult[] results)
    {
        var message = $"Formatting failed for {label} (errors {summary.Errors}/{summary.Total}).";
        var issues = CollectFormattingIssues(rootPath, results, onlyErrors: true, maxItems: 3);
        if (issues.Length == 0) return message;
        var sample = string.Join(" | ", issues.Select(i => $"{i.Path}: {i.Message}"));
        return $"{message} First error(s): {sample}";
    }

    private static FormattingIssue[] CollectFormattingIssues(
        string rootPath,
        FormatterResult[] results,
        bool onlyErrors = false,
        int maxItems = 0)
    {
        if (results is null || results.Length == 0) return Array.Empty<FormattingIssue>();

        var list = new List<FormattingIssue>();
        foreach (var r in results)
        {
            if (r is null) continue;
            var msg = r.Message ?? string.Empty;
            var isError = FormattingSummary.IsErrorMessage(msg);
            var isSkipped = FormattingSummary.IsSkippedMessage(msg);
            if (!isError && !isSkipped) continue;
            if (onlyErrors && !isError) continue;

            var rel = FormatFormattingPath(rootPath, r.Path);
            var message = NormalizeFormattingMessage(msg);
            list.Add(new FormattingIssue(rel, message, isError));
        }

        if (maxItems > 0 && list.Count > maxItems)
            return list.Take(maxItems).ToArray();

        return list.ToArray();
    }

    private static string NormalizeFormattingMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "Unknown";
        var idx = message.IndexOf(';');
        if (idx < 0) return message.Trim();
        var token = message.Substring(0, idx).Trim();
        var details = message.Substring(idx + 1).Trim();
        return string.IsNullOrWhiteSpace(details) ? token : $"{token} ({details})";
    }

    private static string FormatFormattingPath(string rootPath, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return "<unknown>";
        if (string.IsNullOrWhiteSpace(rootPath)) return fullPath;
        try
        {
            return ProjectTextInspection.ComputeRelativePath(rootPath, fullPath);
        }
        catch
        {
            return fullPath;
        }
    }

    private sealed class FormattingIssue
    {
        public string Path { get; }
        public string Message { get; }
        public bool IsError { get; }

        public FormattingIssue(string path, string message, bool isError)
        {
            Path = path ?? string.Empty;
            Message = message ?? string.Empty;
            IsError = isError;
        }
    }

    private void LogFileConsistencyIssues(
        ProjectConsistencyReport report,
        FileConsistencySettings settings,
        string label,
        CheckStatus status)
    {
        if (report is null) return;
        var problematic = report.ProblematicFiles ?? Array.Empty<ProjectConsistencyFileDetail>();
        if (problematic.Length == 0) return;

        var log = status == CheckStatus.Fail ? (Action<string>)_logger.Error : _logger.Warn;
        log($"File consistency issues for {label}: {problematic.Length} file(s).");

        if (!string.IsNullOrWhiteSpace(report.ExportPath))
            log($"File consistency report ({label}): {report.ExportPath}");

        var summary = report.Summary;
        var parts = new List<string>();
        if (summary.FilesNeedingEncodingConversion > 0)
            parts.Add($"encoding {summary.FilesNeedingEncodingConversion}");
        if (summary.FilesNeedingLineEndingConversion > 0)
            parts.Add($"line endings {summary.FilesNeedingLineEndingConversion}");
        if (settings.CheckMixedLineEndings && summary.FilesWithMixedLineEndings > 0)
            parts.Add($"mixed {summary.FilesWithMixedLineEndings}");
        if (settings.CheckMissingFinalNewline && summary.FilesMissingFinalNewline > 0)
            parts.Add($"missing newline {summary.FilesMissingFinalNewline}");
        if (parts.Count > 0)
            log($"File consistency summary ({label}): {string.Join(", ", parts)} (total {summary.TotalFiles}).");

        const int maxItems = 20;
        var shown = 0;
        foreach (var item in problematic)
        {
            var reasons = BuildFileConsistencyReasons(item, settings);
            if (reasons.Count == 0) continue;
            log($"{item.RelativePath} - {string.Join(", ", reasons)}");
            if (++shown >= maxItems) break;
        }

        if (problematic.Length > maxItems)
            _logger.Warn($"File consistency issues: {problematic.Length - maxItems} more not shown.");
    }

    private static List<string> BuildFileConsistencyReasons(
        ProjectConsistencyFileDetail file,
        FileConsistencySettings settings)
    {
        var reasons = new List<string>(4);

        if (file.NeedsEncodingConversion)
        {
            var current = file.CurrentEncoding?.ToString() ?? "Unknown";
            reasons.Add($"encoding {current} (expected {file.RecommendedEncoding})");
        }

        if (file.NeedsLineEndingConversion)
        {
            var current = file.CurrentLineEnding.ToString();
            reasons.Add($"line endings {current} (expected {file.RecommendedLineEnding})");
        }

        if (settings.CheckMixedLineEndings && file.HasMixedLineEndings)
            reasons.Add("mixed line endings");

        if (settings.CheckMissingFinalNewline && file.MissingFinalNewline)
            reasons.Add("missing final newline");

        var error = file.Error;
        if (!string.IsNullOrWhiteSpace(error))
            reasons.Add($"error: {error!.Trim()}");

        return reasons;
    }

}
