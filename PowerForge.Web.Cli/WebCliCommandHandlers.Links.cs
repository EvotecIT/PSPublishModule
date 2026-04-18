using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PowerForge.Web;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static int HandleLinks(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        if (subArgs.Length == 0)
            return Fail("Missing links subcommand. Use validate or export-apache.", outputJson, logger, "web.links");

        var action = subArgs[0].Trim().ToLowerInvariant();
        var args = subArgs.Skip(1).ToArray();

        return action switch
        {
            "validate" or "check" => HandleLinksValidate(args, outputJson, logger, outputSchemaVersion),
            "export-apache" or "export" or "apache" => HandleLinksExportApache(args, outputJson, logger, outputSchemaVersion),
            "import-wordpress" or "import-pretty-links" or "import-prettylinks" or "import" => HandleLinksImportWordPress(args, outputJson, logger, outputSchemaVersion),
            "review-404" or "404-review" or "review404" => HandleLinksReview404(args, outputJson, logger, outputSchemaVersion),
            "report-404" or "404-report" or "report404" => HandleLinksReport404(args, outputJson, logger, outputSchemaVersion),
            "promote-404" or "404-promote" or "promote404" => HandleLinksPromote404(args, outputJson, logger, outputSchemaVersion),
            "ignore-404" or "404-ignore" or "ignore404" => HandleLinksIgnore404(args, outputJson, logger, outputSchemaVersion),
            "apply-review" or "apply-candidates" or "review-apply" => HandleLinksApplyReview(args, outputJson, logger, outputSchemaVersion),
            _ => Fail($"Unsupported links subcommand '{subArgs[0]}'. Use validate, export-apache, import-wordpress, review-404, report-404, promote-404, ignore-404, or apply-review.", outputJson, logger, "web.links")
        };
    }

    private static int HandleLinksValidate(string[] args, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var command = "web.links.validate";
        var loaded = LoadLinksSpecForCommand(args, command, outputJson, logger);
        if (!loaded.HasConfig && !HasDirectLinkSources(args))
            return Fail("Specify --config or at least one link source (--redirects, --shortlinks, or --source).", outputJson, logger, command);

        var baseDir = loaded.BaseDir;
        var linkOptions = BuildLinkLoadOptions(args, loaded.Spec, baseDir);
        var dataSet = WebLinkService.Load(linkOptions);
        var strict = HasOption(args, "--strict");
        if (strict && dataSet.UsedSources.Length == 0)
            return Fail("links validate strict mode failed: no link source files were found.", outputJson, logger, command);

        var validation = WebLinkService.Validate(dataSet);
        var failOnWarnings = HasOption(args, "--fail-on-warnings") || HasOption(args, "--failOnWarnings");
        var failOnNewWarnings = HasOption(args, "--fail-on-new-warnings") || HasOption(args, "--failOnNewWarnings") || HasOption(args, "--fail-on-new");
        var baselineGenerate = HasOption(args, "--baseline-generate") || HasOption(args, "--baselineGenerate");
        var baselineUpdate = HasOption(args, "--baseline-update") || HasOption(args, "--baselineUpdate");
        var baselinePath = TryGetOptionValue(args, "--baseline") ??
                           TryGetOptionValue(args, "--baseline-path") ??
                           TryGetOptionValue(args, "--baselinePath");

        var baseline = WebLinkCommandSupport.EvaluateBaseline(baseDir, baselinePath, validation, baselineGenerate, baselineUpdate, failOnNewWarnings);
        var failOnNewWarningsActive = failOnNewWarnings && !baselineGenerate && !baselineUpdate;
        var success = validation.ErrorCount == 0 &&
                      (!failOnWarnings || validation.WarningCount == 0) &&
                      (!failOnNewWarningsActive || (baseline.Loaded && baseline.NewWarnings.Length == 0));
        if (baseline.ShouldWrite)
            baseline.WrittenPath = WebVerifyBaselineStore.Write(baseDir, baseline.Path, baseline.CurrentWarningKeys, baseline.Merge, logger);

        var summaryPath = ResolveOptionalPath(baseDir, TryGetOptionValue(args, "--summary-path") ?? TryGetOptionValue(args, "--summaryPath"));
        var reportPath = ResolveOptionalPath(baseDir, TryGetOptionValue(args, "--report-path") ?? TryGetOptionValue(args, "--reportPath"));
        var duplicateReportPath = ResolveOptionalPath(baseDir, TryGetOptionValue(args, "--duplicate-report-path") ?? TryGetOptionValue(args, "--duplicateReportPath"));
        WebLinkCommandSupport.WriteSummary(summaryPath, "validate", dataSet, validation, success, export: null, baseline);
        WebLinkCommandSupport.WriteIssueReport(reportPath, validation);
        WebLinkCommandSupport.WriteDuplicateReport(duplicateReportPath, validation);

        var message = success
            ? WebLinkCommandSupport.BuildValidateSuccessMessage(validation, baseline)
            : WebLinkCommandSupport.BuildValidateFailureMessage(validation, baseline, failOnNewWarningsActive);

        return CompleteLinksValidation(command, outputJson, logger, outputSchemaVersion, loaded.ConfigPath, validation, success, message, reportPath, duplicateReportPath);
    }

    private static int HandleLinksExportApache(string[] args, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var command = "web.links.export-apache";
        var loaded = LoadLinksSpecForCommand(args, command, outputJson, logger);
        if (!loaded.HasConfig && !HasDirectLinkSources(args))
            return Fail("Specify --config or at least one link source (--redirects, --shortlinks, or --source).", outputJson, logger, command);

        var baseDir = loaded.BaseDir;
        var linkOptions = BuildLinkLoadOptions(args, loaded.Spec, baseDir);
        var dataSet = WebLinkService.Load(linkOptions);
        var strict = HasOption(args, "--strict");
        if (strict && dataSet.UsedSources.Length == 0)
            return Fail("links export-apache strict mode failed: no link source files were found.", outputJson, logger, command);

        var validation = WebLinkService.Validate(dataSet);
        var summaryPath = ResolveOptionalPath(baseDir, TryGetOptionValue(args, "--summary-path") ?? TryGetOptionValue(args, "--summaryPath"));
        var reportPath = ResolveOptionalPath(baseDir, TryGetOptionValue(args, "--report-path") ?? TryGetOptionValue(args, "--reportPath"));
        var duplicateReportPath = ResolveOptionalPath(baseDir, TryGetOptionValue(args, "--duplicate-report-path") ?? TryGetOptionValue(args, "--duplicateReportPath"));
        var skipValidation = HasOption(args, "--skip-validation") || HasOption(args, "--skipValidation");
        if (!skipValidation && validation.ErrorCount > 0)
        {
            WebLinkCommandSupport.WriteSummary(summaryPath, "export-apache", dataSet, validation, taskSuccess: false, export: null, baseline: null);
            WebLinkCommandSupport.WriteIssueReport(reportPath, validation);
            WebLinkCommandSupport.WriteDuplicateReport(duplicateReportPath, validation);
            var message = $"links-export-apache failed validation: errors={validation.ErrorCount}; warnings={validation.WarningCount}";
            return CompleteLinksValidation(command, outputJson, logger, outputSchemaVersion, loaded.ConfigPath, validation, success: false, message, reportPath, duplicateReportPath);
        }

        var outputOption = TryGetOptionValue(args, "--output");
        if (string.Equals(outputOption, "json", StringComparison.OrdinalIgnoreCase))
            outputOption = null;

        var outputPath = ResolveOptionalPath(baseDir,
                             TryGetOptionValue(args, "--out") ??
                             outputOption ??
                             TryGetOptionValue(args, "--output-path") ??
                             TryGetOptionValue(args, "--outputPath") ??
                             TryGetOptionValue(args, "--apache-out") ??
                             TryGetOptionValue(args, "--apacheOut")) ??
                         ResolveOptionalPath(baseDir, loaded.Spec?.ApacheOut) ??
                         Path.GetFullPath(Path.Combine(baseDir, "deploy", "apache", "link-service-redirects.conf"));
        var includeHeader = !HasOption(args, "--no-header");
        var include404 = HasOption(args, "--include-404") ||
                         HasOption(args, "--include-error-document-404") ||
                         HasOption(args, "--includeErrorDocument404");

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

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = command,
                Success = true,
                ExitCode = 0,
                ConfigPath = loaded.ConfigPath,
                Result = WebCliJson.SerializeToElement(export, WebCliJson.Context.WebLinkApacheExportResult)
            });
            return 0;
        }

        logger.Success($"links-export-apache ok: rules={export.RuleCount}; redirects={validation.RedirectCount}; shortlinks={validation.ShortlinkCount}; warnings={validation.WarningCount}");
        logger.Info($"Output: {export.OutputPath}");
        if (!string.IsNullOrWhiteSpace(reportPath))
            logger.Info($"Report: {reportPath}");
        if (!string.IsNullOrWhiteSpace(duplicateReportPath))
            logger.Info($"Duplicate report: {duplicateReportPath}");
        return 0;
    }

    private static int HandleLinksReport404(string[] args, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var command = "web.links.report-404";
        var loaded = LoadLinksSpecForCommand(args, command, outputJson, logger);
        var baseDir = loaded.BaseDir;
        var siteRoot = ResolveOptionalPath(baseDir,
                           TryGetOptionValue(args, "--site-root") ??
                           TryGetOptionValue(args, "--siteRoot") ??
                           TryGetOptionValue(args, "--out-root") ??
                           TryGetOptionValue(args, "--outRoot")) ??
                       Path.GetFullPath(Path.Combine(baseDir, "_site"));
        var sourcePath = ResolveOptionalPath(baseDir,
            TryGetOptionValue(args, "--source") ??
            TryGetOptionValue(args, "--log") ??
            TryGetOptionValue(args, "--input") ??
            TryGetOptionValue(args, "--in"));
        var reportPath = ResolveOptionalPath(baseDir,
                             TryGetOptionValue(args, "--out") ??
                             TryGetOptionValue(args, "--output") ??
                             TryGetOptionValue(args, "--report-path") ??
                             TryGetOptionValue(args, "--reportPath")) ??
                         Path.GetFullPath(Path.Combine(baseDir, "Build", "link-reports", "404-suggestions.json"));
        var reviewCsvPath = ResolveOptionalPath(baseDir,
            TryGetOptionValue(args, "--review-csv") ??
            TryGetOptionValue(args, "--reviewCsv") ??
            TryGetOptionValue(args, "--review-csv-path") ??
            TryGetOptionValue(args, "--reviewCsvPath") ??
            TryGetOptionValue(args, "--csv-report") ??
            TryGetOptionValue(args, "--csvReport"));
        var ignored404Path = ResolveOptionalPath(baseDir,
                                 TryGetOptionValue(args, "--ignored-404") ??
                                 TryGetOptionValue(args, "--ignored404") ??
                                 TryGetOptionValue(args, "--ignored-404-path") ??
                                 TryGetOptionValue(args, "--ignored404Path")) ??
                             ResolveOptionalPath(baseDir, loaded.Spec?.Ignored404);

        var result = WebLinkService.Generate404Report(new WebLink404ReportOptions
        {
            SiteRoot = siteRoot,
            SourcePath = sourcePath,
            Ignored404Path = ignored404Path,
            AllowMissingSource = HasOption(args, "--allow-missing-source") || HasOption(args, "--allowMissingSource"),
            MaxSuggestions = ParseIntOption(TryGetOptionValue(args, "--max-suggestions") ?? TryGetOptionValue(args, "--maxSuggestions"), 3),
            MinimumScore = ParseDoubleOption(TryGetOptionValue(args, "--min-score") ?? TryGetOptionValue(args, "--minimum-score") ?? TryGetOptionValue(args, "--minimumScore"), 0.35d),
            IncludeAsset404s = HasOption(args, "--include-assets") || HasOption(args, "--include-asset-404s")
        });

        WriteLinks404Report(reportPath, result);
        WebLinkCommandSupport.Write404SuggestionReviewCsv(reviewCsvPath, result);

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = command,
                Success = true,
                ExitCode = 0,
                ConfigPath = loaded.ConfigPath,
                Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebLink404ReportResult)
            });
            return 0;
        }

        logger.Success($"links report-404 ok: observations={result.ObservationCount}; ignored={result.IgnoredObservationCount}; suggested={result.SuggestedObservationCount}; routes={result.RouteCount}");
        logger.Info($"Report: {reportPath}");
        if (!string.IsNullOrWhiteSpace(reviewCsvPath))
            logger.Info($"Review CSV: {reviewCsvPath}");
        return 0;
    }

    private static int HandleLinksReview404(string[] args, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var command = "web.links.review-404";
        var loaded = LoadLinksSpecForCommand(args, command, outputJson, logger);
        var baseDir = loaded.BaseDir;
        var siteRoot = ResolveOptionalPath(baseDir,
                           TryGetOptionValue(args, "--site-root") ??
                           TryGetOptionValue(args, "--siteRoot") ??
                           TryGetOptionValue(args, "--out-root") ??
                           TryGetOptionValue(args, "--outRoot")) ??
                       Path.GetFullPath(Path.Combine(baseDir, "_site"));
        var sourcePath = ResolveOptionalPath(baseDir,
            TryGetOptionValue(args, "--source") ??
            TryGetOptionValue(args, "--log") ??
            TryGetOptionValue(args, "--input") ??
            TryGetOptionValue(args, "--in")) ??
            Path.GetFullPath(Path.Combine(baseDir, "Build", "link-reports", "apache-404.log"));
        var ignored404Path = ResolveOptionalPath(baseDir,
                                 TryGetOptionValue(args, "--ignored-404") ??
                                 TryGetOptionValue(args, "--ignored404") ??
                                 TryGetOptionValue(args, "--ignored-404-path") ??
                                 TryGetOptionValue(args, "--ignored404Path")) ??
                             ResolveOptionalPath(baseDir, loaded.Spec?.Ignored404);

        var reportPath = ResolveOptionalPath(baseDir,
                             TryGetOptionValue(args, "--out") ??
                             TryGetOptionValue(args, "--output") ??
                             TryGetOptionValue(args, "--report-path") ??
                             TryGetOptionValue(args, "--reportPath")) ??
                         Path.GetFullPath(Path.Combine(baseDir, "Build", "link-reports", "404-suggestions.json"));
        var reportCsvPath = ResolveOptionalPath(baseDir,
            TryGetOptionValue(args, "--review-csv") ??
            TryGetOptionValue(args, "--reviewCsv") ??
            TryGetOptionValue(args, "--report-csv") ??
            TryGetOptionValue(args, "--reportCsv")) ??
            Path.GetFullPath(Path.Combine(baseDir, "Build", "link-reports", "404-suggestions.csv"));
        var redirectCandidatesPath = ResolveOptionalPath(baseDir,
            TryGetOptionValue(args, "--redirect-candidates") ??
            TryGetOptionValue(args, "--redirectCandidates") ??
            TryGetOptionValue(args, "--redirect-candidates-path") ??
            TryGetOptionValue(args, "--redirectCandidatesPath")) ??
            Path.GetFullPath(Path.Combine(baseDir, "Build", "link-reports", "404-promoted-candidates.json"));
        var redirectCandidatesCsvPath = ResolveOptionalPath(baseDir,
            TryGetOptionValue(args, "--redirect-candidates-csv") ??
            TryGetOptionValue(args, "--redirectCandidatesCsv") ??
            TryGetOptionValue(args, "--promoted-csv") ??
            TryGetOptionValue(args, "--promotedCsv")) ??
            Path.GetFullPath(Path.Combine(baseDir, "Build", "link-reports", "404-promoted-candidates.csv"));
        var ignored404CandidatesPath = ResolveOptionalPath(baseDir,
            TryGetOptionValue(args, "--ignored-404-candidates") ??
            TryGetOptionValue(args, "--ignored404Candidates") ??
            TryGetOptionValue(args, "--ignored-404-candidates-path") ??
            TryGetOptionValue(args, "--ignored404CandidatesPath")) ??
            Path.GetFullPath(Path.Combine(baseDir, "Build", "link-reports", "ignored-404-candidates.json"));
        var ignored404CandidatesCsvPath = ResolveOptionalPath(baseDir,
            TryGetOptionValue(args, "--ignored-404-candidates-csv") ??
            TryGetOptionValue(args, "--ignored404CandidatesCsv") ??
            TryGetOptionValue(args, "--ignored-csv") ??
            TryGetOptionValue(args, "--ignoredCsv")) ??
            Path.GetFullPath(Path.Combine(baseDir, "Build", "link-reports", "ignored-404-candidates.csv"));
        var promoteSummaryPath = ResolveOptionalPath(baseDir,
            TryGetOptionValue(args, "--promote-summary-path") ??
            TryGetOptionValue(args, "--promoteSummaryPath")) ??
            Path.GetFullPath(Path.Combine(baseDir, "Build", "link-reports", "404-promoted-candidates-summary.json"));
        var ignoreSummaryPath = ResolveOptionalPath(baseDir,
            TryGetOptionValue(args, "--ignore-summary-path") ??
            TryGetOptionValue(args, "--ignoreSummaryPath")) ??
            Path.GetFullPath(Path.Combine(baseDir, "Build", "link-reports", "ignored-404-candidates-summary.json"));
        var summaryPath = ResolveOptionalPath(baseDir,
            TryGetOptionValue(args, "--summary-path") ??
            TryGetOptionValue(args, "--summaryPath"));

        var result = WebLinkService.Review404(new WebLink404ReviewOptions
        {
            SiteRoot = siteRoot,
            SourcePath = sourcePath,
            Ignored404Path = ignored404Path,
            AllowMissingSource = HasOption(args, "--allow-missing-source") || HasOption(args, "--allowMissingSource"),
            MaxSuggestions = ParseIntOption(TryGetOptionValue(args, "--max-suggestions") ?? TryGetOptionValue(args, "--maxSuggestions"), 5),
            MinimumScore = ParseDoubleOption(TryGetOptionValue(args, "--min-score") ?? TryGetOptionValue(args, "--minimum-score") ?? TryGetOptionValue(args, "--minimumScore"), 0.35d),
            IncludeAsset404s = HasOption(args, "--include-assets") || HasOption(args, "--include-asset-404s"),
            ReportPath = reportPath,
            RedirectCandidatesPath = redirectCandidatesPath,
            Ignored404CandidatesPath = ignored404CandidatesPath,
            PromoteSummaryPath = promoteSummaryPath,
            IgnoreSummaryPath = ignoreSummaryPath,
            EnableRedirectCandidates = HasOption(args, "--enable") || HasOption(args, "--enabled") || HasOption(args, "--enable-redirects"),
            PromoteMinimumScore = ParseDoubleOption(TryGetOptionValue(args, "--promote-min-score") ?? TryGetOptionValue(args, "--promoteMinimumScore"), 0.65d),
            PromoteMinimumCount = ParseIntOption(TryGetOptionValue(args, "--promote-min-count") ?? TryGetOptionValue(args, "--promoteMinimumCount"), 1),
            PromoteStatus = ParseIntOption(TryGetOptionValue(args, "--status"), 301),
            PromoteGroup = TryGetOptionValue(args, "--group"),
            IgnoreReason = TryGetOptionValue(args, "--reason"),
            CreatedBy = TryGetOptionValue(args, "--created-by") ?? TryGetOptionValue(args, "--createdBy")
        });

        WebLinkCommandSupport.Write404SuggestionReviewCsv(reportCsvPath, result.Report);
        WebLinkCommandSupport.WriteRedirectReviewCsv(redirectCandidatesCsvPath, result.RedirectCandidatesPath);
        WebLinkCommandSupport.WriteIgnored404ReviewCsv(ignored404CandidatesCsvPath, result.Ignored404CandidatesPath);
        WriteLinksReview404Summary(summaryPath, result);

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = command,
                Success = true,
                ExitCode = 0,
                ConfigPath = loaded.ConfigPath,
                Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebLink404ReviewResult)
            });
            return 0;
        }

        logger.Success($"links review-404 ok: observations={result.Report.ObservationCount}; ignored={result.Report.IgnoredObservationCount}; suggested={result.Report.SuggestedObservationCount}; redirectCandidates={result.Promote.CandidateCount}; ignoredCandidates={result.Ignore.CandidateCount}");
        logger.Info($"Report: {result.ReportPath}");
        logger.Info($"Redirect candidates: {result.RedirectCandidatesPath}");
        logger.Info($"Ignored-404 candidates: {result.Ignored404CandidatesPath}");
        return 0;
    }

    private static int HandleLinksPromote404(string[] args, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var command = "web.links.promote-404";
        var loaded = LoadLinksSpecForCommand(args, command, outputJson, logger);
        var baseDir = loaded.BaseDir;
        var sourcePath = ResolveOptionalPath(baseDir,
            TryGetOptionValue(args, "--source") ??
            TryGetOptionValue(args, "--report") ??
            TryGetOptionValue(args, "--input") ??
            TryGetOptionValue(args, "--in"));
        if (string.IsNullOrWhiteSpace(sourcePath))
            return Fail("Missing required --source.", outputJson, logger, command);

        var outputPath = ResolveOptionalPath(baseDir,
                             TryGetOptionValue(args, "--out") ??
                             TryGetOptionValue(args, "--redirects") ??
                             TryGetOptionValue(args, "--redirects-path") ??
                             TryGetOptionValue(args, "--redirectsPath")) ??
                         ResolveOptionalPath(baseDir, loaded.Spec?.Redirects);
        if (string.IsNullOrWhiteSpace(outputPath))
            return Fail("Missing required --out or links.redirects config path.", outputJson, logger, command);
        var reviewCsvPath = ResolveOptionalPath(baseDir,
            TryGetOptionValue(args, "--review-csv") ??
            TryGetOptionValue(args, "--reviewCsv") ??
            TryGetOptionValue(args, "--review-csv-path") ??
            TryGetOptionValue(args, "--reviewCsvPath") ??
            TryGetOptionValue(args, "--csv-report") ??
            TryGetOptionValue(args, "--csvReport"));

        var result = WebLinkService.Promote404Suggestions(new WebLink404PromoteOptions
        {
            SourcePath = sourcePath,
            OutputPath = outputPath,
            Enabled = HasOption(args, "--enable") || HasOption(args, "--enabled"),
            MinimumScore = ParseDoubleOption(TryGetOptionValue(args, "--min-score") ?? TryGetOptionValue(args, "--minimum-score") ?? TryGetOptionValue(args, "--minimumScore"), 0.35d),
            MinimumCount = ParseIntOption(TryGetOptionValue(args, "--min-count") ?? TryGetOptionValue(args, "--minimum-count") ?? TryGetOptionValue(args, "--minimumCount"), 1),
            Status = ParseIntOption(TryGetOptionValue(args, "--status"), 301),
            Group = TryGetOptionValue(args, "--group"),
            MergeWithExisting = !HasOption(args, "--no-merge"),
            ReplaceExisting = HasOption(args, "--replace-existing") || HasOption(args, "--replaceExisting")
        });
        WebLinkCommandSupport.WriteRedirectReviewCsv(reviewCsvPath, result.OutputPath);

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = command,
                Success = true,
                ExitCode = 0,
                ConfigPath = loaded.ConfigPath,
                Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebLink404PromoteResult)
            });
            return 0;
        }

        logger.Success($"links promote-404 ok: candidates={result.CandidateCount}; written={result.WrittenCount}; skippedDuplicates={result.SkippedDuplicateCount}");
        logger.Info($"Output: {result.OutputPath}");
        if (!string.IsNullOrWhiteSpace(reviewCsvPath))
            logger.Info($"Review CSV: {reviewCsvPath}");
        return 0;
    }

    private static int HandleLinksIgnore404(string[] args, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var command = "web.links.ignore-404";
        var loaded = LoadLinksSpecForCommand(args, command, outputJson, logger);
        var baseDir = loaded.BaseDir;
        var sourcePath = ResolveOptionalPath(baseDir,
            TryGetOptionValue(args, "--source") ??
            TryGetOptionValue(args, "--report") ??
            TryGetOptionValue(args, "--input") ??
            TryGetOptionValue(args, "--in"));
        if (string.IsNullOrWhiteSpace(sourcePath))
            return Fail("Missing required --source.", outputJson, logger, command);

        var outputPath = ResolveOptionalPath(baseDir,
                             TryGetOptionValue(args, "--out") ??
                             TryGetOptionValue(args, "--ignored-404") ??
                             TryGetOptionValue(args, "--ignored404") ??
                             TryGetOptionValue(args, "--ignored-404-path") ??
                             TryGetOptionValue(args, "--ignored404Path")) ??
                         ResolveOptionalPath(baseDir, loaded.Spec?.Ignored404);
        if (string.IsNullOrWhiteSpace(outputPath))
            return Fail("Missing required --out or links.ignored404 config path.", outputJson, logger, command);
        var reviewCsvPath = ResolveOptionalPath(baseDir,
            TryGetOptionValue(args, "--review-csv") ??
            TryGetOptionValue(args, "--reviewCsv") ??
            TryGetOptionValue(args, "--review-csv-path") ??
            TryGetOptionValue(args, "--reviewCsvPath") ??
            TryGetOptionValue(args, "--csv-report") ??
            TryGetOptionValue(args, "--csvReport"));

        var paths = ReadOptionList(args, "--path", "--paths").ToArray();
        var includeAll = HasOption(args, "--all");
        var onlyWithoutSuggestions = HasOption(args, "--without-suggestions") || HasOption(args, "--withoutSuggestions");
        if (paths.Length == 0 && !includeAll && !onlyWithoutSuggestions)
            return Fail("Specify at least one --path, --all, or --without-suggestions.", outputJson, logger, command);

        var result = WebLinkService.Ignore404Suggestions(new WebLink404IgnoreOptions
        {
            SourcePath = sourcePath,
            OutputPath = outputPath,
            Paths = paths,
            IncludeAll = includeAll,
            OnlyWithoutSuggestions = onlyWithoutSuggestions,
            Reason = TryGetOptionValue(args, "--reason"),
            CreatedBy = TryGetOptionValue(args, "--created-by") ?? TryGetOptionValue(args, "--createdBy"),
            MergeWithExisting = !HasOption(args, "--no-merge"),
            ReplaceExisting = HasOption(args, "--replace-existing") || HasOption(args, "--replaceExisting")
        });
        WebLinkCommandSupport.WriteIgnored404ReviewCsv(reviewCsvPath, result.OutputPath);

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = command,
                Success = true,
                ExitCode = 0,
                ConfigPath = loaded.ConfigPath,
                Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebLink404IgnoreResult)
            });
            return 0;
        }

        logger.Success($"links ignore-404 ok: candidates={result.CandidateCount}; written={result.WrittenCount}; skippedDuplicates={result.SkippedDuplicateCount}");
        logger.Info($"Output: {result.OutputPath}");
        if (!string.IsNullOrWhiteSpace(reviewCsvPath))
            logger.Info($"Review CSV: {reviewCsvPath}");
        return 0;
    }

    private static int HandleLinksApplyReview(string[] args, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var command = "web.links.apply-review";
        var loaded = LoadLinksSpecForCommand(args, command, outputJson, logger);
        var baseDir = loaded.BaseDir;
        var applyAll = HasOption(args, "--all");
        var applyRedirects = applyAll ||
                             HasOption(args, "--apply-redirects") ||
                             HasOption(args, "--applyRedirects") ||
                             HasOption(args, "--redirect-candidates-only");
        var applyIgnored404 = applyAll ||
                              HasOption(args, "--apply-ignored-404") ||
                              HasOption(args, "--applyIgnored404") ||
                              HasOption(args, "--ignored-404-candidates-only");
        if (!applyRedirects && !applyIgnored404)
            return Fail("Choose at least one target: --apply-redirects, --apply-ignored-404, or --all.", outputJson, logger, command);

        var redirectCandidatesPath = ResolveOptionalPath(baseDir,
            TryGetOptionValue(args, "--redirect-candidates") ??
            TryGetOptionValue(args, "--redirectCandidates") ??
            TryGetOptionValue(args, "--redirect-candidates-path") ??
            TryGetOptionValue(args, "--redirectCandidatesPath")) ??
            Path.GetFullPath(Path.Combine(baseDir, "Build", "link-reports", "404-promoted-candidates.json"));
        var redirectsPath = ResolveOptionalPath(baseDir,
                                TryGetOptionValue(args, "--redirects") ??
                                TryGetOptionValue(args, "--redirects-path") ??
                                TryGetOptionValue(args, "--redirectsPath")) ??
                            ResolveOptionalPath(baseDir, loaded.Spec?.Redirects);
        if (applyRedirects && string.IsNullOrWhiteSpace(redirectsPath))
            return Fail("Missing --redirects or links.redirects config path.", outputJson, logger, command);

        var ignored404CandidatesPath = ResolveOptionalPath(baseDir,
            TryGetOptionValue(args, "--ignored-404-candidates") ??
            TryGetOptionValue(args, "--ignored404Candidates") ??
            TryGetOptionValue(args, "--ignored-404-candidates-path") ??
            TryGetOptionValue(args, "--ignored404CandidatesPath")) ??
            Path.GetFullPath(Path.Combine(baseDir, "Build", "link-reports", "ignored-404-candidates.json"));
        var ignored404Path = ResolveOptionalPath(baseDir,
                                 TryGetOptionValue(args, "--ignored-404") ??
                                 TryGetOptionValue(args, "--ignored404") ??
                                 TryGetOptionValue(args, "--ignored-404-path") ??
                                 TryGetOptionValue(args, "--ignored404Path")) ??
                             ResolveOptionalPath(baseDir, loaded.Spec?.Ignored404);
        if (applyIgnored404 && string.IsNullOrWhiteSpace(ignored404Path))
            return Fail("Missing --ignored-404 or links.ignored404 config path.", outputJson, logger, command);

        var result = WebLinkService.ApplyReviewCandidates(new WebLinkReviewApplyOptions
        {
            ApplyRedirects = applyRedirects,
            ApplyIgnored404 = applyIgnored404,
            RedirectCandidatesPath = redirectCandidatesPath,
            RedirectsPath = redirectsPath,
            Ignored404CandidatesPath = ignored404CandidatesPath,
            Ignored404Path = ignored404Path,
            ReplaceExisting = HasOption(args, "--replace-existing") || HasOption(args, "--replaceExisting"),
            EnableRedirects = HasOption(args, "--enable-redirects") || HasOption(args, "--enableRedirects"),
            DryRun = HasOption(args, "--dry-run") || HasOption(args, "--dryRun") || HasOption(args, "--what-if") || HasOption(args, "--whatIf")
        });

        var summaryPath = ResolveOptionalPath(baseDir,
            TryGetOptionValue(args, "--summary-path") ??
            TryGetOptionValue(args, "--summaryPath"));
        WebLinkCommandSupport.WriteLinksApplyReviewSummary(summaryPath, result);

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = command,
                Success = true,
                ExitCode = 0,
                ConfigPath = loaded.ConfigPath,
                Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebLinkReviewApplyResult)
            });
            return 0;
        }

        var parts = new List<string>();
        if (result.Redirects is not null)
            parts.Add($"redirects={result.Redirects.CandidateCount}; redirectWritten={result.Redirects.WrittenCount}; redirectSkipped={result.Redirects.SkippedDuplicateCount}");
        if (result.Ignored404 is not null)
            parts.Add($"ignored404={result.Ignored404.CandidateCount}; ignoredWritten={result.Ignored404.WrittenCount}; ignoredSkipped={result.Ignored404.SkippedDuplicateCount}");

        var label = result.DryRun ? "links apply-review dry-run ok" : "links apply-review ok";
        logger.Success(parts.Count == 0 ? label : $"{label}: {string.Join("; ", parts)}");
        if (!string.IsNullOrWhiteSpace(summaryPath))
            logger.Info($"Summary: {summaryPath}");
        return 0;
    }

    private static int HandleLinksImportWordPress(string[] args, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var command = "web.links.import-wordpress";
        var loaded = LoadLinksSpecForCommand(args, command, outputJson, logger);
        var sourcePath = TryGetOptionValue(args, "--source") ??
                         TryGetOptionValue(args, "--csv") ??
                         TryGetOptionValue(args, "--input") ??
                         TryGetOptionValue(args, "--in");
        if (string.IsNullOrWhiteSpace(sourcePath))
            return Fail("Missing required --source.", outputJson, logger, command);

        var baseDir = loaded.BaseDir;
        var outPath = ResolveOptionalPath(baseDir,
            TryGetOptionValue(args, "--out") ??
            TryGetOptionValue(args, "--output-path") ??
            TryGetOptionValue(args, "--outputPath") ??
            TryGetOptionValue(args, "--shortlinks") ??
            TryGetOptionValue(args, "--shortlinks-path") ??
            TryGetOptionValue(args, "--shortlinksPath")) ??
            ResolveOptionalPath(baseDir, loaded.Spec?.Shortlinks);
        if (string.IsNullOrWhiteSpace(outPath))
            return Fail("Missing required --out or links.shortlinks config path.", outputJson, logger, command);

        var hosts = BuildLinkHostMap(args, loaded.Spec);
        var host = TryGetOptionValue(args, "--host");
        hosts.TryGetValue("short", out var configuredShortHost);
        if (string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(configuredShortHost))
            host = configuredShortHost;

        var status = ParseIntOption(TryGetOptionValue(args, "--status"), 302);
        var result = WebLinkService.ImportPrettyLinks(new WebLinkShortlinkImportOptions
        {
            SourcePath = ResolvePathRelative(baseDir, sourcePath),
            SourceOriginPath = sourcePath,
            OutputPath = outPath,
            Host = host,
            ShortHost = configuredShortHost,
            PathPrefix = TryGetOptionValue(args, "--path-prefix") ?? TryGetOptionValue(args, "--pathPrefix"),
            Owner = TryGetOptionValue(args, "--owner"),
            Tags = ReadOptionList(args, "--tag", "--tags").ToArray(),
            Status = status <= 0 ? 302 : status,
            AllowExternal = !HasOption(args, "--no-external"),
            MergeWithExisting = !HasOption(args, "--no-merge"),
            ReplaceExisting = HasOption(args, "--replace-existing") || HasOption(args, "--replaceExisting")
        });

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = command,
                Success = true,
                ExitCode = 0,
                ConfigPath = loaded.ConfigPath,
                Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebLinkShortlinkImportResult)
            });
            return 0;
        }

        logger.Success($"links import-wordpress ok: imported={result.ImportedCount}; written={result.WrittenCount}; skippedDuplicates={result.SkippedDuplicateCount}");
        logger.Info($"Output: {result.OutputPath}");
        foreach (var warning in result.Warnings)
            logger.Warn(warning);
        return 0;
    }

}
