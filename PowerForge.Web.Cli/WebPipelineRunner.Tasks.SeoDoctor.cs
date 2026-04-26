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
    private static readonly JsonSerializerOptions SeoDoctorIndentedJsonOptions = new()
    {
        WriteIndented = true
    };

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
        var referenceSiteRoots = GetArrayOfStrings(step, "referenceSiteRoots")
                                 ?? GetArrayOfStrings(step, "reference-site-roots")
                                 ?? CliPatternHelper.SplitPatterns(
                                     GetString(step, "referenceSiteRoots") ??
                                     GetString(step, "reference-site-roots"));
        var languageRootHosts = ResolveSeoDoctorLanguageRootHosts(step, baseDir);

        var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
        {
            SiteRoot = siteRoot,
            ReferenceSiteRoots = referenceSiteRoots
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Select(path => ResolvePath(baseDir, path))
                .OfType<string>()
                .ToArray(),
            LanguageRootHosts = languageRootHosts,
            Include = CliPatternHelper.SplitPatterns(include),
            Exclude = CliPatternHelper.SplitPatterns(exclude),
            UseDefaultExcludes = useDefaultExclude,
            MaxHtmlFiles = Math.Max(0, maxHtmlFiles),
            IncludeNoIndexPages = GetBool(step, "includeNoIndexPages") ?? false,
            PageAssertions = ResolvePageAssertions(step),
            CheckTitleLength = GetBool(step, "checkTitleLength") ?? true,
            CheckDescriptionLength = GetBool(step, "checkDescriptionLength") ?? true,
            CheckH1 = GetBool(step, "checkH1") ?? GetBool(step, "checkHeading") ?? true,
            CheckImageAlt = GetBool(step, "checkImageAlt") ?? true,
            CheckEmptyImageAlt = GetBool(step, "checkEmptyImageAlt") ?? GetBool(step, "check-empty-image-alt") ?? false,
            CheckSourceMarkdownImageAlt = GetBool(step, "checkSourceMarkdownImageAlt") ?? GetBool(step, "check-source-markdown-image-alt") ?? false,
            ContentRoot = ResolvePath(baseDir, GetString(step, "contentRoot") ?? GetString(step, "content-root")),
            CheckDuplicateTitles = GetBool(step, "checkDuplicateTitles") ?? true,
            CheckOrphanPages = GetBool(step, "checkOrphanPages") ?? true,
            CheckFocusKeyphrase = GetBool(step, "checkFocusKeyphrase") ?? false,
            CheckCanonical = GetBool(step, "checkCanonical") ?? GetBool(step, "check-canonical") ?? true,
            CheckHreflang = GetBool(step, "checkHreflang") ?? GetBool(step, "check-hreflang") ?? true,
            CheckStructuredData = GetBool(step, "checkStructuredData") ?? GetBool(step, "check-structured-data") ?? true,
            CheckContentLeaks = GetBool(step, "checkContentLeaks") ?? GetBool(step, "check-content-leaks") ?? true,
            RequireCanonical = GetBool(step, "requireCanonical") ?? GetBool(step, "require-canonical") ?? false,
            RequireHreflang = GetBool(step, "requireHreflang") ?? GetBool(step, "require-hreflang") ?? false,
            RequireHreflangXDefault = GetBool(step, "requireHreflangXDefault") ?? GetBool(step, "require-hreflang-x-default") ?? false,
            RequireStructuredData = GetBool(step, "requireStructuredData") ?? GetBool(step, "require-structured-data") ?? false,
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

        WriteSeoDoctorBacklogArtifacts(step, baseDir, CloneResultWith(issues, errors, warnings, result));

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

    private static Dictionary<string, string> ResolveSeoDoctorLanguageRootHosts(JsonElement step, string baseDir)
    {
        var configPath = GetString(step, "config") ??
                         GetString(step, "siteConfig") ??
                         GetString(step, "site-config");
        if (string.IsNullOrWhiteSpace(configPath))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var resolvedConfigPath = ResolvePath(baseDir, configPath);
        if (string.IsNullOrWhiteSpace(resolvedConfigPath) || !File.Exists(resolvedConfigPath))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var (siteSpec, _) = WebSiteSpecLoader.LoadWithPath(resolvedConfigPath, WebCliJson.Options);
        var hosts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (siteSpec.Links?.LanguageRootHosts is { Count: > 0 })
        {
            foreach (var pair in siteSpec.Links.LanguageRootHosts)
            {
                var host = NormalizeSeoDoctorHostKey(pair.Key);
                var prefix = NormalizeSeoDoctorLanguagePrefix(pair.Value);
                if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(prefix))
                    continue;
                hosts[host] = prefix;
            }
        }

        if (siteSpec.Localization?.Enabled == true && siteSpec.Localization.Languages is { Length: > 0 })
        {
            foreach (var language in siteSpec.Localization.Languages)
            {
                if (language.Disabled ||
                    language.Default ||
                    !language.RenderAtRoot ||
                    string.IsNullOrWhiteSpace(language.BaseUrl) ||
                    !Uri.TryCreate(language.BaseUrl, UriKind.Absolute, out var baseUri))
                {
                    continue;
                }

                var host = NormalizeSeoDoctorHostKey(baseUri);
                var prefix = NormalizeSeoDoctorLanguagePrefix(
                    !string.IsNullOrWhiteSpace(language.Prefix)
                        ? language.Prefix
                        : language.Code);
                if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(prefix))
                    continue;
                hosts.TryAdd(host, prefix);
            }
        }

        return hosts;
    }

    private static string NormalizeSeoDoctorHostKey(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return string.Empty;

        var trimmed = host.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return NormalizeSeoDoctorHostKey(uri);

        return trimmed.TrimEnd('/').ToLowerInvariant();
    }

    private static string NormalizeSeoDoctorHostKey(Uri uri)
    {
        var host = uri.IdnHost.ToLowerInvariant();
        return uri.IsDefaultPort ? host : host + ":" + uri.Port;
    }

    private static string NormalizeSeoDoctorLanguagePrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return string.Empty;

        return prefix.Trim().Trim('/').Replace('\\', '/');
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
            PagesMissingDescription = source.PagesMissingDescription,
            PagesWithShortDescription = source.PagesWithShortDescription,
            PagesWithLongDescription = source.PagesWithLongDescription,
            PagesMissingH1 = source.PagesMissingH1,
            PagesWithMultipleH1 = source.PagesWithMultipleH1,
            PagesWithMissingAlt = source.PagesWithMissingAlt,
            PagesWithEmptyAlt = source.PagesWithEmptyAlt,
            TotalMissingAlt = source.TotalMissingAlt,
            TotalEmptyAlt = source.TotalEmptyAlt,
            SourceMarkdownFilesWithEmptyAlt = source.SourceMarkdownFilesWithEmptyAlt,
            TotalSourceMarkdownEmptyAlt = source.TotalSourceMarkdownEmptyAlt,
            Issues = issues.ToArray(),
            PageMetrics = source.PageMetrics,
            SourceMarkdownMetrics = source.SourceMarkdownMetrics,
            Errors = errors.ToArray(),
            Warnings = warnings.ToArray()
        };
    }

    private static void WriteSeoDoctorBacklogArtifacts(JsonElement step, string baseDir, WebSeoDoctorResult result)
    {
        WriteSeoDoctorJsonFile(step, baseDir, "backlogSummaryPath", "backlog-summary-path", BuildSeoDoctorBacklogSummary(result));
        WriteSeoDoctorCsvFile(step, baseDir, "pageMetricsPath", "page-metrics-path", BuildSeoDoctorPageMetricsCsv(result.PageMetrics));
        WriteSeoDoctorCsvFile(step, baseDir, "issuesCsvPath", "issues-csv-path", BuildSeoDoctorIssuesCsv(result.Issues));
        WriteSeoDoctorCsvFile(step, baseDir, "sourceMarkdownPath", "source-markdown-path", BuildSeoDoctorSourceMarkdownCsv(result.SourceMarkdownMetrics));
    }

    private static void WriteSeoDoctorJsonFile(JsonElement step, string baseDir, string name, string altName, object payload)
    {
        var path = GetString(step, name) ?? GetString(step, altName);
        if (string.IsNullOrWhiteSpace(path))
            return;

        var resolvedPath = ResolvePathWithinRoot(baseDir, path, path);
        var directory = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(resolvedPath, JsonSerializer.Serialize(payload, SeoDoctorIndentedJsonOptions));
    }

    private static void WriteSeoDoctorCsvFile(JsonElement step, string baseDir, string name, string altName, string csv)
    {
        var path = GetString(step, name) ?? GetString(step, altName);
        if (string.IsNullOrWhiteSpace(path))
            return;

        var resolvedPath = ResolvePathWithinRoot(baseDir, path, path);
        var directory = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(resolvedPath, csv);
    }

    private static object BuildSeoDoctorBacklogSummary(WebSeoDoctorResult result)
        => new
        {
            generatedAt = DateTimeOffset.UtcNow.ToString("O"),
            pageCount = result.PageCount,
            pagesMissingDescription = result.PagesMissingDescription,
            pagesWithShortDescription = result.PagesWithShortDescription,
            pagesWithLongDescription = result.PagesWithLongDescription,
            pagesMissingH1 = result.PagesMissingH1,
            pagesWithMultipleH1 = result.PagesWithMultipleH1,
            pagesWithMissingAlt = result.PagesWithMissingAlt,
            pagesWithEmptyAlt = result.PagesWithEmptyAlt,
            totalMissingAlt = result.TotalMissingAlt,
            totalEmptyAlt = result.TotalEmptyAlt,
            sourceMarkdownFilesWithEmptyAlt = result.SourceMarkdownFilesWithEmptyAlt,
            totalSourceMarkdownEmptyAlt = result.TotalSourceMarkdownEmptyAlt,
            topEmptyAltPages = result.PageMetrics
                .Where(static page => page.EmptyAltCount > 0)
                .OrderByDescending(static page => page.EmptyAltCount)
                .ThenBy(static page => page.Path, StringComparer.OrdinalIgnoreCase)
                .Take(25)
                .Select(static page => new { page.Path, page.EmptyAltCount, page.EmptyAltSamples })
                .ToArray(),
            topSourceEmptyAltFiles = result.SourceMarkdownMetrics
                .OrderByDescending(static row => row.EmptyMarkdownAltCount)
                .ThenBy(static row => row.Path, StringComparer.OrdinalIgnoreCase)
                .Take(25)
                .Select(static row => new { row.Path, row.EmptyMarkdownAltCount, row.SampleTargets, row.SampleLineNumbers })
                .ToArray()
        };

    private static string BuildSeoDoctorPageMetricsCsv(IEnumerable<WebSeoDoctorPageMetric> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Path,Title,TitleTagCount,DescriptionLength,MissingDescription,ShortDescription,LongDescription,H1Count,MissingH1,MultipleH1,ImageCount,MissingAltCount,EmptyAltCount,MissingAltSamples,EmptyAltSamples,NoIndex");
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(",", new[]
            {
                Csv(row.Path),
                Csv(row.Title),
                Csv(row.TitleTagCount),
                Csv(row.DescriptionLength),
                Csv(row.MissingDescription),
                Csv(row.ShortDescription),
                Csv(row.LongDescription),
                Csv(row.H1Count),
                Csv(row.MissingH1),
                Csv(row.MultipleH1),
                Csv(row.ImageCount),
                Csv(row.MissingAltCount),
                Csv(row.EmptyAltCount),
                Csv(row.MissingAltSamples),
                Csv(row.EmptyAltSamples),
                Csv(row.NoIndex)
            }));
        }
        return builder.ToString();
    }

    private static string BuildSeoDoctorIssuesCsv(IEnumerable<WebSeoDoctorIssue> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Severity,Category,Path,Detail,Code,Hint");
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(",", new[]
            {
                Csv(row.Severity),
                Csv(row.Category),
                Csv(row.Path ?? string.Empty),
                Csv(row.Message),
                Csv(row.Code),
                Csv(row.Hint)
            }));
        }
        return builder.ToString();
    }

    private static string BuildSeoDoctorSourceMarkdownCsv(IEnumerable<WebSeoDoctorSourceMarkdownMetric> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Path,EmptyMarkdownAltCount,SampleTargets,SampleLineNumbers");
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(",", new[]
            {
                Csv(row.Path),
                Csv(row.EmptyMarkdownAltCount),
                Csv(row.SampleTargets),
                Csv(row.SampleLineNumbers)
            }));
        }
        return builder.ToString();
    }

    private static string Csv(object? value)
    {
        var text = value is bool flag
            ? flag.ToString().ToLowerInvariant()
            : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
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

    private static WebSeoDoctorPageAssertion[] ResolvePageAssertions(JsonElement step)
    {
        var items = GetArrayOfObjects(step, "pageAssertions") ??
                    GetArrayOfObjects(step, "page-assertions");
        if (items is not { Length: > 0 })
            return Array.Empty<WebSeoDoctorPageAssertion>();

        var assertions = new List<WebSeoDoctorPageAssertion>();
        foreach (var item in items)
        {
            var path = GetString(item, "path") ??
                       GetString(item, "page") ??
                       GetString(item, "route");
            if (string.IsNullOrWhiteSpace(path))
                continue;

            assertions.Add(new WebSeoDoctorPageAssertion
            {
                Path = path,
                Label = GetString(item, "label") ?? string.Empty,
                MustExist = GetBool(item, "mustExist") ?? GetBool(item, "must-exist") ?? true,
                Contains = ResolveStringArrayOrSingle(item, "contains"),
                NotContains = ResolveStringArrayOrSingle(item, "notContains", "not-contains"),
                Scope = GetString(item, "scope") ??
                        GetString(item, "matchIn") ??
                        GetString(item, "match-in") ??
                        "body"
            });
        }

        return assertions.ToArray();
    }

    private static string[] ResolveStringArrayOrSingle(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var values = GetArrayOfStrings(element, name);
            if (values is { Length: > 0 })
                return values;

            var single = GetString(element, name);
            if (!string.IsNullOrWhiteSpace(single))
                return new[] { single };
        }

        return Array.Empty<string>();
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
