using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PowerForge;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecuteDoctor(JsonElement step, string baseDir, bool fast, string effectiveMode, WebPipelineStepResult stepResult)
    {
        var config = ResolvePath(baseDir, GetString(step, "config"));
        if (string.IsNullOrWhiteSpace(config))
            throw new InvalidOperationException("doctor requires config.");

        var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
        var outPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output"));
        var runBuild = GetBool(step, "build");
        var runVerify = GetBool(step, "verify");
        var runAudit = GetBool(step, "audit");
        var noBuild = GetBool(step, "noBuild") ?? false;
        var noVerify = GetBool(step, "noVerify") ?? false;
        var noAudit = GetBool(step, "noAudit") ?? false;
        var executeBuild = runBuild ?? !noBuild;
        var executeVerify = runVerify ?? !noVerify;
        var executeAudit = runAudit ?? !noAudit;

        var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(config, WebCliJson.Options);
        var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);
        if (string.IsNullOrWhiteSpace(outPath))
            outPath = Path.Combine(Path.GetDirectoryName(config) ?? ".", "_site");
        var effectiveSiteRoot = string.IsNullOrWhiteSpace(siteRoot) ? outPath : siteRoot;

        if (executeBuild)
        {
            WebSiteBuilder.Build(spec, plan, outPath, WebCliJson.Options);
            effectiveSiteRoot = outPath;
        }

        if (executeAudit && (string.IsNullOrWhiteSpace(effectiveSiteRoot) || !Directory.Exists(effectiveSiteRoot)))
            throw new InvalidOperationException("doctor audit requires existing siteRoot. Provide siteRoot or enable build.");

        WebVerifyResult? verify = null;
        var verifyPolicyFailures = Array.Empty<string>();
        if (executeVerify)
        {
            verify = WebSiteVerifier.Verify(spec, plan);
            var isDev = string.Equals(effectiveMode, "dev", StringComparison.OrdinalIgnoreCase) || fast;
            var isCi = ConsoleEnvironment.IsCI;
            var ciStrictDefaults = isCi && !isDev;
            var verifyWarningPreviewCount = GetInt(step, "verifyWarningPreviewCount") ?? GetInt(step, "verify-warning-preview") ?? (isDev ? 2 : 5);
            var verifyErrorPreviewCount = GetInt(step, "verifyErrorPreviewCount") ?? GetInt(step, "verify-error-preview") ?? (isDev ? 2 : 5);

            var suppressWarnings = GetArrayOfStrings(step, "suppressWarnings") ?? spec.Verify?.SuppressWarnings;
            var failOnWarnings = GetBool(step, "failOnWarnings") ?? spec.Verify?.FailOnWarnings ?? false;
            var failOnNavLint = GetBool(step, "failOnNavLint") ?? GetBool(step, "failOnNavLintWarnings") ?? spec.Verify?.FailOnNavLint ?? ciStrictDefaults;
            var failOnThemeContract = GetBool(step, "failOnThemeContract") ?? spec.Verify?.FailOnThemeContract ?? ciStrictDefaults;

            var verifyBaselineGenerate = GetBool(step, "verifyBaselineGenerate") ?? false;
            var verifyBaselineUpdate = GetBool(step, "verifyBaselineUpdate") ?? false;
            var verifyBaselinePath = GetString(step, "verifyBaseline") ?? GetString(step, "verifyBaselinePath");
            var verifyFailOnNewWarnings = GetBool(step, "verifyFailOnNewWarnings") ?? GetBool(step, "verifyFailOnNew") ?? false;
            if ((verifyBaselineGenerate || verifyBaselineUpdate || verifyFailOnNewWarnings) && string.IsNullOrWhiteSpace(verifyBaselinePath))
                verifyBaselinePath = ".powerforge/verify-baseline.json";

            var filteredWarnings = WebVerifyPolicy.FilterWarnings(verify.Warnings, suppressWarnings);
            var baselineLoaded = false;
            var baselineKeys = Array.Empty<string>();
            if (!string.IsNullOrWhiteSpace(verifyBaselinePath) || verifyBaselineGenerate || verifyBaselineUpdate || verifyFailOnNewWarnings)
                baselineLoaded = WebVerifyBaselineStore.TryLoadWarningKeys(plan.RootPath, verifyBaselinePath, out _, out baselineKeys);

            var baselineSet = baselineLoaded ? new HashSet<string>(baselineKeys, StringComparer.OrdinalIgnoreCase) : null;
            var newWarnings = baselineSet is null
                ? Array.Empty<string>()
                : filteredWarnings.Where(w => !string.IsNullOrWhiteSpace(w) && !baselineSet.Contains(w.Trim())).ToArray();

            var (verifySuccess, policyFailures) = WebVerifyPolicy.EvaluateOutcome(
                verify,
                failOnWarnings,
                failOnNavLint,
                failOnThemeContract,
                suppressWarnings);

            verifyPolicyFailures = policyFailures;

            if (verifyFailOnNewWarnings)
            {
                if (!baselineLoaded)
                {
                    verifySuccess = false;
                    verifyPolicyFailures = verifyPolicyFailures
                        .Concat(new[] { "verifyFailOnNewWarnings enabled but verify baseline could not be loaded (missing/empty/bad path). Generate one with verifyBaselineGenerate." })
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }
                else if (newWarnings.Length > 0)
                {
                    verifySuccess = false;
                    verifyPolicyFailures = verifyPolicyFailures
                        .Concat(new[] { $"verifyFailOnNewWarnings enabled and verify produced {newWarnings.Length} new warning(s)." })
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }
            }

            if (verifyBaselineGenerate || verifyBaselineUpdate)
                WebVerifyBaselineStore.Write(plan.RootPath, verifyBaselinePath, filteredWarnings, verifyBaselineUpdate, logger: null);

            if (!verifySuccess)
            {
                var message = BuildVerifyFailureSummary(
                    verify,
                    filteredWarnings,
                    verifyPolicyFailures,
                    verifyBaselinePath,
                    baselineKeys.Length,
                    newWarnings,
                    verifyWarningPreviewCount,
                    verifyErrorPreviewCount);
                throw new InvalidOperationException(message);
            }
        }

        WebAuditResult? audit = null;
        if (executeAudit)
        {
            var include = GetString(step, "include");
            var exclude = GetString(step, "exclude");
            var ignoreNav = GetString(step, "ignoreNav") ?? GetString(step, "ignore-nav");
            var ignoreMedia = GetString(step, "ignoreMedia") ?? GetString(step, "ignore-media");
            var navIgnorePrefixes = GetString(step, "navIgnorePrefixes") ?? GetString(step, "nav-ignore-prefixes") ??
                                    GetString(step, "navIgnorePrefix") ?? GetString(step, "nav-ignore-prefix");
            var navRequiredLinks = GetString(step, "navRequiredLinks") ?? GetString(step, "nav-required-links") ??
                                   GetString(step, "navRequiredLink") ?? GetString(step, "nav-required-link");
            var navProfilesPath = GetString(step, "navProfiles") ?? GetString(step, "nav-profiles");
            var mediaProfilesPath = GetString(step, "mediaProfiles") ?? GetString(step, "media-profiles");
            var requiredRoutes = GetString(step, "requiredRoutes") ?? GetString(step, "required-routes") ??
                                 GetString(step, "requiredRoute") ?? GetString(step, "required-route");
            var navSelector = GetString(step, "navSelector") ?? GetString(step, "nav-selector") ?? "nav";
            var navRequired = GetBool(step, "navRequired");
            var navOptional = GetBool(step, "navOptional");
            var minNavCoveragePercent = GetInt(step, "minNavCoveragePercent") ?? GetInt(step, "min-nav-coverage") ?? 0;
            var useDefaultExclude = !(GetBool(step, "noDefaultExclude") ?? false);
            var useDefaultIgnoreNav = !(GetBool(step, "noDefaultIgnoreNav") ?? false);
            var useDefaultIgnoreMedia = !(GetBool(step, "noDefaultIgnoreMedia") ?? false);
            var summary = GetBool(step, "summary") ?? false;
            var summaryPath = GetString(step, "summaryPath");
            var summaryMax = GetInt(step, "summaryMaxIssues") ?? 10;
            var summaryOnFail = GetBool(step, "summaryOnFail") ?? GetBool(step, "summary-on-fail") ?? true;
            var sarif = GetBool(step, "sarif") ?? false;
            var sarifPath = GetString(step, "sarifPath") ?? GetString(step, "sarif-path");
            var sarifOnFail = GetBool(step, "sarifOnFail") ?? GetBool(step, "sarif-on-fail") ?? true;
            var baselineGenerate = GetBool(step, "baselineGenerate") ?? false;
            var baselineUpdate = GetBool(step, "baselineUpdate") ?? false;
            var baselinePath = GetString(step, "baselinePath") ?? GetString(step, "baseline");
            var failOnWarnings = GetBool(step, "failOnWarnings") ?? false;
            var failOnNewIssues = GetBool(step, "failOnNewIssues") ?? GetBool(step, "failOnNew") ?? false;
            var maxErrors = GetInt(step, "maxErrors") ?? -1;
            var maxWarnings = GetInt(step, "maxWarnings") ?? -1;
            var failOnCategories = GetString(step, "failOnCategories") ?? GetString(step, "failCategories");
            var navCanonicalPath = GetString(step, "navCanonicalPath") ?? GetString(step, "navCanonical");
            var navCanonicalSelector = GetString(step, "navCanonicalSelector");
            var navCanonicalRequired = GetBool(step, "navCanonicalRequired") ?? false;
            var checkUtf8 = GetBool(step, "checkUtf8") ?? true;
            var checkMetaCharset = GetBool(step, "checkMetaCharset") ?? true;
            var checkReplacement = GetBool(step, "checkUnicodeReplacementChars") ?? true;
            var checkHeadingOrder = GetBool(step, "checkHeadingOrder") ?? true;
            var checkLinkPurpose = GetBool(step, "checkLinkPurposeConsistency") ?? GetBool(step, "checkLinkPurpose") ?? true;
            var checkMediaEmbeds = GetBool(step, "checkMediaEmbeds") ?? GetBool(step, "checkMedia") ?? true;
            var checkNetworkHints = GetBool(step, "checkNetworkHints") ?? true;
            var checkRenderBlocking = GetBool(step, "checkRenderBlockingResources") ?? GetBool(step, "checkRenderBlocking") ?? true;
            var maxHeadBlockingResources = GetInt(step, "maxHeadBlockingResources") ?? GetInt(step, "max-head-blocking") ?? new WebAuditOptions().MaxHeadBlockingResources;
            var rendered = GetBool(step, "rendered") ?? false;
            var renderedEngine = GetString(step, "renderedEngine");
            var renderedEnsureInstalled = GetBool(step, "renderedEnsureInstalled");
            var renderedHeadless = GetBool(step, "renderedHeadless") ?? true;
            var renderedBaseUrl = GetString(step, "renderedBaseUrl");
            var renderedHost = GetString(step, "renderedHost");
            var renderedPort = GetInt(step, "renderedPort") ?? 0;
            var renderedServe = GetBool(step, "renderedServe") ?? true;
            var renderedMaxPages = GetInt(step, "renderedMaxPages") ?? 20;
            var renderedTimeoutMs = GetInt(step, "renderedTimeoutMs") ?? 30000;
            var renderedCheckErrors = GetBool(step, "renderedCheckConsoleErrors") ?? true;
            var renderedCheckWarnings = GetBool(step, "renderedCheckConsoleWarnings") ?? true;
            var renderedCheckFailures = GetBool(step, "renderedCheckFailedRequests") ?? true;
            var renderedInclude = GetString(step, "renderedInclude");
            var renderedExclude = GetString(step, "renderedExclude");

            var requiredRouteList = CliPatternHelper.SplitPatterns(requiredRoutes).ToList();
            if (requiredRouteList.Count == 0)
                requiredRouteList.Add("/404.html");
            var navRequiredLinksList = CliPatternHelper.SplitPatterns(navRequiredLinks).ToList();
            if (navRequiredLinksList.Count == 0)
                navRequiredLinksList.Add("/");

            var ignoreNavList = CliPatternHelper.SplitPatterns(ignoreNav).ToList();
            var ignoreNavPatterns = BuildIgnoreNavPatternsForPipeline(ignoreNavList, useDefaultIgnoreNav);
            var ignoreMediaList = CliPatternHelper.SplitPatterns(ignoreMedia).ToList();
            var ignoreMediaPatterns = BuildIgnoreMediaPatternsForPipeline(ignoreMediaList, useDefaultIgnoreMedia);
            var navRequiredValue = navRequired ?? !(navOptional ?? false);
            var navIgnorePrefixList = CliPatternHelper.SplitPatterns(navIgnorePrefixes);
            var navProfiles = LoadAuditNavProfilesForPipeline(baseDir, navProfilesPath);
            var mediaProfiles = LoadAuditMediaProfilesForPipeline(baseDir, mediaProfilesPath);
            var suppressIssues = GetArrayOfStrings(step, "suppressIssues") ?? GetArrayOfStrings(step, "suppress-issues");
            var resolvedSummaryPath = ResolveSummaryPathForPipeline(summary, summaryPath);
            if (string.IsNullOrWhiteSpace(resolvedSummaryPath) && summaryOnFail)
                resolvedSummaryPath = ".powerforge/audit-summary.json";

            var resolvedSarifPath = ResolveSarifPathForPipeline(sarif, sarifPath);
            if (string.IsNullOrWhiteSpace(resolvedSarifPath) && sarifOnFail)
                resolvedSarifPath = ".powerforge/audit.sarif.json";

            if ((baselineGenerate || baselineUpdate) && string.IsNullOrWhiteSpace(baselinePath))
                baselinePath = ".powerforge/audit-baseline.json";

            if (fast && rendered)
            {
                rendered = false;
            }

            var ensureInstall = rendered && (renderedEnsureInstalled ?? true);

            audit = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = effectiveSiteRoot!,
                BaselineRoot = baseDir,
                Include = CliPatternHelper.SplitPatterns(include),
                Exclude = CliPatternHelper.SplitPatterns(exclude),
                UseDefaultExcludes = useDefaultExclude,
                MaxHtmlFiles = GetInt(step, "maxHtmlFiles") ?? GetInt(step, "max-html-files") ?? 0,
                MaxTotalFiles = GetInt(step, "maxTotalFiles") ?? GetInt(step, "max-total-files") ?? 0,
                SuppressIssues = suppressIssues ?? Array.Empty<string>(),
                IgnoreNavFor = ignoreNavPatterns,
                IgnoreMediaFor = ignoreMediaPatterns,
                NavSelector = navSelector,
                NavRequired = navRequiredValue,
                NavIgnorePrefixes = navIgnorePrefixList,
                NavRequiredLinks = navRequiredLinksList.ToArray(),
                NavProfiles = navProfiles,
                MediaProfiles = mediaProfiles,
                MinNavCoveragePercent = minNavCoveragePercent,
                RequiredRoutes = requiredRouteList.ToArray(),
                CheckLinks = GetBool(step, "checkLinks") ?? true,
                CheckAssets = GetBool(step, "checkAssets") ?? true,
                CheckNavConsistency = GetBool(step, "checkNav") ?? true,
                CheckTitles = GetBool(step, "checkTitles") ?? true,
                CheckDuplicateIds = GetBool(step, "checkDuplicateIds") ?? true,
                CheckHtmlStructure = GetBool(step, "checkHtmlStructure") ?? true,
                SummaryPath = resolvedSummaryPath,
                SarifPath = resolvedSarifPath,
                SummaryMaxIssues = summaryMax,
                SummaryOnFailOnly = summaryOnFail && !summary,
                SarifOnFailOnly = sarifOnFail && !sarif,
                BaselinePath = baselinePath,
                FailOnWarnings = failOnWarnings,
                FailOnNewIssues = failOnNewIssues,
                MaxErrors = maxErrors,
                MaxWarnings = maxWarnings,
                FailOnCategories = CliPatternHelper.SplitPatterns(failOnCategories),
                NavCanonicalPath = navCanonicalPath,
                NavCanonicalSelector = navCanonicalSelector,
                NavCanonicalRequired = navCanonicalRequired,
                CheckUtf8 = checkUtf8,
                CheckMetaCharset = checkMetaCharset,
                CheckUnicodeReplacementChars = checkReplacement,
                CheckHeadingOrder = checkHeadingOrder,
                CheckLinkPurposeConsistency = checkLinkPurpose,
                CheckMediaEmbeds = checkMediaEmbeds,
                CheckNetworkHints = checkNetworkHints,
                CheckRenderBlockingResources = checkRenderBlocking,
                MaxHeadBlockingResources = maxHeadBlockingResources,
                CheckRendered = rendered,
                RenderedEngine = renderedEngine ?? "Chromium",
                RenderedEnsureInstalled = ensureInstall,
                RenderedHeadless = renderedHeadless,
                RenderedBaseUrl = renderedBaseUrl,
                RenderedServe = renderedServe,
                RenderedServeHost = string.IsNullOrWhiteSpace(renderedHost) ? "localhost" : renderedHost,
                RenderedServePort = renderedPort,
                RenderedMaxPages = renderedMaxPages,
                RenderedTimeoutMs = renderedTimeoutMs,
                RenderedCheckConsoleErrors = renderedCheckErrors,
                RenderedCheckConsoleWarnings = renderedCheckWarnings,
                RenderedCheckFailedRequests = renderedCheckFailures,
                RenderedInclude = CliPatternHelper.SplitPatterns(renderedInclude),
                RenderedExclude = CliPatternHelper.SplitPatterns(renderedExclude)
            });

            if (baselineGenerate || baselineUpdate)
            {
                var written = WebAuditBaselineStore.Write(baseDir, baselinePath, audit, baselineUpdate, logger: null);
                audit.BaselinePath = written;
            }

            if (!audit.Success)
                throw new InvalidOperationException(BuildAuditFailureSummary(audit, GetInt(step, "errorPreviewCount") ?? 5));
        }

        stepResult.Success = true;
        stepResult.Message = BuildDoctorSummary(verify, audit, executeBuild, executeVerify, executeAudit, verifyPolicyFailures);
    }

    private static void ExecuteDotNetBuild(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var project = ResolvePath(baseDir, GetString(step, "project") ?? GetString(step, "solution") ?? GetString(step, "path"));
        var configuration = GetString(step, "configuration");
        var framework = GetString(step, "framework");
        var runtime = GetString(step, "runtime");
        var noRestore = GetBool(step, "noRestore") ?? false;
        var skipIfProjectMissing = GetBool(step, "skipIfProjectMissing") ??
                                   GetBool(step, "skipIfMissingProject") ??
                                   GetBool(step, "skip-if-project-missing") ??
                                   false;
        if (string.IsNullOrWhiteSpace(project))
            throw new InvalidOperationException("dotnet-build requires project.");

        if (TrySkipDotNetStepForMissingProject(project, skipIfProjectMissing, stepResult, "dotnet build"))
            return;

        var res = WebDotNetRunner.Build(new WebDotNetBuildOptions
        {
            ProjectOrSolution = project,
            Configuration = configuration,
            Framework = framework,
            Runtime = runtime,
            Restore = !noRestore
        });
        var buildError = string.IsNullOrWhiteSpace(res.Error) ? res.Output : res.Error;
        stepResult.Success = res.Success;
        stepResult.Message = res.Success ? "dotnet build ok" : buildError;
        if (!res.Success)
            throw new InvalidOperationException(buildError);
    }

    private static void ExecuteDotNetPublish(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var project = ResolvePath(baseDir, GetString(step, "project"));
        var outPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output"));
        var cleanOutput = GetBool(step, "clean") ?? false;
        var configuration = GetString(step, "configuration");
        var framework = GetString(step, "framework");
        var runtime = GetString(step, "runtime");
        var selfContained = GetBool(step, "selfContained") ?? false;
        var noBuild = GetBool(step, "noBuild") ?? false;
        var noRestore = GetBool(step, "noRestore") ?? false;
        var baseHref = GetString(step, "baseHref");
        var defineConstants = GetString(step, "defineConstants") ?? GetString(step, "define-constants");
        var noBlazorFixes = GetBool(step, "noBlazorFixes") ?? false;
        var blazorFixes = GetBool(step, "blazorFixes") ?? !noBlazorFixes;
        var skipIfProjectMissing = GetBool(step, "skipIfProjectMissing") ??
                                   GetBool(step, "skipIfMissingProject") ??
                                   GetBool(step, "skip-if-project-missing") ??
                                   false;

        if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(outPath))
            throw new InvalidOperationException("dotnet-publish requires project and out.");

        if (TrySkipDotNetStepForMissingProject(project, skipIfProjectMissing, stepResult, "dotnet publish"))
            return;

        if (cleanOutput)
            WebCliFileSystem.CleanOutputDirectory(outPath);

        var res = WebDotNetRunner.Publish(new WebDotNetPublishOptions
        {
            ProjectPath = project,
            OutputPath = outPath,
            Configuration = configuration,
            Framework = framework,
            Runtime = runtime,
            SelfContained = selfContained,
            NoBuild = noBuild,
            NoRestore = noRestore,
            DefineConstants = defineConstants
        });

        var publishError = string.IsNullOrWhiteSpace(res.Error) ? res.Output : res.Error;
        if (!res.Success)
            throw new InvalidOperationException(publishError);
        if (blazorFixes)
        {
            WebBlazorPublishFixer.Apply(new WebBlazorPublishFixOptions
            {
                PublishRoot = outPath,
                BaseHref = baseHref
            });
        }

        stepResult.Success = true;
        stepResult.Message = "dotnet publish ok";
    }

    private static bool TrySkipDotNetStepForMissingProject(string project, bool skipIfProjectMissing, WebPipelineStepResult stepResult, string taskLabel)
    {
        if (!skipIfProjectMissing)
            return false;

        if (File.Exists(project) || Directory.Exists(project))
            return false;

        stepResult.Success = true;
        stepResult.Message = $"{taskLabel} skipped: project path not found '{project}'";
        return true;
    }

    private static void ExecuteOverlay(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var source = ResolvePath(baseDir, GetString(step, "source"));
        var destination = ResolvePath(baseDir, GetString(step, "destination") ?? GetString(step, "dest"));
        var include = GetString(step, "include");
        var exclude = GetString(step, "exclude");
        var clean = GetBool(step, "clean") ?? false;
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
            throw new InvalidOperationException("overlay requires source and destination.");

        var res = WebStaticOverlay.Apply(new WebStaticOverlayOptions
        {
            SourceRoot = source,
            DestinationRoot = destination,
            Clean = clean,
            Include = CliPatternHelper.SplitPatterns(include),
            Exclude = CliPatternHelper.SplitPatterns(exclude)
        });
        stepResult.Success = true;
        stepResult.Message = $"overlay {res.CopiedCount} files";
    }
}
