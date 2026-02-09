using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecuteAudit(
        JsonElement step,
        string label,
        string baseDir,
        bool fast,
        WebConsoleLogger? logger,
        string lastBuildOutPath,
        string[] lastBuildUpdatedFiles,
        WebPipelineStepResult stepResult)
    {
        var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
        if (string.IsNullOrWhiteSpace(siteRoot))
            throw new InvalidOperationException("audit requires siteRoot.");

        var include = GetString(step, "include");
        var exclude = GetString(step, "exclude");
        var includeScopeFromBuildUpdated = GetBool(step, "scopeFromBuildUpdated") ?? GetBool(step, "scope-from-build-updated");
        var ignoreNav = GetString(step, "ignoreNav") ?? GetString(step, "ignore-nav");
        var navIgnorePrefixes = GetString(step, "navIgnorePrefixes") ?? GetString(step, "nav-ignore-prefixes") ??
                                GetString(step, "navIgnorePrefix") ?? GetString(step, "nav-ignore-prefix");
        var navRequiredLinks = GetString(step, "navRequiredLinks") ?? GetString(step, "nav-required-links") ??
                               GetString(step, "navRequiredLink") ?? GetString(step, "nav-required-link");
        var navProfilesPath = GetString(step, "navProfiles") ?? GetString(step, "nav-profiles");
        var minNavCoveragePercent = GetInt(step, "minNavCoveragePercent") ?? GetInt(step, "min-nav-coverage") ?? 0;
        var requiredRoutes = GetString(step, "requiredRoutes") ?? GetString(step, "required-routes") ??
                             GetString(step, "requiredRoute") ?? GetString(step, "required-route");
        var navSelector = GetString(step, "navSelector") ?? GetString(step, "nav-selector") ?? "nav";
        var navRequired = GetBool(step, "navRequired");
        var navOptional = GetBool(step, "navOptional");
        var checkLinks = GetBool(step, "checkLinks") ?? true;
        var checkAssets = GetBool(step, "checkAssets") ?? true;
        var checkNav = GetBool(step, "checkNav") ?? true;
        var checkTitles = GetBool(step, "checkTitles") ?? true;
        var checkIds = GetBool(step, "checkDuplicateIds") ?? true;
        var checkHeadingOrder = GetBool(step, "checkHeadingOrder") ?? true;
        var checkLinkPurpose = GetBool(step, "checkLinkPurposeConsistency") ?? GetBool(step, "checkLinkPurpose") ?? true;
        var checkStructure = GetBool(step, "checkHtmlStructure") ?? true;
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
        var checkNetworkHints = GetBool(step, "checkNetworkHints");
        var checkRenderBlocking = GetBool(step, "checkRenderBlockingResources") ?? GetBool(step, "checkRenderBlocking");
        var maxHeadBlockingResources = GetInt(step, "maxHeadBlockingResources") ?? GetInt(step, "max-head-blocking");
        var maxHtmlFiles = GetInt(step, "maxHtmlFiles") ?? GetInt(step, "max-html-files") ?? 0;
        var maxTotalFiles = GetInt(step, "maxTotalFiles") ?? GetInt(step, "max-total-files") ?? 0;
        var suppressIssues = GetArrayOfStrings(step, "suppressIssues") ?? GetArrayOfStrings(step, "suppress-issues");

        if ((baselineGenerate || baselineUpdate) && string.IsNullOrWhiteSpace(baselinePath))
            baselinePath = ".powerforge/audit-baseline.json";

        var useDefaultExclude = !(GetBool(step, "noDefaultExclude") ?? false);
        var useDefaultIgnoreNav = !(GetBool(step, "noDefaultIgnoreNav") ?? false);
        var ignoreNavList = CliPatternHelper.SplitPatterns(ignoreNav).ToList();
        var ignoreNavPatterns = BuildIgnoreNavPatternsForPipeline(ignoreNavList, useDefaultIgnoreNav);
        var navRequiredValue = navRequired ?? !(navOptional ?? false);
        var navIgnorePrefixList = CliPatternHelper.SplitPatterns(navIgnorePrefixes);
        var navProfiles = LoadAuditNavProfilesForPipeline(baseDir, navProfilesPath);
        var resolvedSummaryPath = ResolveSummaryPathForPipeline(summary, summaryPath);
        if (string.IsNullOrWhiteSpace(resolvedSummaryPath) && summaryOnFail)
            resolvedSummaryPath = ".powerforge/audit-summary.json";

        var resolvedSarifPath = ResolveSarifPathForPipeline(sarif, sarifPath);
        if (string.IsNullOrWhiteSpace(resolvedSarifPath) && sarifOnFail)
            resolvedSarifPath = ".powerforge/audit.sarif.json";

        if (includeScopeFromBuildUpdated != false &&
            (includeScopeFromBuildUpdated == true || fast) &&
            string.IsNullOrWhiteSpace(include) &&
            lastBuildUpdatedFiles.Length > 0 &&
            string.Equals(Path.GetFullPath(siteRoot), lastBuildOutPath, FileSystemPathComparison))
        {
            var updatedHtml = lastBuildUpdatedFiles
                .Where(static p => p.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                                   p.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (updatedHtml.Length > 0)
            {
                include = string.Join(";", updatedHtml);
                var modeLabel = fast ? "fast incremental" : "incremental";
                logger?.Info($"{label}: {modeLabel} html scope: {updatedHtml.Length} updated page(s)");
            }
        }

        if (fast)
        {
            var forced = new List<string>();
            if (rendered)
            {
                rendered = false;
                forced.Add("rendered=false");
            }
            if (maxHtmlFiles <= 0)
            {
                // Static audit is usually cheaper than optimize; allow a wider default
                // scope while still keeping large sites manageable during local iteration.
                maxHtmlFiles = 200;
                forced.Add("maxHtmlFiles=200");
            }
            if (forced.Count > 0)
                logger?.Warn($"{label}: fast mode overrides: {string.Join(", ", forced)}");
        }

        var ensureInstall = rendered && (renderedEnsureInstalled ?? true);
        var audit = WebSiteAuditor.Audit(new WebAuditOptions
        {
            SiteRoot = siteRoot,
            BaselineRoot = baseDir,
            Include = CliPatternHelper.SplitPatterns(include),
            Exclude = CliPatternHelper.SplitPatterns(exclude),
            UseDefaultExcludes = useDefaultExclude,
            MaxHtmlFiles = Math.Max(0, maxHtmlFiles),
            MaxTotalFiles = Math.Max(0, maxTotalFiles),
            SuppressIssues = suppressIssues ?? Array.Empty<string>(),
            IgnoreNavFor = ignoreNavPatterns,
            NavSelector = navSelector,
            NavRequired = navRequiredValue,
            NavIgnorePrefixes = navIgnorePrefixList,
            NavRequiredLinks = CliPatternHelper.SplitPatterns(navRequiredLinks),
            NavProfiles = navProfiles,
            MinNavCoveragePercent = minNavCoveragePercent,
            RequiredRoutes = CliPatternHelper.SplitPatterns(requiredRoutes),
            CheckLinks = checkLinks,
            CheckAssets = checkAssets,
            CheckNavConsistency = checkNav,
            CheckTitles = checkTitles,
            CheckDuplicateIds = checkIds,
            CheckHeadingOrder = checkHeadingOrder,
            CheckLinkPurposeConsistency = checkLinkPurpose,
            CheckHtmlStructure = checkStructure,
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
            RenderedExclude = CliPatternHelper.SplitPatterns(renderedExclude),
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
            CheckNetworkHints = checkNetworkHints ?? true,
            CheckRenderBlockingResources = checkRenderBlocking ?? true,
            MaxHeadBlockingResources = maxHeadBlockingResources ?? new WebAuditOptions().MaxHeadBlockingResources
        });

        string? baselineWrittenPath = null;
        if (baselineGenerate || baselineUpdate)
        {
            baselineWrittenPath = WebAuditBaselineStore.Write(baseDir, baselinePath, audit, baselineUpdate, logger);
            audit.BaselinePath = baselineWrittenPath;
        }

        stepResult.Success = audit.Success;
        stepResult.Message = audit.Success
            ? BuildAuditSummary(audit)
            : BuildAuditFailureSummary(audit, GetInt(step, "errorPreviewCount") ?? 5);

        if (!string.IsNullOrWhiteSpace(baselineWrittenPath))
            stepResult.Message += $", baseline {baselineWrittenPath}";

        if (!audit.Success)
            throw new InvalidOperationException(stepResult.Message);
    }
}
