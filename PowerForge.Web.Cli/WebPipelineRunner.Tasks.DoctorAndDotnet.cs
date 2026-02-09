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
            var baselineKeys = (!string.IsNullOrWhiteSpace(verifyBaselinePath) || verifyBaselineGenerate || verifyBaselineUpdate || verifyFailOnNewWarnings)
                ? WebVerifyBaselineStore.LoadWarningKeysSafe(plan.RootPath, verifyBaselinePath)
                : Array.Empty<string>();
            var baselineSet = baselineKeys.Length > 0 ? new HashSet<string>(baselineKeys, StringComparer.OrdinalIgnoreCase) : null;
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
                if (baselineKeys.Length == 0)
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
            var navIgnorePrefixes = GetString(step, "navIgnorePrefixes") ?? GetString(step, "nav-ignore-prefixes") ??
                                    GetString(step, "navIgnorePrefix") ?? GetString(step, "nav-ignore-prefix");
            var navRequiredLinks = GetString(step, "navRequiredLinks") ?? GetString(step, "nav-required-links") ??
                                   GetString(step, "navRequiredLink") ?? GetString(step, "nav-required-link");
            var navProfilesPath = GetString(step, "navProfiles") ?? GetString(step, "nav-profiles");
            var requiredRoutes = GetString(step, "requiredRoutes") ?? GetString(step, "required-routes") ??
                                 GetString(step, "requiredRoute") ?? GetString(step, "required-route");
            var navSelector = GetString(step, "navSelector") ?? GetString(step, "nav-selector") ?? "nav";
            var navRequired = GetBool(step, "navRequired");
            var navOptional = GetBool(step, "navOptional");
            var minNavCoveragePercent = GetInt(step, "minNavCoveragePercent") ?? GetInt(step, "min-nav-coverage") ?? 0;
            var useDefaultExclude = !(GetBool(step, "noDefaultExclude") ?? false);
            var useDefaultIgnoreNav = !(GetBool(step, "noDefaultIgnoreNav") ?? false);
            var summary = GetBool(step, "summary") ?? false;
            var summaryPath = GetString(step, "summaryPath");
            var summaryMax = GetInt(step, "summaryMaxIssues") ?? 10;
            var summaryOnFail = GetBool(step, "summaryOnFail") ?? GetBool(step, "summary-on-fail") ?? true;
            var sarif = GetBool(step, "sarif") ?? false;
            var sarifPath = GetString(step, "sarifPath") ?? GetString(step, "sarif-path");
            var sarifOnFail = GetBool(step, "sarifOnFail") ?? GetBool(step, "sarif-on-fail") ?? true;
            var navCanonicalPath = GetString(step, "navCanonicalPath") ?? GetString(step, "navCanonical");
            var navCanonicalSelector = GetString(step, "navCanonicalSelector");
            var navCanonicalRequired = GetBool(step, "navCanonicalRequired") ?? false;
            var checkUtf8 = GetBool(step, "checkUtf8") ?? true;
            var checkMetaCharset = GetBool(step, "checkMetaCharset") ?? true;
            var checkReplacement = GetBool(step, "checkUnicodeReplacementChars") ?? true;
            var checkHeadingOrder = GetBool(step, "checkHeadingOrder") ?? true;
            var checkLinkPurpose = GetBool(step, "checkLinkPurposeConsistency") ?? GetBool(step, "checkLinkPurpose") ?? true;
            var checkNetworkHints = GetBool(step, "checkNetworkHints") ?? true;
            var checkRenderBlocking = GetBool(step, "checkRenderBlockingResources") ?? GetBool(step, "checkRenderBlocking") ?? true;
            var maxHeadBlockingResources = GetInt(step, "maxHeadBlockingResources") ?? GetInt(step, "max-head-blocking") ?? new WebAuditOptions().MaxHeadBlockingResources;

            var requiredRouteList = CliPatternHelper.SplitPatterns(requiredRoutes).ToList();
            if (requiredRouteList.Count == 0)
                requiredRouteList.Add("/404.html");
            var navRequiredLinksList = CliPatternHelper.SplitPatterns(navRequiredLinks).ToList();
            if (navRequiredLinksList.Count == 0)
                navRequiredLinksList.Add("/");

            var ignoreNavList = CliPatternHelper.SplitPatterns(ignoreNav).ToList();
            var ignoreNavPatterns = BuildIgnoreNavPatternsForPipeline(ignoreNavList, useDefaultIgnoreNav);
            var navRequiredValue = navRequired ?? !(navOptional ?? false);
            var navIgnorePrefixList = CliPatternHelper.SplitPatterns(navIgnorePrefixes);
            var navProfiles = LoadAuditNavProfilesForPipeline(baseDir, navProfilesPath);
            var suppressIssues = GetArrayOfStrings(step, "suppressIssues") ?? GetArrayOfStrings(step, "suppress-issues");
            var resolvedSummaryPath = ResolveSummaryPathForPipeline(summary, summaryPath);
            if (string.IsNullOrWhiteSpace(resolvedSummaryPath) && summaryOnFail)
                resolvedSummaryPath = ".powerforge/audit-summary.json";

            var resolvedSarifPath = ResolveSarifPathForPipeline(sarif, sarifPath);
            if (string.IsNullOrWhiteSpace(resolvedSarifPath) && sarifOnFail)
                resolvedSarifPath = ".powerforge/audit.sarif.json";

            audit = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = effectiveSiteRoot!,
                Include = CliPatternHelper.SplitPatterns(include),
                Exclude = CliPatternHelper.SplitPatterns(exclude),
                UseDefaultExcludes = useDefaultExclude,
                MaxTotalFiles = GetInt(step, "maxTotalFiles") ?? GetInt(step, "max-total-files") ?? 0,
                SuppressIssues = suppressIssues ?? Array.Empty<string>(),
                IgnoreNavFor = ignoreNavPatterns,
                NavSelector = navSelector,
                NavRequired = navRequiredValue,
                NavIgnorePrefixes = navIgnorePrefixList,
                NavRequiredLinks = navRequiredLinksList.ToArray(),
                NavProfiles = navProfiles,
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
                NavCanonicalPath = navCanonicalPath,
                NavCanonicalSelector = navCanonicalSelector,
                NavCanonicalRequired = navCanonicalRequired,
                CheckUtf8 = checkUtf8,
                CheckMetaCharset = checkMetaCharset,
                CheckUnicodeReplacementChars = checkReplacement,
                CheckHeadingOrder = checkHeadingOrder,
                CheckLinkPurposeConsistency = checkLinkPurpose,
                CheckNetworkHints = checkNetworkHints,
                CheckRenderBlockingResources = checkRenderBlocking,
                MaxHeadBlockingResources = maxHeadBlockingResources
            });

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
        if (string.IsNullOrWhiteSpace(project))
            throw new InvalidOperationException("dotnet-build requires project.");

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

        if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(outPath))
            throw new InvalidOperationException("dotnet-publish requires project and out.");
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
