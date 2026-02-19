using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using PowerForge;
using PowerForge.Web;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    internal static int HandlePublish(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var configPath = TryGetOptionValue(subArgs, "--config");
        if (string.IsNullOrWhiteSpace(configPath))
            return Fail("Missing required --config.", outputJson, logger, "web.publish");

        var fullConfigPath = ResolveExistingFilePath(configPath);
        var publishSpec = JsonSerializer.Deserialize<WebPublishSpec>(
            File.ReadAllText(fullConfigPath), WebCliJson.Options);
        if (publishSpec is null)
            return Fail("Invalid publish spec.", outputJson, logger, "web.publish");

        if (string.IsNullOrWhiteSpace(publishSpec.Build.Config) || string.IsNullOrWhiteSpace(publishSpec.Build.Out))
            return Fail("Publish spec requires Build.Config and Build.Out.", outputJson, logger, "web.publish");
        if (string.IsNullOrWhiteSpace(publishSpec.Publish.Project) || string.IsNullOrWhiteSpace(publishSpec.Publish.Out))
            return Fail("Publish spec requires Publish.Project and Publish.Out.", outputJson, logger, "web.publish");

        var baseDir = Path.GetDirectoryName(fullConfigPath) ?? ".";
        var buildConfig = ResolvePathRelative(baseDir, publishSpec.Build.Config);
        var buildOut = ResolvePathRelative(baseDir, publishSpec.Build.Out);

        var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(buildConfig, WebCliJson.Options);
        var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);
        if (publishSpec.Build.Clean)
            WebCliFileSystem.CleanOutputDirectory(buildOut);
        _ = WebSiteBuilder.Build(spec, plan, buildOut, WebCliJson.Options);

        int? overlayCopied = null;
        if (publishSpec.Overlay is not null)
        {
            if (string.IsNullOrWhiteSpace(publishSpec.Overlay.Source) ||
                string.IsNullOrWhiteSpace(publishSpec.Overlay.Destination))
                return Fail("Overlay requires Source and Destination.", outputJson, logger, "web.publish");

            var overlay = WebStaticOverlay.Apply(new WebStaticOverlayOptions
            {
                SourceRoot = ResolvePathRelative(baseDir, publishSpec.Overlay.Source),
                DestinationRoot = ResolvePathRelative(baseDir, publishSpec.Overlay.Destination),
                Include = publishSpec.Overlay.Include ?? Array.Empty<string>(),
                Exclude = publishSpec.Overlay.Exclude ?? Array.Empty<string>()
            });
            overlayCopied = overlay.CopiedCount;
        }

        var publishOut = ResolvePathRelative(baseDir, publishSpec.Publish.Out);
        var publishResult = WebDotNetRunner.Publish(new WebDotNetPublishOptions
        {
            ProjectPath = ResolvePathRelative(baseDir, publishSpec.Publish.Project),
            OutputPath = publishOut,
            Configuration = publishSpec.Publish.Configuration,
            Framework = publishSpec.Publish.Framework,
            Runtime = publishSpec.Publish.Runtime,
            SelfContained = publishSpec.Publish.SelfContained,
            NoBuild = publishSpec.Publish.NoBuild,
            NoRestore = publishSpec.Publish.NoRestore,
            DefineConstants = publishSpec.Publish.DefineConstants
        });

        if (!publishResult.Success)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(publishResult.Error)
                ? "dotnet publish failed."
                : publishResult.Error);

        if (publishSpec.Publish.ApplyBlazorFixes)
        {
            WebBlazorPublishFixer.Apply(new WebBlazorPublishFixOptions
            {
                PublishRoot = publishOut,
                BaseHref = publishSpec.Publish.BaseHref
            });
        }

        int? optimizeUpdated = null;
        if (publishSpec.Optimize is not null)
        {
            var optimizeRoot = string.IsNullOrWhiteSpace(publishSpec.Optimize.SiteRoot)
                ? publishOut
                : ResolvePathRelative(baseDir, publishSpec.Optimize.SiteRoot);

            AssetPolicySpec? policy = null;
            if (!string.IsNullOrWhiteSpace(publishSpec.Optimize.Config))
            {
                var optimizeConfigPath = ResolvePathRelative(baseDir, publishSpec.Optimize.Config);
                var (optimizeSpec, _) = WebSiteSpecLoader.LoadWithPath(optimizeConfigPath, WebCliJson.Options);
                policy = optimizeSpec.AssetPolicy;
            }

            if (publishSpec.Optimize.CacheHeaders)
            {
                policy ??= new AssetPolicySpec();
                policy.CacheHeaders ??= new CacheHeadersSpec { Enabled = true };
                policy.CacheHeaders.Enabled = true;
                if (!string.IsNullOrWhiteSpace(publishSpec.Optimize.CacheHeadersOut))
                    policy.CacheHeaders.OutputPath = publishSpec.Optimize.CacheHeadersOut;
                if (!string.IsNullOrWhiteSpace(publishSpec.Optimize.CacheHeadersHtml))
                    policy.CacheHeaders.HtmlCacheControl = publishSpec.Optimize.CacheHeadersHtml;
                if (!string.IsNullOrWhiteSpace(publishSpec.Optimize.CacheHeadersAssets))
                    policy.CacheHeaders.ImmutableCacheControl = publishSpec.Optimize.CacheHeadersAssets;
                if (publishSpec.Optimize.CacheHeadersPaths is { Length: > 0 })
                    policy.CacheHeaders.ImmutablePaths = publishSpec.Optimize.CacheHeadersPaths;
            }

            var optimizerOptions = new WebAssetOptimizerOptions
            {
                SiteRoot = optimizeRoot,
                CriticalCssPath = string.IsNullOrWhiteSpace(publishSpec.Optimize.CriticalCss)
                    ? null
                    : ResolvePathRelative(baseDir, publishSpec.Optimize.CriticalCss),
                MinifyHtml = publishSpec.Optimize.MinifyHtml,
                MinifyCss = publishSpec.Optimize.MinifyCss,
                MinifyJs = publishSpec.Optimize.MinifyJs,
                OptimizeImages = publishSpec.Optimize.OptimizeImages,
                ImageExtensions = publishSpec.Optimize.ImageExtensions is { Length: > 0 }
                    ? publishSpec.Optimize.ImageExtensions
                    : new[] { ".png", ".jpg", ".jpeg", ".webp" },
                ImageInclude = publishSpec.Optimize.ImageInclude ?? Array.Empty<string>(),
                ImageExclude = publishSpec.Optimize.ImageExclude ?? Array.Empty<string>(),
                ImageQuality = publishSpec.Optimize.ImageQuality ?? 82,
                ImageStripMetadata = publishSpec.Optimize.ImageStripMetadata ?? true,
                ImageGenerateWebp = publishSpec.Optimize.ImageGenerateWebp,
                ImageGenerateAvif = publishSpec.Optimize.ImageGenerateAvif,
                ImagePreferNextGen = publishSpec.Optimize.ImagePreferNextGen,
                ResponsiveImageWidths = publishSpec.Optimize.ResponsiveImageWidths ?? Array.Empty<int>(),
                EnhanceImageTags = publishSpec.Optimize.EnhanceImageTags,
                ImageMaxBytesPerFile = publishSpec.Optimize.ImageMaxBytesPerFile ?? 0,
                ImageMaxTotalBytes = publishSpec.Optimize.ImageMaxTotalBytes ?? 0,
                HashAssets = publishSpec.Optimize.HashAssets,
                HashExtensions = publishSpec.Optimize.HashExtensions is { Length: > 0 } ? publishSpec.Optimize.HashExtensions : new[] { ".css", ".js" },
                HashExclude = publishSpec.Optimize.HashExclude ?? Array.Empty<string>(),
                HashManifestPath = publishSpec.Optimize.HashManifest,
                AssetPolicy = policy
            };
            if (!string.IsNullOrWhiteSpace(publishSpec.Optimize.CssPattern))
                optimizerOptions.CssLinkPattern = publishSpec.Optimize.CssPattern;

            var optimizeResult = WebAssetOptimizer.OptimizeDetailed(optimizerOptions);
            if (publishSpec.Optimize.ImageFailOnBudget && optimizeResult.ImageBudgetExceeded)
                throw new InvalidOperationException($"Image budget exceeded: {string.Join(" | ", optimizeResult.ImageBudgetWarnings)}");
            optimizeUpdated = optimizeResult.UpdatedCount;
        }

        var publishSummary = new WebPublishResult
        {
            Success = true,
            BuildOutputPath = buildOut,
            OverlayCopiedCount = overlayCopied,
            PublishOutputPath = publishOut,
            OptimizeUpdatedCount = optimizeUpdated
        };

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.publish",
                Success = true,
                ExitCode = 0,
                Config = "web.publish",
                ConfigPath = fullConfigPath,
                Spec = WebCliJson.SerializeToElement(publishSpec, WebCliJson.Context.WebPublishSpec),
                Result = WebCliJson.SerializeToElement(publishSummary, WebCliJson.Context.WebPublishResult)
            });
            return 0;
        }

        logger.Success("Web publish completed.");
        logger.Info($"Build output: {buildOut}");
        if (overlayCopied.HasValue) logger.Info($"Overlay copied: {overlayCopied.Value}");
        logger.Info($"Publish output: {publishOut}");
        if (optimizeUpdated.HasValue) logger.Info($"Optimize updated: {optimizeUpdated.Value}");
        return 0;
    }

    internal static int HandleDoctor(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var configPath = TryGetOptionValue(subArgs, "--config");
        if (string.IsNullOrWhiteSpace(configPath))
            return Fail("Missing required --config.", outputJson, logger, "web.doctor");

        var siteRootArg = TryGetOptionValue(subArgs, "--site-root") ??
                          TryGetOptionValue(subArgs, "--root") ??
                          TryGetOptionValue(subArgs, "--path");
        var outPath = TryGetOptionValue(subArgs, "--out") ??
                      TryGetOptionValue(subArgs, "--out-path") ??
                      TryGetOptionValue(subArgs, "--output-path");
        var runBuild = !HasOption(subArgs, "--no-build");
        var runVerify = !HasOption(subArgs, "--no-verify");
        var runAudit = !HasOption(subArgs, "--no-audit");

        var fullConfigPath = ResolveExistingFilePath(configPath);
        var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(fullConfigPath, WebCliJson.Options);
        var isCi = ConsoleEnvironment.IsCI;
        var suppressWarnings = (spec.Verify?.SuppressWarnings ?? Array.Empty<string>())
            .Concat(ReadOptionList(subArgs, "--suppress-warning", "--suppress-warnings"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var verifyBaselineGenerate = HasOption(subArgs, "--verify-baseline-generate");
        var verifyBaselineUpdate = HasOption(subArgs, "--verify-baseline-update");
        var verifyBaselinePath = TryGetOptionValue(subArgs, "--verify-baseline");
        var verifyFailOnNewWarnings = HasOption(subArgs, "--verify-fail-on-new") || HasOption(subArgs, "--verify-fail-on-new-warnings");
        var verifyWarningSummary = HasOption(subArgs, "--verify-warning-summary") || HasOption(subArgs, "--verify-warning-buckets");
        var verifyWarningSummaryTopText = TryGetOptionValue(subArgs, "--verify-warning-summary-top") ?? TryGetOptionValue(subArgs, "--verify-warning-buckets-top");
        var verifyWarningSummaryTop = ParseIntOption(verifyWarningSummaryTopText, 10);
        var failOnWarnings = HasOption(subArgs, "--fail-on-warnings") || (spec.Verify?.FailOnWarnings ?? false);
        var failOnNavLint = HasOption(subArgs, "--fail-on-nav-lint") || (spec.Verify?.FailOnNavLint ?? isCi);
        var failOnThemeContract = HasOption(subArgs, "--fail-on-theme-contract") || (spec.Verify?.FailOnThemeContract ?? isCi);
        var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);

        if (string.IsNullOrWhiteSpace(outPath))
            outPath = Path.Combine(Path.GetDirectoryName(fullConfigPath) ?? ".", "_site");
        var siteRoot = string.IsNullOrWhiteSpace(siteRootArg) ? outPath : siteRootArg;

        if (runBuild)
        {
            WebSiteBuilder.Build(spec, plan, outPath, WebCliJson.Options);
            siteRoot = outPath;
        }

        if (runAudit && (string.IsNullOrWhiteSpace(siteRoot) || !Directory.Exists(siteRoot)))
            return Fail("Doctor audit requires an existing site root. Use --out with --build or pass --site-root.", outputJson, logger, "web.doctor");

        var verify = runVerify ? WebSiteVerifier.Verify(spec, plan) : null;
        var filteredVerifyWarnings = verify is null
            ? Array.Empty<string>()
            : WebVerifyPolicy.FilterWarnings(verify.Warnings, suppressWarnings);
        var (verifySuccess, verifyPolicyFailures) = verify is null
            ? (true, Array.Empty<string>())
            : WebVerifyPolicy.EvaluateOutcome(
                verify,
                failOnWarnings,
                failOnNavLint,
                failOnThemeContract,
                suppressWarnings);

        if ((verifyBaselineGenerate || verifyBaselineUpdate || verifyFailOnNewWarnings) && string.IsNullOrWhiteSpace(verifyBaselinePath))
            verifyBaselinePath = ".powerforge/verify-baseline.json";

        string? verifyBaselineWrittenPath = null;
        if (verify is not null && !string.IsNullOrWhiteSpace(verifyBaselinePath))
        {
            var baselineLoaded = WebVerifyBaselineStore.TryLoadWarningKeys(plan.RootPath, verifyBaselinePath, out _, out var baselineKeys);
            var baselineSet = baselineLoaded ? new HashSet<string>(baselineKeys, StringComparer.OrdinalIgnoreCase) : null;
            var newWarnings = baselineSet is null
                ? Array.Empty<string>()
                : filteredVerifyWarnings.Where(w =>
                    !string.IsNullOrWhiteSpace(w) &&
                    !baselineSet.Contains(WebVerifyBaselineStore.NormalizeWarningKey(w))).ToArray();

            verify.BaselinePath = verifyBaselinePath;
            verify.BaselineWarningCount = baselineKeys.Length;
            verify.NewWarnings = newWarnings;
            verify.NewWarningCount = newWarnings.Length;

            if (verifyFailOnNewWarnings)
            {
                if (!baselineLoaded)
                {
                    verifySuccess = false;
                    verifyPolicyFailures = verifyPolicyFailures
                        .Concat(new[] { "verify-fail-on-new-warnings enabled but verify baseline could not be loaded (missing/empty/bad path). Generate one with --verify-baseline-generate." })
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }
                else if (newWarnings.Length > 0)
                {
                    verifySuccess = false;
                    verifyPolicyFailures = verifyPolicyFailures
                        .Concat(new[] { $"verify-fail-on-new-warnings enabled and verify produced {newWarnings.Length} new warning(s)." })
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }
            }

            if (verifyBaselineGenerate || verifyBaselineUpdate)
            {
                verifyBaselineWrittenPath = WebVerifyBaselineStore.Write(plan.RootPath, verifyBaselinePath, filteredVerifyWarnings, verifyBaselineUpdate, logger);
                verify.BaselinePath = verifyBaselineWrittenPath;
            }
        }

        var audit = runAudit ? RunDoctorAudit(siteRoot!, subArgs) : null;
        if (verify is not null && filteredVerifyWarnings.Length != verify.Warnings.Length)
        {
            verify = new WebVerifyResult
            {
                Success = verify.Success,
                Errors = verify.Errors,
                Warnings = filteredVerifyWarnings,
                BaselinePath = verify.BaselinePath,
                BaselineWarningCount = verify.BaselineWarningCount,
                NewWarningCount = verify.NewWarningCount,
                NewWarnings = verify.NewWarnings
            };
        }

        var recommendations = BuildDoctorRecommendations(verify, audit, verifyPolicyFailures);
        var success = verifySuccess && (audit?.Success ?? true);

        var doctorResult = new WebDoctorResult
        {
            Success = success,
            ConfigPath = specPath,
            SiteRoot = siteRoot ?? string.Empty,
            BuildExecuted = runBuild,
            VerifyExecuted = runVerify,
            AuditExecuted = runAudit,
            Verify = verify,
            Audit = audit,
            PolicyFailures = verifyPolicyFailures,
            Recommendations = recommendations
        };

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.doctor",
                Success = doctorResult.Success,
                ExitCode = doctorResult.Success ? 0 : 1,
                Config = "web",
                ConfigPath = specPath,
                Spec = WebCliJson.SerializeToElement(spec, WebCliJson.Context.SiteSpec),
                Plan = WebCliJson.SerializeToElement(plan, WebCliJson.Context.WebSitePlan),
                Result = WebCliJson.SerializeToElement(doctorResult, WebCliJson.Context.WebDoctorResult)
            });
            return doctorResult.Success ? 0 : 1;
        }

        if (runVerify && verify is not null)
        {
            foreach (var warning in verify.Warnings)
                logger.Warn($"verify: {warning}");
            foreach (var error in verify.Errors)
                logger.Error($"verify: {error}");
            foreach (var policy in verifyPolicyFailures)
                logger.Error($"verify-policy: {policy}");
        }

        if (runAudit && audit is not null)
        {
            foreach (var warning in audit.Warnings)
                logger.Warn($"audit: {warning}");
            foreach (var error in audit.Errors)
                logger.Error($"audit: {error}");
        }

        if (doctorResult.Success)
            logger.Success("Web doctor passed.");

        logger.Info($"Build executed: {doctorResult.BuildExecuted}");
        logger.Info($"Verify executed: {doctorResult.VerifyExecuted}");
        logger.Info($"Audit executed: {doctorResult.AuditExecuted}");
        if (verify is not null)
        {
            logger.Info($"Verify: {verify.Errors.Length} errors, {verify.Warnings.Length} warnings");
            if (!string.IsNullOrWhiteSpace(verify.BaselinePath))
            {
                logger.Info($"Verify baseline: {verify.BaselinePath} ({verify.BaselineWarningCount} keys)");
                if (verify.NewWarningCount > 0)
                    logger.Info($"New verify warnings vs baseline: {verify.NewWarningCount}");
                if (!string.IsNullOrWhiteSpace(verifyBaselineWrittenPath))
                    logger.Info($"Verify baseline written: {verifyBaselineWrittenPath}");
            }
            if (verifyWarningSummary && verify.Warnings.Length > 0)
                logger.Info(WebWarningBucketer.BuildTopBucketsSummary(verify.Warnings, verifyWarningSummaryTop));
        }
        if (audit is not null)
        {
            logger.Info($"Audit: {audit.ErrorCount} errors, {audit.WarningCount} warnings");
            if (!string.IsNullOrWhiteSpace(audit.SummaryPath))
                logger.Info($"Audit summary: {audit.SummaryPath}");
            if (!string.IsNullOrWhiteSpace(audit.SarifPath))
                logger.Info($"Audit SARIF: {audit.SarifPath}");
        }

        if (recommendations.Length > 0)
        {
            foreach (var recommendation in recommendations)
                logger.Info($"Recommendation: {recommendation}");
        }

        return doctorResult.Success ? 0 : 1;
    }

    internal static int HandleAudit(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var siteRoot = TryGetOptionValue(subArgs, "--site-root") ??
                       TryGetOptionValue(subArgs, "--root") ??
                       TryGetOptionValue(subArgs, "--path") ??
                       TryGetOptionValue(subArgs, "--out");
        var configPath = TryGetOptionValue(subArgs, "--config");

        if (!string.IsNullOrWhiteSpace(configPath))
        {
            var fullConfigPath = ResolveExistingFilePath(configPath);
            var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(fullConfigPath, WebCliJson.Options);
            var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);
            var outPath = TryGetOptionValue(subArgs, "--out") ??
                          TryGetOptionValue(subArgs, "--out-path") ??
                          TryGetOptionValue(subArgs, "--output-path");
            if (string.IsNullOrWhiteSpace(outPath))
                outPath = Path.Combine(Path.GetDirectoryName(fullConfigPath) ?? ".", "_site");
            WebSiteBuilder.Build(spec, plan, outPath, WebCliJson.Options);
            siteRoot = outPath;
        }

        if (string.IsNullOrWhiteSpace(siteRoot))
            return Fail("Missing required --site-root.", outputJson, logger, "web.audit");

        var include = ReadOptionList(subArgs, "--include");
        var exclude = ReadOptionList(subArgs, "--exclude");
        var budgetExclude = ReadOptionList(subArgs, "--budget-exclude", "--budget-excludes");
        var ignoreNav = ReadOptionList(subArgs, "--ignore-nav", "--ignore-nav-path");
        var ignoreMedia = ReadOptionList(subArgs, "--ignore-media");
        var navIgnorePrefixes = ReadOptionList(subArgs, "--nav-ignore-prefix", "--nav-ignore-prefixes");
        var navRequiredLinks = ReadOptionList(subArgs, "--nav-required-link", "--nav-required-links");
        var navProfilesPath = TryGetOptionValue(subArgs, "--nav-profiles");
        var mediaProfilesPath = TryGetOptionValue(subArgs, "--media-profiles");
        var minNavCoverageText = TryGetOptionValue(subArgs, "--min-nav-coverage");
        var requiredRoutes = ReadOptionList(subArgs, "--required-route", "--required-routes");
        var useDefaultIgnoreNav = !HasOption(subArgs, "--no-default-ignore-nav");
        var useDefaultIgnoreMedia = !HasOption(subArgs, "--no-default-ignore-media");
        var navSelector = TryGetOptionValue(subArgs, "--nav-selector") ?? "nav";
        var navRequired = !HasOption(subArgs, "--nav-optional");
        var rendered = HasOption(subArgs, "--rendered");
        var renderedHeadless = !HasOption(subArgs, "--rendered-headful");
        var renderedEngine = TryGetOptionValue(subArgs, "--rendered-engine");
        var renderedEnsureInstalled = !HasOption(subArgs, "--rendered-no-install");
        var renderedMaxText = TryGetOptionValue(subArgs, "--rendered-max");
        var renderedTimeoutText = TryGetOptionValue(subArgs, "--rendered-timeout");
        var renderedBaseUrl = TryGetOptionValue(subArgs, "--rendered-base-url");
        var renderedHost = TryGetOptionValue(subArgs, "--rendered-host");
        var renderedPortText = TryGetOptionValue(subArgs, "--rendered-port");
        var renderedNoServe = HasOption(subArgs, "--rendered-no-serve");
        var renderedNoConsoleErrors = HasOption(subArgs, "--rendered-no-console-errors");
        var renderedNoConsoleWarnings = HasOption(subArgs, "--rendered-no-console-warnings");
        var renderedNoFailures = HasOption(subArgs, "--rendered-no-failures");
        var renderedContrast = HasOption(subArgs, "--rendered-contrast") && !HasOption(subArgs, "--rendered-no-contrast");
        var renderedContrastMinText = TryGetOptionValue(subArgs, "--rendered-contrast-min");
        var renderedContrastMaxFindingsText = TryGetOptionValue(subArgs, "--rendered-contrast-max-findings");
        var renderedInclude = ReadOptionList(subArgs, "--rendered-include");
        var renderedExclude = ReadOptionList(subArgs, "--rendered-exclude");
        var summaryEnabled = HasOption(subArgs, "--summary");
        var summaryPath = TryGetOptionValue(subArgs, "--summary-path");
        var summaryMaxText = TryGetOptionValue(subArgs, "--summary-max");
        var sarifEnabled = HasOption(subArgs, "--sarif");
        var sarifPath = TryGetOptionValue(subArgs, "--sarif-path");
        var useDefaultExclude = !HasOption(subArgs, "--no-default-exclude");
        var baselineGenerate = HasOption(subArgs, "--baseline-generate");
        var baselineUpdate = HasOption(subArgs, "--baseline-update");
        var baselinePathValue = TryGetOptionValue(subArgs, "--baseline");
        var failOnWarnings = HasOption(subArgs, "--fail-on-warnings");
        var failOnNew = HasOption(subArgs, "--fail-on-new");
        var maxErrorsText = TryGetOptionValue(subArgs, "--max-errors");
        var maxWarningsText = TryGetOptionValue(subArgs, "--max-warnings");
        var failCategories = ReadOptionList(subArgs, "--fail-category", "--fail-categories");
        var failIssueCodes = ReadOptionList(subArgs, "--fail-issue", "--fail-issues", "--fail-issue-code", "--fail-issue-codes");
        var navCanonical = TryGetOptionValue(subArgs, "--nav-canonical");
        var navCanonicalSelector = TryGetOptionValue(subArgs, "--nav-canonical-selector");
        var navCanonicalRequired = HasOption(subArgs, "--nav-canonical-required");
        var checkUtf8 = !HasOption(subArgs, "--no-utf8");
        var checkMetaCharset = !HasOption(subArgs, "--no-meta-charset");
        var checkReplacementChars = !HasOption(subArgs, "--no-replacement-char-check");
        var checkHeadingOrder = !HasOption(subArgs, "--no-heading-order");
        var checkSeoMeta = HasOption(subArgs, "--seo-meta") && !HasOption(subArgs, "--no-seo-meta");
        var checkLinkPurpose = !HasOption(subArgs, "--no-link-purpose");
        var checkMediaEmbeds = !HasOption(subArgs, "--no-media");
        var checkNetworkHints = !HasOption(subArgs, "--no-network-hints");
        var checkRenderBlocking = !HasOption(subArgs, "--no-render-blocking");
        var maxHeadBlockingText = TryGetOptionValue(subArgs, "--max-head-blocking");
        var maxHtmlFilesText = TryGetOptionValue(subArgs, "--max-html-files") ?? TryGetOptionValue(subArgs, "--max-html");
        var maxTotalFilesText = TryGetOptionValue(subArgs, "--max-total-files") ?? TryGetOptionValue(subArgs, "--max-files-total");
        var suppressIssues = ReadOptionList(subArgs, "--suppress-issue", "--suppress-issues");

        var ignoreNavPatterns = BuildIgnoreNavPatterns(ignoreNav, useDefaultIgnoreNav);
        var ignoreMediaPatterns = BuildIgnoreMediaPatterns(ignoreMedia, useDefaultIgnoreMedia);
        var renderedMaxPages = ParseIntOption(renderedMaxText, 20);
        var renderedTimeoutMs = ParseIntOption(renderedTimeoutText, 30000);
        var renderedPort = ParseIntOption(renderedPortText, 0);
        var renderedContrastMinRatio = ParseDoubleOption(renderedContrastMinText, 4.5d);
        var renderedContrastMaxFindings = ParseIntOption(renderedContrastMaxFindingsText, 10);
        var summaryMax = ParseIntOption(summaryMaxText, 10);
        var maxErrors = ParseIntOption(maxErrorsText, -1);
        var maxWarnings = ParseIntOption(maxWarningsText, -1);
        var minNavCoveragePercent = ParseIntOption(minNavCoverageText, 0);
        var maxHeadBlockingResources = ParseIntOption(maxHeadBlockingText, new WebAuditOptions().MaxHeadBlockingResources);
        var maxHtmlFiles = ParseIntOption(maxHtmlFilesText, 0);
        var maxTotalFiles = ParseIntOption(maxTotalFilesText, 0);
        if ((baselineGenerate || baselineUpdate) && string.IsNullOrWhiteSpace(baselinePathValue))
            baselinePathValue = ".powerforge/audit-baseline.json";
        var baselineRoot = !string.IsNullOrWhiteSpace(configPath)
            ? (Path.GetDirectoryName(ResolveExistingFilePath(configPath)) ?? Directory.GetCurrentDirectory())
            : Directory.GetCurrentDirectory();
        var resolvedSummaryPath = ResolveSummaryPath(summaryEnabled, summaryPath);
        var resolvedSarifPath = ResolveSarifPath(sarifEnabled, sarifPath);
        var navProfiles = LoadAuditNavProfiles(navProfilesPath);
        var mediaProfiles = LoadAuditMediaProfiles(mediaProfilesPath);

        var result = WebSiteAuditor.Audit(new WebAuditOptions
        {
            SiteRoot = siteRoot,
            BaselineRoot = baselineRoot,
            Include = include.ToArray(),
            Exclude = exclude.ToArray(),
            UseDefaultExcludes = useDefaultExclude,
            MaxHtmlFiles = Math.Max(0, maxHtmlFiles),
            MaxTotalFiles = Math.Max(0, maxTotalFiles),
            BudgetExclude = budgetExclude.ToArray(),
            SuppressIssues = suppressIssues.ToArray(),
            IgnoreNavFor = ignoreNavPatterns,
            IgnoreMediaFor = ignoreMediaPatterns,
            NavSelector = navSelector,
            NavRequired = navRequired,
            NavIgnorePrefixes = navIgnorePrefixes.ToArray(),
            NavRequiredLinks = navRequiredLinks.ToArray(),
            NavProfiles = navProfiles,
            MediaProfiles = mediaProfiles,
            MinNavCoveragePercent = minNavCoveragePercent,
            RequiredRoutes = requiredRoutes.ToArray(),
            CheckLinks = !HasOption(subArgs, "--no-links"),
            CheckAssets = !HasOption(subArgs, "--no-assets"),
            CheckNavConsistency = !HasOption(subArgs, "--no-nav"),
            CheckTitles = !(HasOption(subArgs, "--no-titles") || HasOption(subArgs, "--no-title")),
            CheckDuplicateIds = !HasOption(subArgs, "--no-ids"),
            CheckHtmlStructure = !HasOption(subArgs, "--no-structure"),
            CheckRendered = rendered,
            RenderedHeadless = renderedHeadless,
            RenderedEngine = renderedEngine ?? "Chromium",
            RenderedEnsureInstalled = renderedEnsureInstalled,
            RenderedBaseUrl = renderedBaseUrl,
            RenderedServe = !renderedNoServe,
            RenderedServeHost = string.IsNullOrWhiteSpace(renderedHost) ? "localhost" : renderedHost,
            RenderedServePort = renderedPort,
            RenderedMaxPages = renderedMaxPages,
            RenderedTimeoutMs = renderedTimeoutMs,
            RenderedCheckConsoleErrors = !renderedNoConsoleErrors,
            RenderedCheckConsoleWarnings = !renderedNoConsoleWarnings,
            RenderedCheckFailedRequests = !renderedNoFailures,
            RenderedCheckContrast = renderedContrast,
            RenderedContrastMinRatio = renderedContrastMinRatio,
            RenderedContrastMaxFindings = Math.Clamp(renderedContrastMaxFindings, 1, 200),
            RenderedInclude = renderedInclude.ToArray(),
            RenderedExclude = renderedExclude.ToArray(),
            SummaryPath = resolvedSummaryPath,
            SarifPath = resolvedSarifPath,
            SummaryMaxIssues = summaryMax,
            BaselinePath = baselinePathValue,
            FailOnWarnings = failOnWarnings,
            FailOnNewIssues = failOnNew,
            MaxErrors = maxErrors,
            MaxWarnings = maxWarnings,
            FailOnCategories = failCategories.ToArray(),
            FailOnIssueCodes = failIssueCodes.ToArray(),
            NavCanonicalPath = navCanonical,
            NavCanonicalSelector = navCanonicalSelector,
            NavCanonicalRequired = navCanonicalRequired,
            CheckUtf8 = checkUtf8,
            CheckMetaCharset = checkMetaCharset,
            CheckUnicodeReplacementChars = checkReplacementChars,
            CheckHeadingOrder = checkHeadingOrder,
            CheckSeoMeta = checkSeoMeta,
            CheckLinkPurposeConsistency = checkLinkPurpose,
            CheckMediaEmbeds = checkMediaEmbeds,
            CheckNetworkHints = checkNetworkHints,
            CheckRenderBlockingResources = checkRenderBlocking,
            MaxHeadBlockingResources = maxHeadBlockingResources
        });

        string? writtenBaselinePath = null;
        if (baselineGenerate || baselineUpdate)
        {
            writtenBaselinePath = WebAuditBaselineStore.Write(baselineRoot, baselinePathValue, result, baselineUpdate, logger);
            result.BaselinePath = writtenBaselinePath;
        }

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.audit",
                Success = result.Success,
                ExitCode = result.Success ? 0 : 1,
                Config = "web",
                Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebAuditResult)
            });
            return result.Success ? 0 : 1;
        }

        var warningPreviewText = TryGetOptionValue(subArgs, "--warning-preview") ?? TryGetOptionValue(subArgs, "--warning-preview-count");
        var errorPreviewText = TryGetOptionValue(subArgs, "--error-preview") ?? TryGetOptionValue(subArgs, "--error-preview-count");
        var warningPreviewCount = ParseIntOption(warningPreviewText, 0);
        var errorPreviewCount = ParseIntOption(errorPreviewText, 0);

        if (result.Warnings.Length > 0)
        {
            var max = warningPreviewCount <= 0 ? result.Warnings.Length : Math.Max(0, warningPreviewCount);
            foreach (var w in result.Warnings.Take(max))
                logger.Warn(w);
            var remaining = result.Warnings.Length - max;
            if (remaining > 0)
                logger.Info($"Audit warnings: showing {max}/{result.Warnings.Length} (use --warning-preview 0 to show all).");
        }

        if (result.Errors.Length > 0)
        {
            var max = errorPreviewCount <= 0 ? result.Errors.Length : Math.Max(0, errorPreviewCount);
            foreach (var e in result.Errors.Take(max))
                logger.Error(e);
            var remaining = result.Errors.Length - max;
            if (remaining > 0)
                logger.Info($"Audit errors: showing {max}/{result.Errors.Length} (use --error-preview 0 to show all).");
        }

        if (result.Success)
        logger.Success("Web audit passed.");

        if (result.TotalFileCount > 0)
            logger.Info($"Files: {result.TotalFileCount}");
        logger.Info($"Pages: {result.PageCount}");
        logger.Info($"Links: {result.LinkCount} (broken {result.BrokenLinkCount})");
        logger.Info($"Assets: {result.AssetCount} (missing {result.MissingAssetCount})");
        logger.Info($"Navigation: checked {result.NavCheckedCount}, ignored {result.NavIgnoredCount}, coverage {result.NavCoveragePercent:0.0}%, mismatches {result.NavMismatchCount}");
        if (result.RequiredRouteCount > 0)
            logger.Info($"Required routes: {result.RequiredRouteCount} (missing {result.MissingRequiredRouteCount})");
        logger.Info($"Issues: {result.ErrorCount} errors, {result.WarningCount} warnings");
        if (result.NewIssueCount > 0 || !string.IsNullOrWhiteSpace(result.BaselinePath))
            logger.Info($"New issues: {result.NewIssueCount} (errors {result.NewErrorCount}, warnings {result.NewWarningCount})");
        if (result.DuplicateIdCount > 0)
            logger.Info($"Duplicate IDs: {result.DuplicateIdCount}");
        if (result.RenderedPageCount > 0)
        {
            logger.Info($"Rendered pages: {result.RenderedPageCount}");
            if (result.RenderedConsoleErrorCount > 0)
                logger.Info($"Rendered console errors: {result.RenderedConsoleErrorCount}");
            if (result.RenderedConsoleWarningCount > 0)
                logger.Info($"Rendered console warnings: {result.RenderedConsoleWarningCount}");
            if (result.RenderedFailedRequestCount > 0)
                logger.Info($"Rendered failed requests: {result.RenderedFailedRequestCount}");
            if (result.RenderedContrastIssueCount > 0)
                logger.Info($"Rendered contrast findings: {result.RenderedContrastIssueCount}");
        }

        if (!string.IsNullOrWhiteSpace(result.BaselinePath))
            logger.Info($"Baseline: {result.BaselinePath} ({result.BaselineIssueCount} keys)");
        if (!string.IsNullOrWhiteSpace(writtenBaselinePath))
            logger.Info($"Baseline written: {writtenBaselinePath}");
        if (!string.IsNullOrWhiteSpace(result.SummaryPath))
            logger.Info($"Audit summary: {result.SummaryPath}");
        if (!string.IsNullOrWhiteSpace(result.SarifPath))
            logger.Info($"Audit SARIF: {result.SarifPath}");

        return result.Success ? 0 : 1;
    }

    internal static int HandleOptimize(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var siteRoot = TryGetOptionValue(subArgs, "--site-root") ??
                       TryGetOptionValue(subArgs, "--root") ??
                       TryGetOptionValue(subArgs, "--path");
        var configPath = TryGetOptionValue(subArgs, "--config");
        var criticalCss = TryGetOptionValue(subArgs, "--critical-css");
        var cssPattern = TryGetOptionValue(subArgs, "--css-pattern");
        var minifyHtml = subArgs.Any(a => a.Equals("--minify-html", StringComparison.OrdinalIgnoreCase));
        var minifyCss = subArgs.Any(a => a.Equals("--minify-css", StringComparison.OrdinalIgnoreCase));
        var minifyJs = subArgs.Any(a => a.Equals("--minify-js", StringComparison.OrdinalIgnoreCase));
        var optimizeImages = HasOption(subArgs, "--optimize-images") || HasOption(subArgs, "--images");
        var imageExtensions = ReadOptionList(subArgs, "--image-ext", "--image-extensions");
        var imageInclude = ReadOptionList(subArgs, "--image-include");
        var imageExclude = ReadOptionList(subArgs, "--image-exclude");
        var imageQuality = ParseIntOption(TryGetOptionValue(subArgs, "--image-quality"), 82);
        var imageStripMetadata = !HasOption(subArgs, "--image-keep-metadata");
        var imageGenerateWebp = HasOption(subArgs, "--image-generate-webp");
        var imageGenerateAvif = HasOption(subArgs, "--image-generate-avif");
        var imagePreferNextGen = HasOption(subArgs, "--image-prefer-nextgen");
        var imageWidths = ParseIntListOption(TryGetOptionValue(subArgs, "--image-widths"));
        var imageEnhanceTags = HasOption(subArgs, "--image-enhance-tags");
        var imageMaxBytesPerFile = ParseLongOption(TryGetOptionValue(subArgs, "--image-max-bytes"), 0);
        var imageMaxTotalBytes = ParseLongOption(TryGetOptionValue(subArgs, "--image-max-total-bytes"), 0);
        var imageFailOnBudget = HasOption(subArgs, "--image-fail-on-budget");
        var hashAssets = HasOption(subArgs, "--hash-assets");
        var hashExtensions = ReadOptionList(subArgs, "--hash-ext", "--hash-extensions");
        var hashExclude = ReadOptionList(subArgs, "--hash-exclude");
        var hashManifest = TryGetOptionValue(subArgs, "--hash-manifest");
        var reportPath = TryGetOptionValue(subArgs, "--report-path");
        var headersEnabled = HasOption(subArgs, "--headers");
        var headersOut = TryGetOptionValue(subArgs, "--headers-out");
        var headersHtml = TryGetOptionValue(subArgs, "--headers-html");
        var headersAssets = TryGetOptionValue(subArgs, "--headers-assets");

        if (string.IsNullOrWhiteSpace(siteRoot))
            return Fail("Missing required --site-root.", outputJson, logger, "web.optimize");

        AssetPolicySpec? policy = null;
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            var resolved = ResolveExistingFilePath(configPath);
            var (spec, _) = WebSiteSpecLoader.LoadWithPath(resolved, WebCliJson.Options);
            policy = spec.AssetPolicy;
        }

        if (headersEnabled)
        {
            policy ??= new AssetPolicySpec();
            policy.CacheHeaders ??= new CacheHeadersSpec { Enabled = true };
            policy.CacheHeaders.Enabled = true;
            if (!string.IsNullOrWhiteSpace(headersOut))
                policy.CacheHeaders.OutputPath = headersOut;
            if (!string.IsNullOrWhiteSpace(headersHtml))
                policy.CacheHeaders.HtmlCacheControl = headersHtml;
            if (!string.IsNullOrWhiteSpace(headersAssets))
                policy.CacheHeaders.ImmutableCacheControl = headersAssets;
        }

        var optimizeResult = WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions
        {
            SiteRoot = siteRoot,
            CriticalCssPath = criticalCss,
            CssLinkPattern = string.IsNullOrWhiteSpace(cssPattern) ? "(app|api-docs)\\.css" : cssPattern,
            MinifyHtml = minifyHtml,
            MinifyCss = minifyCss,
            MinifyJs = minifyJs,
            OptimizeImages = optimizeImages,
            ImageExtensions = imageExtensions.Count > 0 ? imageExtensions.ToArray() : new[] { ".png", ".jpg", ".jpeg", ".webp" },
            ImageInclude = imageInclude.Count > 0 ? imageInclude.ToArray() : Array.Empty<string>(),
            ImageExclude = imageExclude.Count > 0 ? imageExclude.ToArray() : Array.Empty<string>(),
            ImageQuality = imageQuality,
            ImageStripMetadata = imageStripMetadata,
            ImageGenerateWebp = imageGenerateWebp,
            ImageGenerateAvif = imageGenerateAvif,
            ImagePreferNextGen = imagePreferNextGen,
            ResponsiveImageWidths = imageWidths.Length > 0 ? imageWidths : Array.Empty<int>(),
            EnhanceImageTags = imageEnhanceTags,
            ImageMaxBytesPerFile = imageMaxBytesPerFile,
            ImageMaxTotalBytes = imageMaxTotalBytes,
            HashAssets = hashAssets,
            HashExtensions = hashExtensions.Count > 0 ? hashExtensions.ToArray() : new[] { ".css", ".js" },
            HashExclude = hashExclude.Count > 0 ? hashExclude.ToArray() : Array.Empty<string>(),
            HashManifestPath = hashManifest,
            ReportPath = reportPath,
            AssetPolicy = policy
        });
        if (imageFailOnBudget && optimizeResult.ImageBudgetExceeded)
            return Fail($"Image budget exceeded: {string.Join(" | ", optimizeResult.ImageBudgetWarnings)}", outputJson, logger, "web.optimize");

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.optimize",
                Success = true,
                ExitCode = 0,
                Result = WebCliJson.SerializeToElement(optimizeResult, WebCliJson.Context.WebOptimizeResult)
            });
            return 0;
        }

        logger.Success($"Optimize updated files: {optimizeResult.UpdatedCount}");
        logger.Info($"HTML files: {optimizeResult.HtmlFileCount}");
        logger.Info($"Critical CSS inlined: {optimizeResult.CriticalCssInlinedCount}");
        logger.Info($"Minified HTML: {optimizeResult.HtmlMinifiedCount}");
        logger.Info($"HTML bytes saved: {optimizeResult.HtmlBytesSaved}");
        logger.Info($"Minified CSS: {optimizeResult.CssMinifiedCount}");
        logger.Info($"CSS bytes saved: {optimizeResult.CssBytesSaved}");
        logger.Info($"Minified JS: {optimizeResult.JsMinifiedCount}");
        logger.Info($"JS bytes saved: {optimizeResult.JsBytesSaved}");
        logger.Info($"Image files: {optimizeResult.ImageFileCount}");
        logger.Info($"Optimized images: {optimizeResult.ImageOptimizedCount}");
        logger.Info($"Image bytes before: {optimizeResult.ImageBytesBefore}");
        logger.Info($"Image bytes after: {optimizeResult.ImageBytesAfter}");
        logger.Info($"Image bytes saved: {optimizeResult.ImageBytesSaved}");
        logger.Info($"Generated image variants: {optimizeResult.ImageVariantCount}");
        logger.Info($"Image HTML rewrites: {optimizeResult.ImageHtmlRewriteCount}");
        logger.Info($"Image hints added: {optimizeResult.ImageHintedCount}");
        foreach (var entry in optimizeResult.OptimizedImages.Take(5))
            logger.Info($"Image saved: {entry.Path} (-{entry.BytesSaved}B)");
        if (optimizeResult.ImageBudgetExceeded)
        {
            foreach (var warning in optimizeResult.ImageBudgetWarnings)
                logger.Warn($"Image budget: {warning}");
        }

        if (optimizeResult.HashedAssetCount > 0)
        {
            logger.Info($"Hashed assets: {optimizeResult.HashedAssetCount}");
            logger.Info($"Hashed reference rewrites: HTML {optimizeResult.HtmlHashRewriteCount}, CSS {optimizeResult.CssHashRewriteCount}");
        }

        if (optimizeResult.CacheHeadersWritten)
            logger.Info($"Cache headers: {optimizeResult.CacheHeadersPath}");
        if (!string.IsNullOrWhiteSpace(optimizeResult.ReportPath))
            logger.Info($"Optimize report: {optimizeResult.ReportPath}");
        return 0;
    }
}
