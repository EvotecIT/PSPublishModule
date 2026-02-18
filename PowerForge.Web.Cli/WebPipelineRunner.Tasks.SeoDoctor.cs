using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecuteSeoDoctor(
        JsonElement step,
        string baseDir,
        bool fast,
        string lastBuildOutPath,
        string[] lastBuildUpdatedFiles,
        WebPipelineStepResult stepResult)
    {
        var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
        if (string.IsNullOrWhiteSpace(siteRoot))
            throw new InvalidOperationException("seo-doctor requires siteRoot.");
        if (!Directory.Exists(siteRoot))
            throw new DirectoryNotFoundException($"Site root not found: {siteRoot}");

        var include = GetString(step, "include");
        var exclude = GetString(step, "exclude");
        var includeScopeFromBuildUpdated = GetBool(step, "scopeFromBuildUpdated") ?? GetBool(step, "scope-from-build-updated");
        if ((includeScopeFromBuildUpdated != false &&
             (includeScopeFromBuildUpdated == true || fast) &&
             string.IsNullOrWhiteSpace(include) &&
             lastBuildUpdatedFiles.Length > 0 &&
             string.Equals(Path.GetFullPath(siteRoot), lastBuildOutPath, FileSystemPathComparison)))
        {
            var updatedHtml = lastBuildUpdatedFiles
                .Where(static path => path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                                      path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (updatedHtml.Length > 0)
                include = string.Join(";", updatedHtml);
        }

        var useDefaultExclude = !(GetBool(step, "noDefaultExclude") ?? false);
        var maxHtmlFiles = GetInt(step, "maxHtmlFiles") ?? GetInt(step, "max-html-files") ?? 0;

        var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
        {
            SiteRoot = siteRoot,
            Include = CliPatternHelper.SplitPatterns(include),
            Exclude = CliPatternHelper.SplitPatterns(exclude),
            UseDefaultExcludes = useDefaultExclude,
            MaxHtmlFiles = Math.Max(0, maxHtmlFiles),
            IncludeNoIndexPages = GetBool(step, "includeNoIndexPages") ?? false,
            CheckTitleLength = GetBool(step, "checkTitleLength") ?? true,
            CheckDescriptionLength = GetBool(step, "checkDescriptionLength") ?? true,
            CheckH1 = GetBool(step, "checkH1") ?? GetBool(step, "checkHeading") ?? true,
            CheckImageAlt = GetBool(step, "checkImageAlt") ?? true,
            CheckDuplicateTitles = GetBool(step, "checkDuplicateTitles") ?? true,
            CheckOrphanPages = GetBool(step, "checkOrphanPages") ?? true,
            CheckFocusKeyphrase = GetBool(step, "checkFocusKeyphrase") ?? false,
            MinTitleLength = GetInt(step, "minTitleLength") ?? 30,
            MaxTitleLength = GetInt(step, "maxTitleLength") ?? 60,
            MinDescriptionLength = GetInt(step, "minDescriptionLength") ?? 70,
            MaxDescriptionLength = GetInt(step, "maxDescriptionLength") ?? 160,
            MinFocusKeyphraseMentions = GetInt(step, "minFocusKeyphraseMentions") ?? 2,
            FocusKeyphraseMetaNames = ResolveFocusKeyphraseMetaNames(step)
        });

        var baselineGenerate = GetBool(step, "baselineGenerate") ?? false;
        var baselineUpdate = GetBool(step, "baselineUpdate") ?? false;
        var baselinePath = GetString(step, "baselinePath") ?? GetString(step, "baseline");
        var failOnWarnings = GetBool(step, "failOnWarnings") ?? false;
        var failOnNewIssues = GetBool(step, "failOnNewIssues") ?? GetBool(step, "failOnNew") ?? false;
        var maxErrors = GetInt(step, "maxErrors") ?? -1;
        var maxWarnings = GetInt(step, "maxWarnings") ?? -1;

        if ((baselineGenerate || baselineUpdate || failOnNewIssues) && string.IsNullOrWhiteSpace(baselinePath))
            baselinePath = ".powerforge/seo-baseline.json";

        var issues = result.Issues.ToList();
        var errors = result.Errors.ToList();
        var warnings = result.Warnings.ToList();

        if (!string.IsNullOrWhiteSpace(baselinePath))
        {
            var baselineLoaded = WebSeoDoctorBaselineStore.TryLoadIssueHashes(baseDir, baselinePath, out _, out var baselineHashes);
            var baselineSet = baselineLoaded
                ? new HashSet<string>(baselineHashes, StringComparer.OrdinalIgnoreCase)
                : null;

            foreach (var issue in issues)
            {
                var keyHash = WebAuditKeyHasher.Hash(issue.Key);
                issue.IsNew = baselineSet is null ||
                              string.IsNullOrWhiteSpace(keyHash) ||
                              !baselineSet.Contains(keyHash);
            }

            result.BaselinePath = WebSeoDoctorBaselineStore.ResolveBaselinePath(baseDir, baselinePath);
            result.BaselineIssueCount = baselineSet?.Count ?? 0;

            if (failOnNewIssues && baselineSet is null)
            {
                AddGateIssue(issues, errors,
                    "fail-on-new is enabled but SEO baseline could not be loaded (missing/empty/bad path). Generate one with baselineGenerate.",
                    "gate-fail-new-missing-baseline");
            }
        }

        result.NewIssueCount = issues.Count(issue => issue.IsNew);
        result.NewErrorCount = issues.Count(issue =>
            issue.IsNew && issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));
        result.NewWarningCount = issues.Count(issue =>
            issue.IsNew && issue.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase));

        if (failOnWarnings && warnings.Count > 0)
        {
            AddGateIssue(issues, errors,
                $"warnings present ({warnings.Count}) and failOnWarnings is enabled.",
                "gate-fail-warnings");
        }

        if (failOnNewIssues && result.NewIssueCount > 0)
        {
            AddGateIssue(issues, errors,
                $"new SEO issues present ({result.NewIssueCount}) and failOnNew is enabled.",
                "gate-fail-new");
        }

        if (maxErrors >= 0 && errors.Count > maxErrors)
        {
            AddGateIssue(issues, errors,
                $"errors {errors.Count} exceed maxErrors {maxErrors}.",
                "gate-max-errors");
        }

        if (maxWarnings >= 0 && warnings.Count > maxWarnings)
        {
            AddGateIssue(issues, errors,
                $"warnings {warnings.Count} exceed maxWarnings {maxWarnings}.",
                "gate-max-warnings");
        }

        string? baselineWrittenPath = null;
        if (baselineGenerate || baselineUpdate)
        {
            var mutableResult = CloneResultWith(issues, errors, warnings, result);
            baselineWrittenPath = WebSeoDoctorBaselineStore.Write(baseDir, baselinePath, mutableResult, baselineUpdate, logger: null);
            result.BaselinePath = baselineWrittenPath;
        }

        var reportPath = GetString(step, "reportPath") ?? GetString(step, "report-path");
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            var resolvedReportPath = ResolvePathWithinRoot(baseDir, reportPath, reportPath);
            var snapshot = CloneResultWith(issues, errors, warnings, result);
            var reportDirectory = Path.GetDirectoryName(resolvedReportPath);
            if (!string.IsNullOrWhiteSpace(reportDirectory))
                Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(resolvedReportPath, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
            result.ReportPath = resolvedReportPath;
        }

        var summaryPath = GetString(step, "summaryPath") ?? GetString(step, "summary-path");
        if (!string.IsNullOrWhiteSpace(summaryPath))
        {
            var resolvedSummaryPath = ResolvePathWithinRoot(baseDir, summaryPath, summaryPath);
            var summaryDirectory = Path.GetDirectoryName(resolvedSummaryPath);
            if (!string.IsNullOrWhiteSpace(summaryDirectory))
                Directory.CreateDirectory(summaryDirectory);
            var markdown = BuildSeoDoctorMarkdownSummary(CloneResultWith(issues, errors, warnings, result));
            File.WriteAllText(resolvedSummaryPath, markdown);
            result.SummaryPath = resolvedSummaryPath;
        }

        var finalResult = CloneResultWith(issues, errors, warnings, result);
        finalResult.IssueCount = issues.Count;
        finalResult.ErrorCount = errors.Count;
        finalResult.WarningCount = warnings.Count;
        finalResult.NewIssueCount = issues.Count(issue => issue.IsNew);
        finalResult.NewErrorCount = issues.Count(issue =>
            issue.IsNew && issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));
        finalResult.NewWarningCount = issues.Count(issue =>
            issue.IsNew && issue.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase));
        finalResult.Success = errors.Count == 0;

        stepResult.Success = finalResult.Success;
        stepResult.Message = BuildSeoDoctorSummary(finalResult);
        if (!string.IsNullOrWhiteSpace(baselineWrittenPath))
            stepResult.Message += $", baseline {baselineWrittenPath}";
        if (!finalResult.Success)
            throw new InvalidOperationException(stepResult.Message);
    }

    private static WebSeoDoctorResult CloneResultWith(
        IReadOnlyList<WebSeoDoctorIssue> issues,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings,
        WebSeoDoctorResult source)
    {
        return new WebSeoDoctorResult
        {
            Success = source.Success,
            HtmlFileCount = source.HtmlFileCount,
            HtmlSelectedFileCount = source.HtmlSelectedFileCount,
            PageCount = source.PageCount,
            OrphanPageCount = source.OrphanPageCount,
            IssueCount = issues.Count,
            ErrorCount = errors.Count,
            WarningCount = warnings.Count,
            NewIssueCount = source.NewIssueCount,
            NewErrorCount = source.NewErrorCount,
            NewWarningCount = source.NewWarningCount,
            BaselinePath = source.BaselinePath,
            BaselineIssueCount = source.BaselineIssueCount,
            ReportPath = source.ReportPath,
            SummaryPath = source.SummaryPath,
            Issues = issues.ToArray(),
            Errors = errors.ToArray(),
            Warnings = warnings.ToArray()
        };
    }

    private static void AddGateIssue(List<WebSeoDoctorIssue> issues, List<string> errors, string message, string hint)
    {
        var normalizedHint = NormalizeSeoDoctorIssueToken(hint);
        var code = $"PFSEO.GATE.{normalizedHint.Replace('-', '_').ToUpperInvariant()}";
        var issue = new WebSeoDoctorIssue
        {
            Severity = "error",
            Category = "gate",
            Code = code,
            Hint = normalizedHint,
            Message = "SEO doctor gate failed: " + message,
            Key = $"error|gate|-|{normalizedHint}"
        };
        issues.Add(issue);
        errors.Add($"[{code}] SEO doctor gate failed: {message}");
    }

    private static string NormalizeSeoDoctorIssueToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "general";

        var sb = new StringBuilder(value.Length);
        var previousDash = false;
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
            {
                sb.Append(ch);
                previousDash = false;
                continue;
            }

            if (previousDash)
                continue;
            sb.Append('-');
            previousDash = true;
        }

        return sb.ToString().Trim('-');
    }

    private static string[] ResolveFocusKeyphraseMetaNames(JsonElement step)
    {
        var values = GetArrayOfStrings(step, "focusKeyphraseMetaNames") ??
                     GetArrayOfStrings(step, "focus-keyphrase-meta-names");
        if (values is { Length: > 0 })
            return values;

        var single = GetString(step, "focusKeyphraseMetaName") ??
                     GetString(step, "focus-keyphrase-meta-name");
        if (!string.IsNullOrWhiteSpace(single))
            return new[] { single };

        return new[] { "pf:focus-keyphrase", "focus-keyphrase", "seo-focus-keyphrase" };
    }

    private static string BuildSeoDoctorSummary(WebSeoDoctorResult result)
    {
        var message = $"seo-doctor: {result.PageCount} pages, {result.ErrorCount} errors, {result.WarningCount} warnings, {result.NewIssueCount} new issues";
        if (result.OrphanPageCount > 0)
            message += $", {result.OrphanPageCount} orphan candidates";
        if (!string.IsNullOrWhiteSpace(result.ReportPath))
            message += $", report {result.ReportPath}";
        if (!string.IsNullOrWhiteSpace(result.SummaryPath))
            message += $", summary {result.SummaryPath}";
        return message;
    }

    private static string BuildSeoDoctorMarkdownSummary(WebSeoDoctorResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# SEO Doctor Summary");
        builder.AppendLine();
        builder.AppendLine($"- Success: {(result.Success ? "yes" : "no")}");
        builder.AppendLine($"- Pages scanned: {result.PageCount}");
        builder.AppendLine($"- Issues: {result.IssueCount}");
        builder.AppendLine($"- Errors: {result.ErrorCount}");
        builder.AppendLine($"- Warnings: {result.WarningCount}");
        builder.AppendLine($"- New issues: {result.NewIssueCount}");
        if (!string.IsNullOrWhiteSpace(result.BaselinePath))
            builder.AppendLine($"- Baseline: {result.BaselinePath} ({result.BaselineIssueCount} keys)");
        if (result.OrphanPageCount > 0)
            builder.AppendLine($"- Orphan candidates: {result.OrphanPageCount}");

        var grouped = result.Issues
            .GroupBy(issue => issue.Code, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();

        if (grouped.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Top Issues");
            builder.AppendLine();
            builder.AppendLine("| Count | Code | Example |");
            builder.AppendLine("|---:|---|---|");
            foreach (var group in grouped)
            {
                var sample = group.FirstOrDefault();
                var example = sample is null
                    ? "-"
                    : string.IsNullOrWhiteSpace(sample.Path)
                        ? sample.Message
                        : $"{sample.Path}: {sample.Message}";
                example = example.Replace("|", "\\|");
                builder.AppendLine($"| {group.Count()} | `{group.Key}` | {example} |");
            }
        }

        return builder.ToString();
    }
}
