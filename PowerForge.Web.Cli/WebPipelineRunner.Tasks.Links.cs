using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecuteLinksValidate(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var summaryPath = ResolvePath(baseDir, GetString(step, "summaryPath") ?? GetString(step, "summary-path"));
        var reportPath = ResolvePath(baseDir, GetString(step, "reportPath") ?? GetString(step, "report-path"));
        var duplicateReportPath = ResolvePath(baseDir, GetString(step, "duplicateReportPath") ?? GetString(step, "duplicate-report-path"));
        var strict = GetBool(step, "strict") ?? false;
        var failOnWarnings = GetBool(step, "failOnWarnings") ?? GetBool(step, "fail-on-warnings") ?? false;
        var failOnNewWarnings = GetBool(step, "failOnNewWarnings") ?? GetBool(step, "fail-on-new-warnings") ?? GetBool(step, "failOnNew") ?? false;
        var baselineGenerate = GetBool(step, "baselineGenerate") ?? GetBool(step, "baseline-generate") ?? false;
        var baselineUpdate = GetBool(step, "baselineUpdate") ?? GetBool(step, "baseline-update") ?? false;
        var baselinePath = GetString(step, "baselinePath") ?? GetString(step, "baseline-path") ?? GetString(step, "baseline");

        var loaded = LoadLinksSpec(step, baseDir);
        var linkOptions = BuildLinkLoadOptions(step, baseDir, loaded);
        var dataSet = WebLinkService.Load(linkOptions);
        if (strict && dataSet.UsedSources.Length == 0)
            throw new InvalidOperationException("links-validate strict mode failed: no link source files were found.");

        var validation = WebLinkService.Validate(dataSet);
        var baseline = WebLinkCommandSupport.EvaluateBaseline(baseDir, baselinePath, validation, baselineGenerate, baselineUpdate, failOnNewWarnings);
        var failOnNewWarningsActive = failOnNewWarnings && !baselineGenerate && !baselineUpdate;
        var success = validation.ErrorCount == 0 &&
                      (!failOnWarnings || validation.WarningCount == 0) &&
                      (!failOnNewWarningsActive || (baseline.Loaded && baseline.NewWarnings.Length == 0));
        if (baseline.ShouldWrite)
            baseline.WrittenPath = WebVerifyBaselineStore.Write(baseDir, baseline.Path, baseline.CurrentWarningKeys, baseline.Merge, logger: null);

        WebLinkCommandSupport.WriteSummary(summaryPath, "validate", dataSet, validation, taskSuccess: success, export: null, baseline);
        WebLinkCommandSupport.WriteIssueReport(reportPath, validation);
        WebLinkCommandSupport.WriteDuplicateReport(duplicateReportPath, validation);

        stepResult.Success = success;
        stepResult.Message = success
            ? WebLinkCommandSupport.BuildValidateSuccessMessage(validation, baseline)
            : WebLinkCommandSupport.BuildValidateFailureMessage(validation, baseline, failOnNewWarningsActive);
    }

    private static void ExecuteLinksExportApache(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var summaryPath = ResolvePath(baseDir, GetString(step, "summaryPath") ?? GetString(step, "summary-path"));
        var reportPath = ResolvePath(baseDir, GetString(step, "reportPath") ?? GetString(step, "report-path"));
        var duplicateReportPath = ResolvePath(baseDir, GetString(step, "duplicateReportPath") ?? GetString(step, "duplicate-report-path"));
        var strict = GetBool(step, "strict") ?? false;
        var skipValidation = GetBool(step, "skipValidation") ?? GetBool(step, "skip-validation") ?? false;
        var includeHeader = GetBool(step, "includeHeader") ?? GetBool(step, "include-header") ?? true;
        var include404 = GetBool(step, "includeErrorDocument404") ?? GetBool(step, "include-error-document-404") ?? false;

        var loaded = LoadLinksSpec(step, baseDir);
        var linkOptions = BuildLinkLoadOptions(step, baseDir, loaded);
        var dataSet = WebLinkService.Load(linkOptions);
        if (strict && dataSet.UsedSources.Length == 0)
            throw new InvalidOperationException("links-export-apache strict mode failed: no link source files were found.");

        var validation = WebLinkService.Validate(dataSet);
        if (!skipValidation && validation.ErrorCount > 0)
        {
            WebLinkCommandSupport.WriteSummary(summaryPath, "export-apache", dataSet, validation, taskSuccess: false, export: null, baseline: null);
            WebLinkCommandSupport.WriteIssueReport(reportPath, validation);
            WebLinkCommandSupport.WriteDuplicateReport(duplicateReportPath, validation);
            stepResult.Success = false;
            stepResult.Message = $"links-export-apache failed validation: errors={validation.ErrorCount}; warnings={validation.WarningCount}";
            return;
        }

        var outputPath = ResolvePath(baseDir,
            GetString(step, "out") ??
            GetString(step, "output") ??
            GetString(step, "outputPath") ??
            GetString(step, "output-path") ??
            GetString(step, "apacheOut") ??
            GetString(step, "apache-out")) ??
            ResolvePath(loaded.BaseDir ?? baseDir, loaded.Spec?.ApacheOut) ??
            Path.GetFullPath(Path.Combine(baseDir, "deploy", "apache", "link-service-redirects.conf"));

        var export = WebLinkService.ExportApache(dataSet, new WebLinkApacheExportOptions
        {
            OutputPath = outputPath,
            IncludeHeader = includeHeader,
            IncludeErrorDocument404 = include404,
            Hosts = linkOptions.Hosts,
            LanguageRootHosts = linkOptions.LanguageRootHosts
        });

        WebLinkCommandSupport.WriteSummary(summaryPath, "export-apache", dataSet, validation, taskSuccess: true, export, baseline: null);
        WebLinkCommandSupport.WriteIssueReport(reportPath, validation);
        WebLinkCommandSupport.WriteDuplicateReport(duplicateReportPath, validation);

        stepResult.Success = true;
        stepResult.Message = $"links-export-apache ok: rules={export.RuleCount}; redirects={validation.RedirectCount}; shortlinks={validation.ShortlinkCount}; warnings={validation.WarningCount}";
    }

    private static void ExecuteLinksImportWordPress(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var loaded = LoadLinksSpec(step, baseDir);
        var links = loaded.Spec;
        var linkBaseDir = loaded.BaseDir ?? baseDir;
        var sourcePathValue = GetString(step, "source") ??
                              GetString(step, "csv") ??
                              GetString(step, "input") ??
                              GetString(step, "in");
        var sourcePath = ResolvePath(baseDir, sourcePathValue);
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new InvalidOperationException("links-import-wordpress requires source.");

        var outputPath = ResolvePath(baseDir,
                             GetString(step, "out") ??
                             GetString(step, "output") ??
                             GetString(step, "outputPath") ??
                             GetString(step, "output-path") ??
                             GetString(step, "shortlinks") ??
                             GetString(step, "shortlinksPath") ??
                             GetString(step, "shortlinks-path")) ??
                         ResolvePath(linkBaseDir, links?.Shortlinks);
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new InvalidOperationException("links-import-wordpress requires out or links.shortlinks config.");

        var hosts = BuildLinksHostMap(step, links);
        var host = GetString(step, "host");
        if (string.IsNullOrWhiteSpace(host) && hosts.TryGetValue("short", out var shortHost))
            host = shortHost;

        var result = WebLinkService.ImportPrettyLinks(new WebLinkShortlinkImportOptions
        {
            SourcePath = sourcePath,
            SourceOriginPath = sourcePathValue,
            OutputPath = outputPath,
            Host = host,
            PathPrefix = GetString(step, "pathPrefix") ?? GetString(step, "path-prefix"),
            Owner = GetString(step, "owner"),
            Tags = GetArrayOfStrings(step, "tags") ?? GetArrayOfStrings(step, "tag") ?? Array.Empty<string>(),
            Status = GetInt(step, "status") ?? 302,
            AllowExternal = !(GetBool(step, "allowExternal") == false || GetBool(step, "allow-external") == false),
            MergeWithExisting = !(GetBool(step, "merge") == false || GetBool(step, "mergeWithExisting") == false || GetBool(step, "merge-with-existing") == false),
            ReplaceExisting = GetBool(step, "replaceExisting") ?? GetBool(step, "replace-existing") ?? false
        });

        var summaryPath = ResolvePath(baseDir, GetString(step, "summaryPath") ?? GetString(step, "summary-path"));
        WriteLinksImportSummary(summaryPath, result);

        stepResult.Success = true;
        stepResult.Message = $"links-import-wordpress ok: imported={result.ImportedCount}; written={result.WrittenCount}; skippedDuplicates={result.SkippedDuplicateCount}";
    }

    private static void ExecuteLinksReport404(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var loaded = LoadLinksSpec(step, baseDir);
        var links = loaded.Spec;
        var linkBaseDir = loaded.BaseDir ?? baseDir;
        var siteRoot = ResolvePath(baseDir,
                           GetString(step, "siteRoot") ??
                           GetString(step, "site-root") ??
                           GetString(step, "outRoot") ??
                           GetString(step, "out-root")) ??
                       Path.GetFullPath(Path.Combine(baseDir, "_site"));
        var sourcePath = ResolvePath(baseDir,
            GetString(step, "source") ??
            GetString(step, "log") ??
            GetString(step, "input") ??
            GetString(step, "in"));
        var reportPath = ResolvePath(baseDir,
                             GetString(step, "out") ??
                             GetString(step, "output") ??
                             GetString(step, "reportPath") ??
                             GetString(step, "report-path")) ??
                         Path.GetFullPath(Path.Combine(baseDir, "Build", "link-reports", "404-suggestions.json"));
        var reviewCsvPath = ResolvePath(baseDir,
            GetString(step, "reviewCsv") ??
            GetString(step, "review-csv") ??
            GetString(step, "reviewCsvPath") ??
            GetString(step, "review-csv-path") ??
            GetString(step, "csvReport") ??
            GetString(step, "csv-report"));
        var ignored404Path = ResolvePath(baseDir,
                                 GetString(step, "ignored404") ??
                                 GetString(step, "ignored-404") ??
                                 GetString(step, "ignored404Path") ??
                                 GetString(step, "ignored-404-path")) ??
                             ResolvePath(linkBaseDir, links?.Ignored404);

        var result = WebLinkService.Generate404Report(new WebLink404ReportOptions
        {
            SiteRoot = siteRoot,
            SourcePath = sourcePath,
            Ignored404Path = ignored404Path,
            AllowMissingSource = GetBool(step, "allowMissingSource") ?? GetBool(step, "allow-missing-source") ?? false,
            MaxSuggestions = GetInt(step, "maxSuggestions") ?? GetInt(step, "max-suggestions") ?? 3,
            MinimumScore = GetDouble(step, "minimumScore") ?? GetDouble(step, "minimum-score") ?? GetDouble(step, "minScore") ?? GetDouble(step, "min-score") ?? 0.35d,
            IncludeAsset404s = GetBool(step, "includeAsset404s") ?? GetBool(step, "include-asset-404s") ?? GetBool(step, "includeAssets") ?? GetBool(step, "include-assets") ?? false
        });

        WriteLinks404Report(reportPath, result);
        WebLinkCommandSupport.Write404SuggestionReviewCsv(reviewCsvPath, result);

        stepResult.Success = true;
        stepResult.Message = $"links-report-404 ok: observations={result.ObservationCount}; ignored={result.IgnoredObservationCount}; suggested={result.SuggestedObservationCount}; routes={result.RouteCount}";
    }

    private static void ExecuteLinksPromote404(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var loaded = LoadLinksSpec(step, baseDir);
        var links = loaded.Spec;
        var linkBaseDir = loaded.BaseDir ?? baseDir;
        var sourcePath = ResolvePath(baseDir,
            GetString(step, "source") ??
            GetString(step, "report") ??
            GetString(step, "input") ??
            GetString(step, "in"));
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new InvalidOperationException("links-promote-404 requires source.");

        var outputPath = ResolvePath(baseDir,
                             GetString(step, "out") ??
                             GetString(step, "output") ??
                             GetString(step, "outputPath") ??
                             GetString(step, "output-path") ??
                             GetString(step, "redirects") ??
                             GetString(step, "redirectsPath") ??
                             GetString(step, "redirects-path")) ??
                         ResolvePath(linkBaseDir, links?.Redirects);
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new InvalidOperationException("links-promote-404 requires out or links.redirects config.");
        var reviewCsvPath = ResolvePath(baseDir,
            GetString(step, "reviewCsv") ??
            GetString(step, "review-csv") ??
            GetString(step, "reviewCsvPath") ??
            GetString(step, "review-csv-path") ??
            GetString(step, "csvReport") ??
            GetString(step, "csv-report"));

        var result = WebLinkService.Promote404Suggestions(new WebLink404PromoteOptions
        {
            SourcePath = sourcePath,
            OutputPath = outputPath,
            Enabled = GetBool(step, "enabled") ?? GetBool(step, "enable") ?? false,
            MinimumScore = GetDouble(step, "minimumScore") ?? GetDouble(step, "minimum-score") ?? GetDouble(step, "minScore") ?? GetDouble(step, "min-score") ?? 0.35d,
            MinimumCount = GetInt(step, "minimumCount") ?? GetInt(step, "minimum-count") ?? GetInt(step, "minCount") ?? GetInt(step, "min-count") ?? 1,
            Status = GetInt(step, "status") ?? 301,
            Group = GetString(step, "group"),
            MergeWithExisting = !(GetBool(step, "merge") == false || GetBool(step, "mergeWithExisting") == false || GetBool(step, "merge-with-existing") == false),
            ReplaceExisting = GetBool(step, "replaceExisting") ?? GetBool(step, "replace-existing") ?? false
        });

        var summaryPath = ResolvePath(baseDir, GetString(step, "summaryPath") ?? GetString(step, "summary-path"));
        WriteLinksPromoteSummary(summaryPath, result);
        WebLinkCommandSupport.WriteRedirectReviewCsv(reviewCsvPath, result.OutputPath);

        stepResult.Success = true;
        stepResult.Message = $"links-promote-404 ok: candidates={result.CandidateCount}; written={result.WrittenCount}; skippedDuplicates={result.SkippedDuplicateCount}";
    }

    private static void ExecuteLinksIgnore404(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var loaded = LoadLinksSpec(step, baseDir);
        var links = loaded.Spec;
        var linkBaseDir = loaded.BaseDir ?? baseDir;
        var sourcePath = ResolvePath(baseDir,
            GetString(step, "source") ??
            GetString(step, "report") ??
            GetString(step, "input") ??
            GetString(step, "in"));
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new InvalidOperationException("links-ignore-404 requires source.");

        var outputPath = ResolvePath(baseDir,
                             GetString(step, "out") ??
                             GetString(step, "output") ??
                             GetString(step, "outputPath") ??
                             GetString(step, "output-path") ??
                             GetString(step, "ignored404") ??
                             GetString(step, "ignored-404") ??
                             GetString(step, "ignored404Path") ??
                             GetString(step, "ignored-404-path")) ??
                         ResolvePath(linkBaseDir, links?.Ignored404);
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new InvalidOperationException("links-ignore-404 requires out or links.ignored404 config.");
        var reviewCsvPath = ResolvePath(baseDir,
            GetString(step, "reviewCsv") ??
            GetString(step, "review-csv") ??
            GetString(step, "reviewCsvPath") ??
            GetString(step, "review-csv-path") ??
            GetString(step, "csvReport") ??
            GetString(step, "csv-report"));

        var paths = GetStringOrArrayOfStrings(step, "paths", "path");
        var includeAll = GetBool(step, "all") ?? false;
        var onlyWithoutSuggestions = GetBool(step, "withoutSuggestions") ?? GetBool(step, "without-suggestions") ?? false;
        if (paths.Length == 0 && !includeAll && !onlyWithoutSuggestions)
            throw new InvalidOperationException("links-ignore-404 requires paths, all:true, or withoutSuggestions:true.");

        var result = WebLinkService.Ignore404Suggestions(new WebLink404IgnoreOptions
        {
            SourcePath = sourcePath,
            OutputPath = outputPath,
            Paths = paths,
            IncludeAll = includeAll,
            OnlyWithoutSuggestions = onlyWithoutSuggestions,
            Reason = GetString(step, "reason"),
            CreatedBy = GetString(step, "createdBy") ?? GetString(step, "created-by"),
            MergeWithExisting = !(GetBool(step, "merge") == false || GetBool(step, "mergeWithExisting") == false || GetBool(step, "merge-with-existing") == false),
            ReplaceExisting = GetBool(step, "replaceExisting") ?? GetBool(step, "replace-existing") ?? false
        });

        var summaryPath = ResolvePath(baseDir, GetString(step, "summaryPath") ?? GetString(step, "summary-path"));
        WriteLinksIgnoreSummary(summaryPath, result);
        WebLinkCommandSupport.WriteIgnored404ReviewCsv(reviewCsvPath, result.OutputPath);

        stepResult.Success = true;
        stepResult.Message = $"links-ignore-404 ok: candidates={result.CandidateCount}; written={result.WrittenCount}; skippedDuplicates={result.SkippedDuplicateCount}";
    }

    private static WebLinkLoadOptions BuildLinkLoadOptions(
        JsonElement step,
        string baseDir,
        (LinkServiceSpec? Spec, string? BaseDir)? loadedSpec = null)
    {
        var loaded = loadedSpec ?? LoadLinksSpec(step, baseDir);
        var links = loaded.Spec;
        var linkBaseDir = loaded.BaseDir ?? baseDir;

        var redirectsPath = ResolvePathForLinks(baseDir, linkBaseDir,
            GetString(step, "redirects") ??
            GetString(step, "redirectsPath") ??
            GetString(step, "redirects-path"),
            links?.Redirects);

        var shortlinksPath = ResolvePathForLinks(baseDir, linkBaseDir,
            GetString(step, "shortlinks") ??
            GetString(step, "shortlinksPath") ??
            GetString(step, "shortlinks-path"),
            links?.Shortlinks);

        var csvSources = GetArrayOfStrings(step, "sources") ??
                         GetArrayOfStrings(step, "redirectCsvPaths") ??
                         GetArrayOfStrings(step, "redirect-csv-paths") ??
                         GetArrayOfStrings(step, "csvSources") ??
                         GetArrayOfStrings(step, "csv-sources");

        var csvPaths = new List<string>();
        if (csvSources is { Length: > 0 })
        {
            foreach (var value in csvSources.Where(static value => !string.IsNullOrWhiteSpace(value)))
            {
                var resolved = ResolvePath(baseDir, value);
                if (!string.IsNullOrWhiteSpace(resolved))
                    csvPaths.Add(resolved);
            }
        }
        else if (links?.RedirectCsvPaths is { Length: > 0 })
        {
            foreach (var value in links.RedirectCsvPaths.Where(static value => !string.IsNullOrWhiteSpace(value)))
            {
                var resolved = ResolvePath(linkBaseDir, value);
                if (!string.IsNullOrWhiteSpace(resolved))
                    csvPaths.Add(resolved);
            }
        }

        return new WebLinkLoadOptions
        {
            RedirectsPath = redirectsPath,
            ShortlinksPath = shortlinksPath,
            RedirectCsvPaths = csvPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Hosts = BuildLinksHostMap(step, links),
            LanguageRootHosts = BuildLinksLanguageRootHostMap(step, links)
        };
    }

    private static (LinkServiceSpec? Spec, string? BaseDir) LoadLinksSpec(JsonElement step, string baseDir)
    {
        var configValue = GetString(step, "config");
        var configPath = ResolvePath(baseDir, configValue);
        if (string.IsNullOrWhiteSpace(configPath))
            return (null, null);

        if (!File.Exists(configPath))
            throw new InvalidOperationException($"links config file not found: {configPath}");

        var (siteSpec, siteSpecPath) = WebSiteSpecLoader.LoadWithPath(configPath, WebCliJson.Options);
        return (siteSpec.Links, Path.GetDirectoryName(siteSpecPath) ?? baseDir);
    }

    private static string? ResolvePathForLinks(string stepBaseDir, string configBaseDir, string? stepValue, string? configValue)
    {
        if (!string.IsNullOrWhiteSpace(stepValue))
            return ResolvePath(stepBaseDir, stepValue);
        return string.IsNullOrWhiteSpace(configValue) ? null : ResolvePath(configBaseDir, configValue);
    }

    private static IReadOnlyDictionary<string, string> BuildLinksHostMap(JsonElement step, LinkServiceSpec? links)
    {
        var hosts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (links?.Hosts is not null)
        {
            foreach (var pair in links.Hosts)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                    hosts[pair.Key] = pair.Value;
            }
        }

        if ((step.TryGetProperty("hosts", out var hostsElement) ||
             step.TryGetProperty("hostMap", out hostsElement) ||
             step.TryGetProperty("host-map", out hostsElement)) &&
            hostsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in hostsElement.EnumerateObject())
            {
                var value = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : property.Value.ToString();
                if (!string.IsNullOrWhiteSpace(property.Name) && !string.IsNullOrWhiteSpace(value))
                    hosts[property.Name] = value.Trim();
            }
        }

        return hosts;
    }

    private static IReadOnlyDictionary<string, string> BuildLinksLanguageRootHostMap(JsonElement step, LinkServiceSpec? links)
    {
        var hosts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (links?.LanguageRootHosts is not null)
        {
            foreach (var pair in links.LanguageRootHosts)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                    hosts[pair.Key.Trim()] = pair.Value.Trim().Trim('/');
            }
        }

        if ((step.TryGetProperty("languageRootHosts", out var hostsElement) ||
             step.TryGetProperty("language-root-hosts", out hostsElement)) &&
            hostsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in hostsElement.EnumerateObject())
            {
                var value = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : property.Value.ToString();
                if (!string.IsNullOrWhiteSpace(property.Name) && !string.IsNullOrWhiteSpace(value))
                    hosts[property.Name.Trim()] = value.Trim().Trim('/');
            }
        }

        return hosts;
    }

    private static void WriteLinksImportSummary(string? summaryPath, WebLinkShortlinkImportResult result)
    {
        if (string.IsNullOrWhiteSpace(summaryPath))
            return;

        var summaryDirectory = Path.GetDirectoryName(summaryPath);
        if (!string.IsNullOrWhiteSpace(summaryDirectory))
            Directory.CreateDirectory(summaryDirectory);

        File.WriteAllText(summaryPath, JsonSerializer.Serialize(result, LinksSummaryJsonContext.WebLinkShortlinkImportResult));
    }

    private static void WriteLinks404Report(string reportPath, WebLink404ReportResult result)
    {
        var directory = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(reportPath, JsonSerializer.Serialize(result, LinksSummaryJsonContext.WebLink404ReportResult));
    }

    private static void WriteLinksPromoteSummary(string? summaryPath, WebLink404PromoteResult result)
    {
        if (string.IsNullOrWhiteSpace(summaryPath))
            return;

        var summaryDirectory = Path.GetDirectoryName(summaryPath);
        if (!string.IsNullOrWhiteSpace(summaryDirectory))
            Directory.CreateDirectory(summaryDirectory);

        File.WriteAllText(summaryPath, JsonSerializer.Serialize(result, LinksSummaryJsonContext.WebLink404PromoteResult));
    }

    private static void WriteLinksIgnoreSummary(string? summaryPath, WebLink404IgnoreResult result)
    {
        if (string.IsNullOrWhiteSpace(summaryPath))
            return;

        var summaryDirectory = Path.GetDirectoryName(summaryPath);
        if (!string.IsNullOrWhiteSpace(summaryDirectory))
            Directory.CreateDirectory(summaryDirectory);

        File.WriteAllText(summaryPath, JsonSerializer.Serialize(result, LinksSummaryJsonContext.WebLink404IgnoreResult));
    }

    private static string[] GetStringOrArrayOfStrings(JsonElement step, params string[] names)
    {
        foreach (var name in names)
        {
            var array = GetArrayOfStrings(step, name);
            if (array is { Length: > 0 })
                return array;

            var value = GetString(step, name);
            if (!string.IsNullOrWhiteSpace(value))
                return new[] { value };
        }

        return Array.Empty<string>();
    }

    private static readonly PowerForgeWebCliJsonContext LinksSummaryJsonContext = new(new JsonSerializerOptions(WebCliJson.Options)
    {
        WriteIndented = true
    });
}
