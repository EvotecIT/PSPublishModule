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
        var budgetExclude = GetString(step, "budgetExclude") ?? GetString(step, "budget-exclude");
        var includeScopeFromBuildUpdated = GetBool(step, "scopeFromBuildUpdated") ?? GetBool(step, "scope-from-build-updated");
        var ignoreNav = GetString(step, "ignoreNav") ?? GetString(step, "ignore-nav");
        var ignoreMedia = GetString(step, "ignoreMedia") ?? GetString(step, "ignore-media");
        var navIgnorePrefixes = GetString(step, "navIgnorePrefixes") ?? GetString(step, "nav-ignore-prefixes") ??
                                GetString(step, "navIgnorePrefix") ?? GetString(step, "nav-ignore-prefix");
        var navRequiredLinks = GetString(step, "navRequiredLinks") ?? GetString(step, "nav-required-links") ??
                               GetString(step, "navRequiredLink") ?? GetString(step, "nav-required-link");
        var navProfilesPath = GetString(step, "navProfiles") ?? GetString(step, "nav-profiles");
        var mediaProfilesPath = GetString(step, "mediaProfiles") ?? GetString(step, "media-profiles");
        var minNavCoveragePercent = GetInt(step, "minNavCoveragePercent") ?? GetInt(step, "min-nav-coverage") ?? 0;
        var requiredRoutes = GetString(step, "requiredRoutes") ?? GetString(step, "required-routes") ??
                             GetString(step, "requiredRoute") ?? GetString(step, "required-route");
        var forbiddenRoutes = GetString(step, "forbiddenRoutes") ?? GetString(step, "forbidden-routes") ??
                              GetString(step, "forbiddenRoute") ?? GetString(step, "forbidden-route");
        var navSelector = GetString(step, "navSelector") ?? GetString(step, "nav-selector") ?? "nav";
        var navRequired = GetBool(step, "navRequired");
        var navOptional = GetBool(step, "navOptional");
        var checkLinks = GetBool(step, "checkLinks") ?? true;
        var checkAssets = GetBool(step, "checkAssets") ?? true;
        var checkNav = GetBool(step, "checkNav") ?? true;
        var checkTitles = GetBool(step, "checkTitles") ?? true;
        var checkIds = GetBool(step, "checkDuplicateIds") ?? true;
        var checkHeadingOrder = GetBool(step, "checkHeadingOrder") ?? true;
        var checkSeoMeta = GetBool(step, "checkSeoMeta") ?? GetBool(step, "check-seo-meta") ?? false;
        var checkLinkPurpose = GetBool(step, "checkLinkPurposeConsistency") ?? GetBool(step, "checkLinkPurpose") ?? true;
        var checkMediaEmbeds = GetBool(step, "checkMediaEmbeds") ?? GetBool(step, "checkMedia") ?? true;
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
        var renderedCheckContrast = GetBool(step, "renderedCheckContrast") ?? false;
        var renderedContrastMinRatio = GetDouble(step, "renderedContrastMinRatio") ?? 4.5d;
        var renderedContrastMaxFindings = GetInt(step, "renderedContrastMaxFindings") ?? 10;
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
        var failOnIssueCodes = GetString(step, "failOnIssueCodes") ?? GetString(step, "failIssueCodes") ??
                               GetString(step, "failOnIssues") ?? GetString(step, "failIssues");
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
        var maxFileBytes = GetLong(step, "maxFileBytes") ?? GetLong(step, "max-file-bytes") ?? 0;
        var suppressIssues = GetArrayOfStrings(step, "suppressIssues") ?? GetArrayOfStrings(step, "suppress-issues");
        var checkAgentContentSecurity = GetStrictAgentBool(
            step, false, "checkAgentContentSecurity", "check-agent-content-security");
        var agentContentFiles = GetStrictAgentStringArray(
            step, "agentContentFiles", "agent-content-files");
        var agentPublicationCatalog = ResolvePath(baseDir,
            GetStrictAgentString(step, "agentPublicationCatalog", "agent-publication-catalog"));
        var agentPublicationCatalogMaxAgeHours = GetStrictAgentInt(
            step, 0, 0, "agentPublicationCatalogMaxAgeHours", "agent-publication-catalog-max-age-hours");
        var agentNuGetOwner = GetStrictAgentString(step, "agentNuGetOwner", "agent-nuget-owner");
        var agentPowerShellGalleryOwner = GetStrictAgentString(
            step, "agentPowerShellGalleryOwner", "agent-powershell-gallery-owner");
        var agentRequireOwnerVerification = GetStrictAgentStringArray(
            step, "agentRequireOwnerVerification", "agent-require-owner-verification");
        var agentRegistryVerifiedPackages = GetStrictAgentStringArray(
            step, "agentRegistryVerifiedPackages", "agent-registry-verified-packages");
        var agentVerifyPackages = GetStrictAgentBool(
            step, true, "agentVerifyPackages", "agent-verify-packages");
        var agentVerifyExternalHosts = GetStrictAgentBool(
            step, false, "agentVerifyExternalHosts", "agent-verify-external-hosts");
        var agentTrustedDomains = GetStrictAgentStringArray(
            step, "agentTrustedDomains", "agent-trusted-domains");
        var agentRequestTimeoutSeconds = GetStrictAgentInt(
            step, 15, 1, "agentRequestTimeoutSeconds", "agent-request-timeout-seconds");
        var agentMaxArtifactBytes = GetStrictAgentLong(
            step, 5 * 1024 * 1024, 1, "agentMaxArtifactBytes", "agent-max-artifact-bytes");
        var agentMaxPackageReferences = GetStrictAgentInt(
            step, 100, 1, "agentMaxPackageReferences", "agent-max-package-references");
        var agentMaxExternalHosts = GetStrictAgentInt(
            step, 100, 1, "agentMaxExternalHosts", "agent-max-external-hosts");
        var agentMaxRegistryResponseBytes = GetStrictAgentLong(
            step, 2 * 1024 * 1024, 1, "agentMaxRegistryResponseBytes", "agent-max-registry-response-bytes");
        var agentMaxNetworkDurationSeconds = GetStrictAgentInt(
            step, 120, 1, "agentMaxNetworkDurationSeconds", "agent-max-network-duration-seconds");
        var agentCheckPromptInjection = GetStrictAgentBool(
            step, true, "agentCheckPromptInjection", "agent-check-prompt-injection");

        EnforceExplicitAuditCheckContract(step, "audit");

        if ((baselineGenerate || baselineUpdate) && string.IsNullOrWhiteSpace(baselinePath))
            baselinePath = ".powerforge/audit-baseline.json";

        var useDefaultExclude = !(GetBool(step, "noDefaultExclude") ?? false);
        var useDefaultIgnoreNav = !(GetBool(step, "noDefaultIgnoreNav") ?? false);
        var useDefaultIgnoreMedia = !(GetBool(step, "noDefaultIgnoreMedia") ?? false);
        var ignoreNavList = CliPatternHelper.SplitPatterns(ignoreNav).ToList();
        var ignoreNavPatterns = BuildIgnoreNavPatternsForPipeline(ignoreNavList, useDefaultIgnoreNav);
        var ignoreMediaList = CliPatternHelper.SplitPatterns(ignoreMedia).ToList();
        var ignoreMediaPatterns = BuildIgnoreMediaPatternsForPipeline(ignoreMediaList, useDefaultIgnoreMedia);
        var navRequiredValue = navRequired ?? !(navOptional ?? false);
        var navIgnorePrefixList = CliPatternHelper.SplitPatterns(navIgnorePrefixes);
        var navProfiles = LoadAuditNavProfilesForPipeline(baseDir, navProfilesPath);
        var mediaProfiles = LoadAuditMediaProfilesForPipeline(baseDir, mediaProfilesPath);
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
            MaxFileBytes = Math.Max(0, maxFileBytes),
            BudgetExclude = CliPatternHelper.SplitPatterns(budgetExclude),
            SuppressIssues = suppressIssues ?? Array.Empty<string>(),
            IgnoreNavFor = ignoreNavPatterns,
            IgnoreMediaFor = ignoreMediaPatterns,
            NavSelector = navSelector,
            NavRequired = navRequiredValue,
            NavIgnorePrefixes = navIgnorePrefixList,
            NavRequiredLinks = CliPatternHelper.SplitPatterns(navRequiredLinks),
            NavProfiles = navProfiles,
            MediaProfiles = mediaProfiles,
            MinNavCoveragePercent = minNavCoveragePercent,
            RequiredRoutes = CliPatternHelper.SplitPatterns(requiredRoutes),
            ForbiddenRoutes = CliPatternHelper.SplitPatterns(forbiddenRoutes),
            CheckLinks = checkLinks,
            CheckAssets = checkAssets,
            CheckNavConsistency = checkNav,
            CheckTitles = checkTitles,
            CheckDuplicateIds = checkIds,
            CheckHeadingOrder = checkHeadingOrder,
            CheckSeoMeta = checkSeoMeta,
            CheckLinkPurposeConsistency = checkLinkPurpose,
            CheckMediaEmbeds = checkMediaEmbeds,
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
            RenderedCheckContrast = renderedCheckContrast,
            RenderedContrastMinRatio = renderedContrastMinRatio,
            RenderedContrastMaxFindings = Math.Clamp(renderedContrastMaxFindings, 1, 200),
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
            FailOnIssueCodes = CliPatternHelper.SplitPatterns(failOnIssueCodes),
            NavCanonicalPath = navCanonicalPath,
            NavCanonicalSelector = navCanonicalSelector,
            NavCanonicalRequired = navCanonicalRequired,
            CheckUtf8 = checkUtf8,
            CheckMetaCharset = checkMetaCharset,
            CheckUnicodeReplacementChars = checkReplacement,
            CheckNetworkHints = checkNetworkHints ?? true,
            CheckRenderBlockingResources = checkRenderBlocking ?? true,
            MaxHeadBlockingResources = maxHeadBlockingResources ?? new WebAuditOptions().MaxHeadBlockingResources,
            AgentContentSecurity = checkAgentContentSecurity
                ? new WebAgentContentSecurityOptions
                {
                    SiteRoot = siteRoot,
                    Files = agentContentFiles ?? new[] { "llms.txt", "llms-full.txt", "llms.json" },
                    PublicationCatalogPath = agentPublicationCatalog,
                    PublicationCatalogMaxAgeHours = agentPublicationCatalogMaxAgeHours,
                    NuGetOwner = agentNuGetOwner,
                    PowerShellGalleryOwner = agentPowerShellGalleryOwner,
                    RequireOwnerVerification = agentRequireOwnerVerification ??
                        BuildDefaultAgentOwnerSelectors(agentNuGetOwner, agentPowerShellGalleryOwner),
                    RegistryVerifiedPackages = agentRegistryVerifiedPackages ?? Array.Empty<string>(),
                    VerifyPackages = agentVerifyPackages,
                    VerifyExternalHosts = agentVerifyExternalHosts,
                    TrustedDomains = agentTrustedDomains ?? Array.Empty<string>(),
                    RequestTimeoutSeconds = agentRequestTimeoutSeconds,
                    MaxArtifactBytes = agentMaxArtifactBytes,
                    MaxPackageReferences = agentMaxPackageReferences,
                    MaxExternalHosts = agentMaxExternalHosts,
                    MaxRegistryResponseBytes = agentMaxRegistryResponseBytes,
                    MaxNetworkDurationSeconds = agentMaxNetworkDurationSeconds,
                    CheckPromptInjection = agentCheckPromptInjection
                }
                : null
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

    private static string[] BuildDefaultAgentOwnerSelectors(string? nuGetOwner, string? powerShellGalleryOwner)
    {
        var selectors = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(nuGetOwner))
            selectors.Add("nuget:*");
        if (!string.IsNullOrWhiteSpace(powerShellGalleryOwner))
            selectors.Add("powershellgallery:*");
        return selectors.ToArray();
    }

    private static int GetStrictAgentInt(
        JsonElement step,
        int fallback,
        int minimum,
        params string[] names)
    {
        foreach (var name in names)
        {
            if (!step.TryGetProperty(name, out var value))
                continue;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var parsed) || parsed < minimum)
                throw new InvalidOperationException($"{name} must be an integer greater than or equal to {minimum}.");
            return parsed;
        }
        return fallback;
    }

    private static long GetStrictAgentLong(
        JsonElement step,
        long fallback,
        long minimum,
        params string[] names)
    {
        foreach (var name in names)
        {
            if (!step.TryGetProperty(name, out var value))
                continue;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var parsed) || parsed < minimum)
                throw new InvalidOperationException($"{name} must be an integer greater than or equal to {minimum}.");
            return parsed;
        }
        return fallback;
    }

    private static bool GetStrictAgentBool(JsonElement step, bool fallback, params string[] names)
    {
        foreach (var name in names)
        {
            if (!step.TryGetProperty(name, out var value))
                continue;
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidOperationException($"{name} must be a boolean.");
            return value.GetBoolean();
        }
        return fallback;
    }

    private static string? GetStrictAgentString(JsonElement step, params string[] names)
    {
        foreach (var name in names)
        {
            if (!step.TryGetProperty(name, out var value))
                continue;
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                throw new InvalidOperationException($"{name} must be a non-empty string.");
            return value.GetString()!.Trim();
        }
        return null;
    }

    private static string[]? GetStrictAgentStringArray(JsonElement step, params string[] names)
    {
        foreach (var name in names)
        {
            if (!step.TryGetProperty(name, out var value))
                continue;
            if (value.ValueKind != JsonValueKind.Array ||
                value.EnumerateArray().Any(static item =>
                    item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString())))
            {
                throw new InvalidOperationException($"{name} must be an array of non-empty strings.");
            }
            return value.EnumerateArray().Select(static item => item.GetString()!.Trim()).ToArray();
        }
        return null;
    }
}
