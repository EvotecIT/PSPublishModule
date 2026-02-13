using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using PowerForge;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecuteApiDocs(
        JsonElement step,
        string label,
        string baseDir,
        bool fast,
        string effectiveMode,
        WebConsoleLogger? logger,
        WebPipelineStepResult stepResult)
    {
        var batchInputs = GetArrayOfObjects(step, "inputs") ?? GetArrayOfObjects(step, "entries");
        if (batchInputs is { Length: > 0 })
        {
            ExecuteApiDocsBatch(step, batchInputs, label, baseDir, fast, effectiveMode, logger, stepResult);
            return;
        }

        string? injectedCriticalCssHtml = null;

        var typeText = GetString(step, "type");
        var xml = ResolvePath(baseDir, GetString(step, "xml"));
        var help = ResolvePath(baseDir, GetString(step, "help") ?? GetString(step, "helpPath") ?? GetString(step, "help-path"));
        var outPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output"));
        var assembly = ResolvePath(baseDir, GetString(step, "assembly"));
        var title = GetString(step, "title");
        var baseUrl = GetString(step, "baseUrl") ?? GetString(step, "base-url") ?? "/api";
        var format = GetString(step, "format");
        var css = GetString(step, "css") ?? GetString(step, "cssHref") ?? GetString(step, "css-href");
        var criticalCssPath = ResolvePath(baseDir, GetString(step, "criticalCssPath") ?? GetString(step, "critical-css-path") ?? GetString(step, "criticalCss") ?? GetString(step, "critical-css"));
        var injectCriticalCss = GetBool(step, "injectCriticalCss") ?? GetBool(step, "inject-critical-css") ?? false;
        var header = ResolvePath(baseDir, GetString(step, "headerHtml") ?? GetString(step, "header-html"));
        var footer = ResolvePath(baseDir, GetString(step, "footerHtml") ?? GetString(step, "footer-html"));
        var template = GetString(step, "template");
        var templateRoot = ResolvePath(baseDir, GetString(step, "templateRoot") ?? GetString(step, "template-root"));
        var indexTemplate = ResolvePath(baseDir, GetString(step, "templateIndex") ?? GetString(step, "template-index"));
        var typeTemplate = ResolvePath(baseDir, GetString(step, "templateType") ?? GetString(step, "template-type"));
        var docsIndexTemplate = ResolvePath(baseDir, GetString(step, "templateDocsIndex") ?? GetString(step, "template-docs-index"));
        var docsTypeTemplate = ResolvePath(baseDir, GetString(step, "templateDocsType") ?? GetString(step, "template-docs-type"));
        var docsScript = ResolvePath(baseDir, GetString(step, "docsScript") ?? GetString(step, "docs-script"));
        var searchScript = ResolvePath(baseDir, GetString(step, "searchScript") ?? GetString(step, "search-script"));
        var docsHome = GetString(step, "docsHome") ?? GetString(step, "docsHomeUrl") ??
                       GetString(step, "docs-home") ?? GetString(step, "docs-home-url");
        var sidebar = GetString(step, "sidebar") ?? GetString(step, "sidebarPosition") ?? GetString(step, "sidebar-position");
        var bodyClass = GetString(step, "bodyClass") ?? GetString(step, "body-class");
        var sourceRoot = ResolvePath(baseDir, GetString(step, "sourceRoot") ?? GetString(step, "source-root"));
        var sourcePathPrefix = GetString(step, "sourcePathPrefix") ?? GetString(step, "source-path-prefix");
        var sourceUrl = GetString(step, "sourceUrl") ?? GetString(step, "source-url") ??
                        GetString(step, "sourcePattern") ?? GetString(step, "source-pattern");
        var coverageReportPath = ResolvePath(baseDir, GetString(step, "coverageReport") ?? GetString(step, "coverage-report") ?? GetString(step, "coverageReportPath") ?? GetString(step, "coverage-report-path"));
        var generateCoverageReport = GetBool(step, "generateCoverageReport") ?? GetBool(step, "generate-coverage-report") ?? true;
        var xrefMapPath = ResolvePath(baseDir, GetString(step, "xrefMap") ?? GetString(step, "xref-map") ?? GetString(step, "xrefMapPath") ?? GetString(step, "xref-map-path"));
        var generateXrefMap = GetBool(step, "generateXrefMap") ?? GetBool(step, "generate-xref-map") ?? true;
        var generateMemberXrefs = GetBool(step, "generateMemberXrefs") ?? GetBool(step, "generate-member-xrefs") ?? true;
        var memberXrefKindsText = GetString(step, "memberXrefKinds") ?? GetString(step, "member-xref-kinds");
        var memberXrefKindsArray = GetArrayOfStrings(step, "memberXrefKinds") ?? GetArrayOfStrings(step, "member-xref-kinds");
        var memberXrefMaxPerType = GetInt(step, "memberXrefMaxPerType") ?? GetInt(step, "member-xref-max-per-type") ?? 0;
        var powerShellExamplesPath = ResolvePath(baseDir, GetString(step, "psExamplesPath") ?? GetString(step, "ps-examples-path") ?? GetString(step, "powerShellExamplesPath") ?? GetString(step, "powershell-examples-path"));
        var generatePowerShellFallbackExamples = GetBool(step, "generatePowerShellFallbackExamples") ?? GetBool(step, "generate-powershell-fallback-examples") ?? true;
        var powerShellFallbackExampleLimit = GetInt(step, "powerShellFallbackExampleLimit") ?? GetInt(step, "powershell-fallback-example-limit") ?? 2;
        var sourceUrlMappings = GetApiDocsSourceUrlMappings(
            step,
            "sourceUrlMappings",
            "source-url-mappings",
            "sourceMappings",
            "source-mappings");
        var includeUndocumented = GetBool(step, "includeUndocumented") ?? GetBool(step, "include-undocumented") ?? true;
        var nav = ResolvePath(baseDir, GetString(step, "nav") ?? GetString(step, "navJson") ?? GetString(step, "nav-json"));
        var navContextPath = GetString(step, "navContextPath") ?? GetString(step, "nav-context-path") ??
                             GetString(step, "navContextRoute") ?? GetString(step, "nav-context-route");
        var navContextCollection = GetString(step, "navContextCollection") ?? GetString(step, "nav-context-collection");
        var navContextLayout = GetString(step, "navContextLayout") ?? GetString(step, "nav-context-layout");
        var navContextProject = GetString(step, "navContextProject") ?? GetString(step, "nav-context-project");
        var navSurfaceName = GetString(step, "navSurface") ?? GetString(step, "nav-surface") ??
                             GetString(step, "navSurfaceName") ?? GetString(step, "nav-surface-name");
        var includeNamespaces = GetString(step, "includeNamespace") ?? GetString(step, "include-namespace");
        var excludeNamespaces = GetString(step, "excludeNamespace") ?? GetString(step, "exclude-namespace");
        var includeTypes = GetString(step, "includeType") ?? GetString(step, "include-type");
        var excludeTypes = GetString(step, "excludeType") ?? GetString(step, "exclude-type");
        var quickStartTypes = GetString(step, "quickStartTypes") ?? GetString(step, "quickstartTypes") ??
                              GetString(step, "quick-start-types") ?? GetString(step, "quickstart-types");
        var siteName = GetString(step, "siteName") ?? GetString(step, "site-name");
        var brandUrl = GetString(step, "brandUrl") ?? GetString(step, "brand-url");
        var brandIcon = GetString(step, "brandIcon") ?? GetString(step, "brand-icon");
        var suppressWarnings = GetArrayOfStrings(step, "suppressWarnings");
        var isDev = string.Equals(effectiveMode, "dev", StringComparison.OrdinalIgnoreCase) || fast;
        var ciStrictDefaults = ConsoleEnvironment.IsCI && !isDev;
        var failOnWarnings = GetBool(step, "failOnWarnings") ?? ciStrictDefaults;
        var warningPreviewCount = GetInt(step, "warningPreviewCount") ?? GetInt(step, "warning-preview") ?? (isDev ? 2 : 5);
        var coveragePreviewCount = GetInt(step, "coveragePreviewCount") ?? GetInt(step, "coverage-preview") ?? (isDev ? 2 : 5);
        var coverageThresholds = GetApiDocsCoverageThresholds(step);
        var failOnCoverage = GetBool(step, "failOnCoverage") ?? GetBool(step, "fail-on-coverage") ?? (coverageThresholds.Count > 0);

        var apiType = ApiDocsType.CSharp;
        if (!string.IsNullOrWhiteSpace(typeText) &&
            Enum.TryParse<ApiDocsType>(typeText, true, out var parsedType))
            apiType = parsedType;

        if (string.IsNullOrWhiteSpace(outPath))
            throw new InvalidOperationException("apidocs requires out.");
        if (apiType == ApiDocsType.CSharp && string.IsNullOrWhiteSpace(xml))
            throw new InvalidOperationException("apidocs requires xml for CSharp.");
        if (apiType == ApiDocsType.PowerShell && string.IsNullOrWhiteSpace(help))
            throw new InvalidOperationException("apidocs requires help for PowerShell.");

        var preflightWarnings = ValidateApiDocsPreflight(
            apiType,
            sourceRoot,
            sourceUrl,
            sourceUrlMappings,
            nav,
            navSurfaceName,
            navContextPath,
            powerShellExamplesPath);
        var filteredPreflightWarnings = suppressWarnings is { Length: > 0 }
            ? WebVerifyPolicy.FilterWarnings(preflightWarnings, suppressWarnings)
            : preflightWarnings;
        if (filteredPreflightWarnings.Length > 0)
        {
            logger?.Warn($"{label}: apidocs preflight warnings: {filteredPreflightWarnings.Length}");

            var previewLimit = Math.Clamp(warningPreviewCount, 0, 20);
            if (previewLimit > 0)
            {
                foreach (var warning in filteredPreflightWarnings.Where(static w => !string.IsNullOrWhiteSpace(w)).Take(previewLimit))
                {
                    logger?.Warn($"{label}: {warning}");
                }

                var remaining = filteredPreflightWarnings.Length - previewLimit;
                if (remaining > 0)
                    logger?.Warn($"{label}: (+{remaining} more warnings)");
            }

            if (failOnWarnings)
            {
                var headline = filteredPreflightWarnings.FirstOrDefault(static w => !string.IsNullOrWhiteSpace(w))
                               ?? "API docs preflight warnings encountered.";
                throw new InvalidOperationException(headline);
            }
        }

        // Best-practice default: when pipeline runs at a website repo root, assume ./site.json
        // unless the step overrides it explicitly. This prevents "API reference has no navigation"
        // and theme drift when agents forget to wire config/nav.
        var configPath = ResolvePath(baseDir, GetString(step, "config"));
        if (string.IsNullOrWhiteSpace(configPath))
        {
            var defaultSiteConfig = Path.Combine(baseDir, "site.json");
            if (File.Exists(defaultSiteConfig))
                configPath = defaultSiteConfig;
        }

        // If the user configured header/footer paths but they don't exist, treat them as unset so
        // we can fall back to theme fragments (or embedded fragments) deterministically.
        if (!string.IsNullOrWhiteSpace(header) && !File.Exists(Path.GetFullPath(header)))
        {
            logger?.Warn($"{label}: apidocs headerHtml not found: {header}");
            header = null;
        }
        if (!string.IsNullOrWhiteSpace(footer) && !File.Exists(Path.GetFullPath(footer)))
        {
            logger?.Warn($"{label}: apidocs footerHtml not found: {footer}");
            footer = null;
        }

        if (!string.IsNullOrWhiteSpace(configPath))
        {
            if (string.IsNullOrWhiteSpace(nav))
            {
                // Prefer site-nav.json when the repo provides it (static/data). This supports navigation profiles
                // and avoids "API reference has no navigation" drift across sites.
                try
                {
                    var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(configPath, WebCliJson.Options);
                    var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);
                    var dataRoot = string.IsNullOrWhiteSpace(spec.DataRoot) ? "data" : spec.DataRoot;
                    var relativeRoot = Path.IsPathRooted(dataRoot) ? "data" : dataRoot.TrimStart('/', '\\');
                    var staticNavPath = Path.Combine(plan.RootPath, "static", relativeRoot, "site-nav.json");
                    nav = File.Exists(staticNavPath) ? staticNavPath : configPath;
                }
                catch
                {
                    nav = configPath;
                }
            }

            TryResolveApiFragmentsFromTheme(configPath, ref header, ref footer);

            if (injectCriticalCss)
            {
                try
                {
                    var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(configPath, WebCliJson.Options);
                    var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);
                    injectedCriticalCssHtml = RenderCriticalCssHtml(spec.AssetRegistry, plan.RootPath);
                    if (!string.IsNullOrWhiteSpace(injectedCriticalCssHtml))
                        criticalCssPath = null; // avoid accidental double-injection if both are set
                }
                catch
                {
                    // Best-effort: critical CSS injection is optional.
                }
            }
        }

        var options = new WebApiDocsOptions
        {
            Type = apiType,
            XmlPath = xml ?? string.Empty,
            HelpPath = help,
            AssemblyPath = assembly,
            OutputPath = outPath,
            Title = string.IsNullOrWhiteSpace(title) ? "API Reference" : title,
            BaseUrl = baseUrl,
            Format = format,
            CssHref = css,
            CriticalCssHtml = injectedCriticalCssHtml,
            CriticalCssPath = criticalCssPath,
            HeaderHtmlPath = header,
            FooterHtmlPath = footer,
            Template = template,
            TemplateRootPath = templateRoot,
            IndexTemplatePath = indexTemplate,
            TypeTemplatePath = typeTemplate,
            DocsIndexTemplatePath = docsIndexTemplate,
            DocsTypeTemplatePath = docsTypeTemplate,
            DocsScriptPath = docsScript,
            SearchScriptPath = searchScript,
            DocsHomeUrl = docsHome,
            SidebarPosition = sidebar,
            BodyClass = bodyClass,
            SourceRootPath = sourceRoot,
            SourcePathPrefix = sourcePathPrefix,
            SourceUrlPattern = sourceUrl,
            GenerateCoverageReport = generateCoverageReport,
            CoverageReportPath = coverageReportPath,
            GenerateXrefMap = generateXrefMap,
            GenerateMemberXrefs = generateMemberXrefs,
            MemberXrefMaxPerType = memberXrefMaxPerType <= 0 ? 0 : memberXrefMaxPerType,
            XrefMapPath = xrefMapPath,
            GeneratePowerShellFallbackExamples = generatePowerShellFallbackExamples,
            PowerShellExamplesPath = powerShellExamplesPath,
            PowerShellFallbackExampleLimitPerCommand = powerShellFallbackExampleLimit > 0 ? powerShellFallbackExampleLimit : 2,
            IncludeUndocumentedTypes = includeUndocumented,
            NavJsonPath = nav,
            // Default to root context for profile selection to avoid accidental "API header has different nav"
            // when sites define /api profiles that override menus. Sites that want /api-specific menus can set navContextPath explicitly.
            NavContextPath = navContextPath,
            NavContextCollection = navContextCollection,
            NavContextLayout = navContextLayout,
            NavContextProject = navContextProject,
            NavSurfaceName = navSurfaceName,
            SiteName = siteName,
            BrandUrl = brandUrl,
            BrandIcon = brandIcon
        };
        if (sourceUrlMappings.Length > 0)
            options.SourceUrlMappings.AddRange(sourceUrlMappings);

        var includeList = CliPatternHelper.SplitPatterns(includeNamespaces);
        var excludeList = CliPatternHelper.SplitPatterns(excludeNamespaces);
        var includeTypeList = CliPatternHelper.SplitPatterns(includeTypes);
        var excludeTypeList = CliPatternHelper.SplitPatterns(excludeTypes);
        if (includeList.Length > 0)
            options.IncludeNamespacePrefixes.AddRange(includeList);
        if (excludeList.Length > 0)
            options.ExcludeNamespacePrefixes.AddRange(excludeList);
        if (includeTypeList.Length > 0)
            options.IncludeTypeNames.AddRange(includeTypeList);
        if (excludeTypeList.Length > 0)
            options.ExcludeTypeNames.AddRange(excludeTypeList);
        var quickStartTypeList = CliPatternHelper.SplitPatterns(quickStartTypes);
        if (quickStartTypeList.Length > 0)
            options.QuickStartTypeNames.AddRange(quickStartTypeList);
        var memberXrefKindList = new List<string>();
        if (!string.IsNullOrWhiteSpace(memberXrefKindsText))
            memberXrefKindList.AddRange(CliPatternHelper.SplitPatterns(memberXrefKindsText));
        if (memberXrefKindsArray is { Length: > 0 })
        {
            foreach (var entry in memberXrefKindsArray)
            {
                if (string.IsNullOrWhiteSpace(entry))
                    continue;
                memberXrefKindList.AddRange(CliPatternHelper.SplitPatterns(entry));
            }
        }
        if (memberXrefKindList.Count > 0)
            options.MemberXrefKinds.AddRange(memberXrefKindList);

        var res = WebApiDocsGenerator.Generate(options);
        var note = res.UsedReflectionFallback ? " (reflection)" : string.Empty;
        var filteredWarnings = suppressWarnings is { Length: > 0 }
            ? WebVerifyPolicy.FilterWarnings(res.Warnings, suppressWarnings)
            : res.Warnings;

        if (filteredWarnings.Length > 0)
        {
            var firstWarning = filteredWarnings[0];
            if (!string.IsNullOrWhiteSpace(firstWarning))
            {
                var trimmed = firstWarning.Length > 120
                    ? $"{firstWarning.Substring(0, 117)}..."
                    : firstWarning;
                note += $" (warn: {trimmed})";
            }
            else
            {
                note += $" ({filteredWarnings.Length} warnings)";
            }

            logger?.Warn($"{label}: apidocs warnings: {filteredWarnings.Length}");

            var previewLimit = Math.Clamp(warningPreviewCount, 0, 20);
            if (previewLimit > 0)
            {
                foreach (var warning in filteredWarnings.Where(static w => !string.IsNullOrWhiteSpace(w)).Take(previewLimit))
                {
                    logger?.Warn($"{label}: {warning}");
                }

                var remaining = filteredWarnings.Length - previewLimit;
                if (remaining > 0)
                    logger?.Warn($"{label}: (+{remaining} more warnings)");
            }

            if (failOnWarnings)
            {
                var headline = filteredWarnings.FirstOrDefault(static w => !string.IsNullOrWhiteSpace(w)) ?? "API docs warnings encountered.";
                throw new InvalidOperationException(headline);
            }
        }

        if (coverageThresholds.Count > 0)
        {
            var coverageFailures = EvaluateApiDocsCoverageThresholds(res.CoveragePath, coverageThresholds, out var coverageHeadline);
            if (coverageFailures.Count > 0)
            {
                note += $" (coverage: {coverageFailures.Count} issue(s))";
                logger?.Warn($"{label}: apidocs coverage threshold failures: {coverageFailures.Count}");

                var previewLimit = Math.Clamp(coveragePreviewCount, 0, 20);
                if (previewLimit > 0)
                {
                    foreach (var failure in coverageFailures.Take(previewLimit))
                        logger?.Warn($"{label}: {failure}");

                    var remaining = coverageFailures.Count - previewLimit;
                    if (remaining > 0)
                        logger?.Warn($"{label}: (+{remaining} more coverage issues)");
                }

                if (failOnCoverage)
                    throw new InvalidOperationException(coverageHeadline ?? "API docs coverage thresholds failed.");
            }
        }

        stepResult.Success = true;
        stepResult.Message = $"API docs {res.TypeCount} types{note}";
    }

    private static void ExecuteApiDocsBatch(
        JsonElement parentStep,
        JsonElement[] inputs,
        string label,
        string baseDir,
        bool fast,
        string effectiveMode,
        WebConsoleLogger? logger,
        WebPipelineStepResult stepResult)
    {
        if (inputs.Length == 0)
            throw new InvalidOperationException("apidocs batch requires at least one input object.");

        var completed = 0;
        var notes = new List<string>();
        for (var index = 0; index < inputs.Length; index++)
        {
            var input = inputs[index];
            var inputLabel = GetString(input, "id") ??
                             GetString(input, "name") ??
                             GetString(input, "title") ??
                             $"input-{index + 1}";

            using var merged = CreateMergedApiDocsStepDocument(parentStep, input);
            var nestedResult = new WebPipelineStepResult { Task = "apidocs" };
            try
            {
                ExecuteApiDocs(
                    merged.RootElement,
                    $"{label}/{inputLabel}",
                    baseDir,
                    fast,
                    effectiveMode,
                    logger,
                    nestedResult);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"apidocs batch input '{inputLabel}' failed: {ex.Message}", ex);
            }

            completed++;
            if (!string.IsNullOrWhiteSpace(nestedResult.Message))
            {
                var note = nestedResult.Message!;
                if (note.Length > 100)
                    note = $"{note.Substring(0, 97)}...";
                notes.Add($"{inputLabel}: {note}");
            }
        }

        var summary = string.Join("; ", notes.Take(2));
        var suffix = notes.Count > 2 ? $" (+{notes.Count - 2} more)" : string.Empty;
        stepResult.Success = true;
        stepResult.Message = string.IsNullOrWhiteSpace(summary)
            ? $"API docs batch {completed} input(s)"
            : $"API docs batch {completed} input(s): {summary}{suffix}";
    }

    private static JsonDocument CreateMergedApiDocsStepDocument(JsonElement parentStep, JsonElement input)
    {
        var parentNode = JsonNode.Parse(parentStep.GetRawText()) as JsonObject
                         ?? throw new InvalidOperationException("apidocs batch parent step must be a JSON object.");
        parentNode.Remove("inputs");
        parentNode.Remove("entries");

        if (input.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("apidocs batch inputs must be JSON objects.");

        foreach (var property in input.EnumerateObject())
            parentNode[property.Name] = JsonNode.Parse(property.Value.GetRawText());

        return JsonDocument.Parse(parentNode.ToJsonString());
    }

    internal static string[] ValidateApiDocsPreflight(
        ApiDocsType apiType,
        string? sourceRoot,
        string? sourceUrl,
        IReadOnlyList<WebApiDocsSourceUrlMapping> sourceUrlMappings,
        string? navPath,
        string? navSurfaceName,
        string? navContextPath,
        string? powerShellExamplesPath)
    {
        var warnings = new List<string>();

        var hasMappings = sourceUrlMappings is { Count: > 0 };
        var hasSourceRoot = !string.IsNullOrWhiteSpace(sourceRoot);
        var hasSourceUrl = !string.IsNullOrWhiteSpace(sourceUrl);
        if (hasMappings && !hasSourceRoot && !hasSourceUrl)
        {
            warnings.Add("[PFWEB.APIDOCS.SOURCE] API docs source preflight: sourceUrlMappings are configured but both sourceRoot and sourceUrl are empty; source links will be disabled.");
        }

        if (hasSourceRoot)
        {
            var fullSourceRoot = Path.GetFullPath(sourceRoot!);
            if (!Directory.Exists(fullSourceRoot))
            {
                warnings.Add($"[PFWEB.APIDOCS.SOURCE] API docs source preflight: sourceRoot path does not exist: {fullSourceRoot}");
            }
        }

        if (hasSourceUrl && !ContainsAnyPathToken(sourceUrl!))
        {
            warnings.Add("[PFWEB.APIDOCS.SOURCE] API docs source preflight: sourceUrl does not include a path token ({path}, {pathNoRoot}, or {pathNoPrefix}).");
        }

        if (hasMappings)
        {
            var seenPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mapping in sourceUrlMappings)
            {
                if (mapping is null)
                    continue;
                var prefix = (mapping.PathPrefix ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(prefix))
                    continue;

                if (!seenPrefixes.Add(prefix))
                {
                    warnings.Add($"[PFWEB.APIDOCS.SOURCE] API docs source preflight: duplicate sourceUrlMappings pathPrefix '{prefix}'.");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(navSurfaceName) && string.IsNullOrWhiteSpace(navPath))
        {
            warnings.Add("[PFWEB.APIDOCS.NAV] API docs nav preflight: navSurface is set but nav/navJson is empty; navSurface cannot be resolved.");
        }

        if (!string.IsNullOrWhiteSpace(navPath))
        {
            var fullNavPath = Path.GetFullPath(navPath);
            if (!File.Exists(fullNavPath))
            {
                warnings.Add($"[PFWEB.APIDOCS.NAV] API docs nav preflight: nav/navJson file was not found: {fullNavPath}");
            }
        }

        if (!string.IsNullOrWhiteSpace(navContextPath))
        {
            var trimmed = navContextPath.Trim();
            if (!trimmed.StartsWith("/", StringComparison.Ordinal) &&
                !trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"[PFWEB.APIDOCS.NAV] API docs nav preflight: navContextPath '{navContextPath}' should be root-relative (for example '/api/').");
            }
        }

        if (apiType == ApiDocsType.PowerShell && !string.IsNullOrWhiteSpace(powerShellExamplesPath))
        {
            var fullExamplesPath = Path.GetFullPath(powerShellExamplesPath);
            if (!Directory.Exists(fullExamplesPath) && !File.Exists(fullExamplesPath))
            {
                warnings.Add($"[PFWEB.APIDOCS.POWERSHELL] API docs PowerShell preflight: psExamplesPath was not found: {fullExamplesPath}");
            }
        }

        return warnings.ToArray();
    }

    private static bool ContainsAnyPathToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.IndexOf("{path}", StringComparison.OrdinalIgnoreCase) >= 0 ||
               value.IndexOf("{pathNoRoot}", StringComparison.OrdinalIgnoreCase) >= 0 ||
               value.IndexOf("{pathNoPrefix}", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static List<ApiDocsCoverageThreshold> GetApiDocsCoverageThresholds(JsonElement step)
    {
        var thresholds = new List<ApiDocsCoverageThreshold>();
        AddCoverageMinPercentThreshold(thresholds, step, "minTypeSummaryPercent", "min-type-summary-percent", "types.summary.percent", "Type summary coverage");
        AddCoverageMinPercentThreshold(thresholds, step, "minTypeRemarksPercent", "min-type-remarks-percent", "types.remarks.percent", "Type remarks coverage");
        AddCoverageMinPercentThreshold(thresholds, step, "minTypeCodeExamplesPercent", "min-type-code-examples-percent", "types.codeExamples.percent", "Type code examples coverage");
        AddCoverageMinPercentThreshold(thresholds, step, "minMemberSummaryPercent", "min-member-summary-percent", "members.summary.percent", "Member summary coverage");
        AddCoverageMinPercentThreshold(thresholds, step, "minMemberCodeExamplesPercent", "min-member-code-examples-percent", "members.codeExamples.percent", "Member code examples coverage");
        AddCoverageMinPercentThreshold(thresholds, step, "minPowerShellSummaryPercent", "min-powershell-summary-percent", "powershell.summary.percent", "PowerShell command summary coverage", powerShellCommandMetric: true);
        AddCoverageMinPercentThreshold(thresholds, step, "minPowerShellRemarksPercent", "min-powershell-remarks-percent", "powershell.remarks.percent", "PowerShell command remarks coverage", powerShellCommandMetric: true);
        AddCoverageMinPercentThreshold(thresholds, step, "minPowerShellCodeExamplesPercent", "min-powershell-code-examples-percent", "powershell.codeExamples.percent", "PowerShell command code examples coverage", powerShellCommandMetric: true);
        AddCoverageMinPercentThreshold(thresholds, step, "minPowerShellParameterSummaryPercent", "min-powershell-parameter-summary-percent", "powershell.parameters.percent", "PowerShell parameter summary coverage", powerShellCommandMetric: true);
        AddCoverageMinPercentThreshold(thresholds, step, "minTypeSourcePathPercent", "min-type-source-path-percent", "source.types.path.percent", "Type source path coverage");
        AddCoverageMinPercentThreshold(thresholds, step, "minTypeSourceUrlPercent", "min-type-source-url-percent", "source.types.url.percent", "Type source URL coverage");
        AddCoverageMinPercentThreshold(thresholds, step, "minMemberSourcePathPercent", "min-member-source-path-percent", "source.members.path.percent", "Member source path coverage");
        AddCoverageMinPercentThreshold(thresholds, step, "minMemberSourceUrlPercent", "min-member-source-url-percent", "source.members.url.percent", "Member source URL coverage");
        AddCoverageMinPercentThreshold(thresholds, step, "minPowerShellSourcePathPercent", "min-powershell-source-path-percent", "source.powershell.path.percent", "PowerShell command source path coverage", powerShellCommandMetric: true);
        AddCoverageMinPercentThreshold(thresholds, step, "minPowerShellSourceUrlPercent", "min-powershell-source-url-percent", "source.powershell.url.percent", "PowerShell command source URL coverage", powerShellCommandMetric: true);
        AddCoverageMaxThreshold(thresholds, step, "maxTypeSourceInvalidUrlCount", "max-type-source-invalid-url-count", "source.types.invalidUrl.count", "Type source invalid URL count");
        AddCoverageMaxThreshold(thresholds, step, "maxMemberSourceInvalidUrlCount", "max-member-source-invalid-url-count", "source.members.invalidUrl.count", "Member source invalid URL count");
        AddCoverageMaxThreshold(thresholds, step, "maxPowerShellSourceInvalidUrlCount", "max-powershell-source-invalid-url-count", "source.powershell.invalidUrl.count", "PowerShell command source invalid URL count", powerShellCommandMetric: true);
        AddCoverageMaxThreshold(thresholds, step, "maxTypeSourceUnresolvedTemplateCount", "max-type-source-unresolved-template-count", "source.types.unresolvedTemplateToken.count", "Type source unresolved template token count");
        AddCoverageMaxThreshold(thresholds, step, "maxMemberSourceUnresolvedTemplateCount", "max-member-source-unresolved-template-count", "source.members.unresolvedTemplateToken.count", "Member source unresolved template token count");
        AddCoverageMaxThreshold(thresholds, step, "maxPowerShellSourceUnresolvedTemplateCount", "max-powershell-source-unresolved-template-count", "source.powershell.unresolvedTemplateToken.count", "PowerShell command source unresolved template token count", powerShellCommandMetric: true);
        AddCoverageMaxThreshold(thresholds, step, "maxTypeSourceRepoMismatchHints", "max-type-source-repo-mismatch-hints", "source.types.repoMismatchHints.count", "Type source repo-mismatch hints");
        AddCoverageMaxThreshold(thresholds, step, "maxMemberSourceRepoMismatchHints", "max-member-source-repo-mismatch-hints", "source.members.repoMismatchHints.count", "Member source repo-mismatch hints");
        AddCoverageMaxThreshold(thresholds, step, "maxPowerShellSourceRepoMismatchHints", "max-powershell-source-repo-mismatch-hints", "source.powershell.repoMismatchHints.count", "PowerShell command source repo-mismatch hints", powerShellCommandMetric: true);
        return thresholds;
    }

    private static void AddCoverageMinPercentThreshold(
        List<ApiDocsCoverageThreshold> thresholds,
        JsonElement step,
        string primaryName,
        string aliasName,
        string metricPath,
        string label,
        bool powerShellCommandMetric = false)
    {
        var value = GetDouble(step, primaryName) ?? GetDouble(step, aliasName);
        if (!value.HasValue)
            return;

        if (value.Value is < 0 or > 100)
            throw new InvalidOperationException($"apidocs coverage threshold '{primaryName}' must be between 0 and 100.");

        thresholds.Add(new ApiDocsCoverageThreshold
        {
            Label = label,
            MetricPath = metricPath,
            TargetValue = value.Value,
            Comparison = ApiDocsCoverageComparison.Minimum,
            FormatAsPercent = true,
            SkipWhenNoPowerShellCommands = powerShellCommandMetric
        });
    }

    private static void AddCoverageMaxThreshold(
        List<ApiDocsCoverageThreshold> thresholds,
        JsonElement step,
        string primaryName,
        string aliasName,
        string metricPath,
        string label,
        bool powerShellCommandMetric = false)
    {
        var value = GetDouble(step, primaryName) ?? GetDouble(step, aliasName);
        if (!value.HasValue)
            return;

        if (value.Value < 0)
            throw new InvalidOperationException($"apidocs coverage threshold '{primaryName}' must be greater than or equal to 0.");

        thresholds.Add(new ApiDocsCoverageThreshold
        {
            Label = label,
            MetricPath = metricPath,
            TargetValue = value.Value,
            Comparison = ApiDocsCoverageComparison.Maximum,
            FormatAsPercent = false,
            SkipWhenNoPowerShellCommands = powerShellCommandMetric
        });
    }

    private static List<string> EvaluateApiDocsCoverageThresholds(
        string? coveragePath,
        IReadOnlyList<ApiDocsCoverageThreshold> thresholds,
        out string? headline)
    {
        headline = null;
        var failures = new List<string>();
        if (thresholds.Count == 0)
            return failures;

        if (string.IsNullOrWhiteSpace(coveragePath) || !File.Exists(coveragePath))
        {
            var message = "API docs coverage report not found; cannot evaluate coverage thresholds. Enable GenerateCoverageReport or set coverageReport path.";
            failures.Add(message);
            headline = message;
            return failures;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(coveragePath));
        var root = doc.RootElement;

        var commandCount = 0;
        if (TryGetJsonDoubleByPath(root, "powershell.commandCount", out var commandCountRaw))
            commandCount = (int)Math.Round(commandCountRaw);

        foreach (var threshold in thresholds)
        {
            if (threshold.SkipWhenNoPowerShellCommands && commandCount <= 0)
                continue;

            if (!TryGetJsonDoubleByPath(root, threshold.MetricPath, out var actual))
            {
                failures.Add($"{threshold.Label}: metric '{threshold.MetricPath}' is missing from coverage report.");
                continue;
            }

            if (threshold.Comparison == ApiDocsCoverageComparison.Minimum && actual + 0.0001 < threshold.TargetValue)
            {
                failures.Add($"{threshold.Label}: {FormatCoverageValue(actual, threshold.FormatAsPercent)} is below required {FormatCoverageValue(threshold.TargetValue, threshold.FormatAsPercent)}.");
            }
            else if (threshold.Comparison == ApiDocsCoverageComparison.Maximum && actual - threshold.TargetValue > 0.0001)
            {
                failures.Add($"{threshold.Label}: {FormatCoverageValue(actual, threshold.FormatAsPercent)} exceeds allowed {FormatCoverageValue(threshold.TargetValue, threshold.FormatAsPercent)}.");
            }
        }

        headline = failures.FirstOrDefault();
        return failures;
    }

    private static string FormatCoverageValue(double value, bool asPercent)
    {
        return asPercent ? $"{value:0.##}%" : $"{value:0.##}";
    }

    private static bool TryGetJsonDoubleByPath(JsonElement root, string path, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var current = root;
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
                return false;
        }

        if (current.ValueKind == JsonValueKind.Number && current.TryGetDouble(out value))
            return true;
        if (current.ValueKind == JsonValueKind.String &&
            double.TryParse(current.GetString(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value))
            return true;
        return false;
    }

    private sealed class ApiDocsCoverageThreshold
    {
        public string Label { get; init; } = string.Empty;
        public string MetricPath { get; init; } = string.Empty;
        public double TargetValue { get; init; }
        public ApiDocsCoverageComparison Comparison { get; init; } = ApiDocsCoverageComparison.Minimum;
        public bool FormatAsPercent { get; init; } = true;
        public bool SkipWhenNoPowerShellCommands { get; init; }
    }

    private enum ApiDocsCoverageComparison
    {
        Minimum,
        Maximum
    }

    private static string RenderCriticalCssHtml(AssetRegistrySpec? assets, string rootPath)
    {
        if (assets?.CriticalCss is null || assets.CriticalCss.Length == 0)
            return string.Empty;
        if (string.IsNullOrWhiteSpace(rootPath))
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        foreach (var css in assets.CriticalCss)
        {
            if (css is null || string.IsNullOrWhiteSpace(css.Path))
                continue;

            var fullPath = Path.IsPathRooted(css.Path)
                ? css.Path
                : Path.Combine(rootPath, css.Path);
            if (!File.Exists(fullPath))
                continue;

            sb.Append("<style>");
            sb.Append(File.ReadAllText(fullPath));
            sb.AppendLine("</style>");
        }
        return sb.ToString();
    }

    private static void ExecuteChangelog(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var outPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output"));
        var sourceText = GetString(step, "source");
        var changelog = ResolvePath(baseDir, GetString(step, "changelog") ?? GetString(step, "changelogPath") ?? GetString(step, "changelog-path"));
        var repo = GetString(step, "repo");
        var repoUrl = GetString(step, "repoUrl") ?? GetString(step, "repo-url");
        var token = GetString(step, "token");
        var maxValue = GetInt(step, "max") ?? 0;
        var title = GetString(step, "title");
        if (string.IsNullOrWhiteSpace(outPath))
            throw new InvalidOperationException("changelog requires out.");

        var source = WebChangelogSource.Auto;
        if (!string.IsNullOrWhiteSpace(sourceText) &&
            Enum.TryParse<WebChangelogSource>(sourceText, true, out var parsedSource))
            source = parsedSource;

        var options = new WebChangelogOptions
        {
            Source = source,
            ChangelogPath = changelog,
            OutputPath = outPath,
            Repo = repo,
            RepoUrl = repoUrl,
            Token = token,
            Title = title,
            MaxReleases = maxValue <= 0 ? null : maxValue
        };

        var res = WebChangelogGenerator.Generate(options);
        var note = res.Source != WebChangelogSource.Auto ? $" ({res.Source.ToString().ToLowerInvariant()})" : string.Empty;
        if (res.Warnings.Length > 0)
        {
            var firstWarning = res.Warnings[0];
            if (!string.IsNullOrWhiteSpace(firstWarning))
            {
                var trimmed = firstWarning.Length > 120
                    ? $"{firstWarning.Substring(0, 117)}..."
                    : firstWarning;
                note += $" (warn: {trimmed})";
            }
        }

        stepResult.Success = true;
        stepResult.Message = $"Changelog {res.ReleaseCount} releases{note}";
    }

    private static void ExecuteVersionHub(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var outPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output"));
        if (string.IsNullOrWhiteSpace(outPath))
            throw new InvalidOperationException("version-hub requires out.");

        var discoverRoot = ResolvePath(baseDir, GetString(step, "discoverRoot") ?? GetString(step, "discover-root"));
        var discoverPattern = GetString(step, "discoverPattern") ?? GetString(step, "discover-pattern") ?? "v*";
        var basePath = GetString(step, "basePath") ?? GetString(step, "base-path") ?? "/docs/";
        var setLatestFromNewest = GetBool(step, "setLatestFromNewest") ?? GetBool(step, "set-latest-from-newest") ?? true;
        var title = GetString(step, "title");

        var entries = ParseVersionHubEntries(step);
        var result = WebVersionHubGenerator.Generate(new WebVersionHubOptions
        {
            OutputPath = outPath,
            BaseDirectory = baseDir,
            Title = title,
            Entries = entries,
            DiscoverRoot = discoverRoot,
            DiscoverPattern = discoverPattern,
            BasePath = basePath,
            SetLatestFromNewest = setLatestFromNewest
        });

        var warningNote = result.Warnings.Length > 0 ? $" ({result.Warnings.Length} warnings)" : string.Empty;
        stepResult.Success = true;
        stepResult.Message = string.IsNullOrWhiteSpace(result.LatestVersion)
            ? $"Version hub {result.VersionCount} versions{warningNote}"
            : $"Version hub {result.VersionCount} versions (latest {result.LatestVersion}){warningNote}";
    }

    private static List<WebVersionHubEntryInput> ParseVersionHubEntries(JsonElement step)
    {
        var parsed = new List<WebVersionHubEntryInput>();
        var arrays = new[]
        {
            GetArrayOfObjects(step, "versions"),
            GetArrayOfObjects(step, "entries")
        };

        foreach (var array in arrays)
        {
            if (array is null || array.Length == 0)
                continue;

            foreach (var item in array)
            {
                var aliases = GetArrayOfStrings(item, "aliases") ?? Array.Empty<string>();
                parsed.Add(new WebVersionHubEntryInput
                {
                    Id = GetString(item, "id"),
                    Version = GetString(item, "version"),
                    Label = GetString(item, "label"),
                    Path = GetString(item, "path") ?? GetString(item, "url"),
                    Channel = GetString(item, "channel"),
                    Support = GetString(item, "support"),
                    Latest = GetBool(item, "latest") ?? false,
                    Lts = GetBool(item, "lts") ?? false,
                    Deprecated = GetBool(item, "deprecated") ?? false,
                    Aliases = aliases
                        .Where(static value => !string.IsNullOrWhiteSpace(value))
                        .Select(static value => value.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                });
            }
        }

        return parsed;
    }

    private static void ExecutePackageHub(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var outPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output"));
        if (string.IsNullOrWhiteSpace(outPath))
            throw new InvalidOperationException("package-hub requires out.");

        var title = GetString(step, "title");
        var projects = new List<string>();
        AddInputPaths(projects, GetString(step, "project"));
        AddInputPaths(projects, GetString(step, "projects"));
        AddInputPaths(projects, GetArrayOfStrings(step, "projectFiles"));
        AddInputPaths(projects, GetArrayOfStrings(step, "project-files"));

        var modules = new List<string>();
        AddInputPaths(modules, GetString(step, "module"));
        AddInputPaths(modules, GetString(step, "modules"));
        AddInputPaths(modules, GetArrayOfStrings(step, "moduleFiles"));
        AddInputPaths(modules, GetArrayOfStrings(step, "module-files"));

        var options = new WebPackageHubOptions
        {
            OutputPath = outPath,
            BaseDirectory = baseDir,
            Title = title
        };
        options.ProjectPaths.AddRange(projects
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase));
        options.ModulePaths.AddRange(modules
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase));

        var result = WebPackageHubGenerator.Generate(options);
        var warningNote = result.Warnings.Length > 0
            ? $" ({result.Warnings.Length} warnings)"
            : string.Empty;
        stepResult.Success = true;
        stepResult.Message = $"Package hub {result.LibraryCount} libraries, {result.ModuleCount} modules{warningNote}";
    }

    private static void ExecuteLlms(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
        if (string.IsNullOrWhiteSpace(siteRoot))
            throw new InvalidOperationException("llms requires siteRoot.");

        var apiLevelText = GetString(step, "apiLevel") ?? GetString(step, "api-level");
        var res = WebLlmsGenerator.Generate(new WebLlmsOptions
        {
            SiteRoot = siteRoot,
            ProjectFile = ResolvePath(baseDir, GetString(step, "project")),
            ApiIndexPath = ResolvePath(baseDir, GetString(step, "apiIndex") ?? GetString(step, "api-index")),
            ApiBase = GetString(step, "apiBase") ?? "/api",
            Name = GetString(step, "name"),
            PackageId = GetString(step, "package") ?? GetString(step, "packageId"),
            Version = GetString(step, "version"),
            QuickstartPath = ResolvePath(baseDir, GetString(step, "quickstart")),
            Overview = GetString(step, "overview"),
            License = GetString(step, "license"),
            Targets = GetString(step, "targets"),
            ExtraContentPath = ResolvePath(baseDir, GetString(step, "extra")),
            ApiDetailLevel = ParseApiDetailLevel(apiLevelText),
            ApiMaxTypes = GetInt(step, "apiMaxTypes") ?? 200,
            ApiMaxMembers = GetInt(step, "apiMaxMembers") ?? 2000
        });
        stepResult.Success = true;
        stepResult.Message = $"LLMS generated ({res.Version})";
    }

    private static void ExecuteCompatibilityMatrix(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var outputPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output"));
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new InvalidOperationException("compat-matrix requires out.");

        var markdownOutputPath = ResolvePath(baseDir,
            GetString(step, "markdownOut") ??
            GetString(step, "markdown-out") ??
            GetString(step, "markdownOutput") ??
            GetString(step, "markdown-output"));

        var includeDependencies = GetBool(step, "includeDependencies") ??
                                  GetBool(step, "include-dependencies") ??
                                  true;
        var failOnWarnings = GetBool(step, "failOnWarnings") ?? false;

        var csprojInputs = new List<string>();
        AddInputPaths(csprojInputs, GetString(step, "csproj"));
        AddInputPaths(csprojInputs, GetString(step, "project"));
        AddInputPaths(csprojInputs, GetString(step, "csprojFiles"));
        AddInputPaths(csprojInputs, GetString(step, "csproj-files"));
        AddInputPaths(csprojInputs, GetArrayOfStrings(step, "csprojFiles"));
        AddInputPaths(csprojInputs, GetArrayOfStrings(step, "csproj-files"));
        AddInputPaths(csprojInputs, GetArrayOfStrings(step, "projects"));

        var psd1Inputs = new List<string>();
        AddInputPaths(psd1Inputs, GetString(step, "psd1"));
        AddInputPaths(psd1Inputs, GetString(step, "psd1Files"));
        AddInputPaths(psd1Inputs, GetString(step, "psd1-files"));
        AddInputPaths(psd1Inputs, GetArrayOfStrings(step, "psd1Files"));
        AddInputPaths(psd1Inputs, GetArrayOfStrings(step, "psd1-files"));

        var options = new WebCompatibilityMatrixOptions
        {
            OutputPath = outputPath,
            MarkdownOutputPath = markdownOutputPath,
            BaseDirectory = baseDir,
            Title = GetString(step, "title"),
            IncludeDependencies = includeDependencies
        };
        options.CsprojFiles.AddRange(csprojInputs
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(value => ResolvePath(baseDir, value) ?? value)
            .Distinct(StringComparer.OrdinalIgnoreCase));
        options.Psd1Files.AddRange(psd1Inputs
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(value => ResolvePath(baseDir, value) ?? value)
            .Distinct(StringComparer.OrdinalIgnoreCase));

        if (step.TryGetProperty("entries", out var entriesElement) && entriesElement.ValueKind == JsonValueKind.Array)
            options.Entries.AddRange(ParseCompatibilityEntries(entriesElement));

        var result = WebCompatibilityMatrixGenerator.Generate(options);
        if (failOnWarnings && result.Warnings.Length > 0)
        {
            throw new InvalidOperationException(result.Warnings.FirstOrDefault(static warning => !string.IsNullOrWhiteSpace(warning))
                                                ?? "compat-matrix generated warnings.");
        }

        var note = result.Warnings.Length > 0 ? $" ({result.Warnings.Length} warnings)" : string.Empty;
        stepResult.Success = true;
        stepResult.Message = $"Compatibility matrix {result.EntryCount} entries{note}";
    }

    private static IEnumerable<WebCompatibilityMatrixEntryInput> ParseCompatibilityEntries(JsonElement entriesElement)
    {
        foreach (var item in entriesElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            yield return new WebCompatibilityMatrixEntryInput
            {
                Type = GetString(item, "type"),
                Id = GetString(item, "id"),
                Name = GetString(item, "name"),
                Version = GetString(item, "version"),
                SourcePath = GetString(item, "sourcePath") ?? GetString(item, "source-path") ?? GetString(item, "source"),
                TargetFrameworks = (GetArrayOfStrings(item, "targetFrameworks")
                                    ?? GetArrayOfStrings(item, "target-frameworks")
                                    ?? GetArrayOfStrings(item, "tfms")
                                    ?? Array.Empty<string>()).ToList(),
                PowerShellEditions = (GetArrayOfStrings(item, "powerShellEditions")
                                      ?? GetArrayOfStrings(item, "powershellEditions")
                                      ?? GetArrayOfStrings(item, "psEditions")
                                      ?? GetArrayOfStrings(item, "ps-editions")
                                      ?? Array.Empty<string>()).ToList(),
                PowerShellVersion = GetString(item, "powerShellVersion")
                                    ?? GetString(item, "powershellVersion")
                                    ?? GetString(item, "psVersion")
                                    ?? GetString(item, "ps-version"),
                Dependencies = (GetArrayOfStrings(item, "dependencies")
                                ?? GetArrayOfStrings(item, "dependsOn")
                                ?? Array.Empty<string>()).ToList(),
                Status = GetString(item, "status"),
                Notes = GetString(item, "notes"),
                Url = GetString(item, "url")
            };
        }
    }

    private static void ExecuteSitemap(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
        var baseUrl = GetString(step, "baseUrl") ?? GetString(step, "base-url");
        if (string.IsNullOrWhiteSpace(siteRoot) || string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("sitemap requires siteRoot and baseUrl.");

        var entries = GetSitemapEntries(step, "entries");
        var includeHtml = GetBool(step, "includeHtmlFiles");
        var includeText = GetBool(step, "includeTextFiles");
        var htmlEnabled = GetBool(step, "html") ?? false;
        var htmlOutput = ResolvePath(baseDir, GetString(step, "htmlOutput") ?? GetString(step, "htmlOut") ?? GetString(step, "html-out"));
        var htmlTemplate = ResolvePath(baseDir, GetString(step, "htmlTemplate") ?? GetString(step, "html-template"));
        var htmlCss = GetString(step, "htmlCss") ?? GetString(step, "html-css");
        var htmlTitle = GetString(step, "htmlTitle") ?? GetString(step, "html-title");
        if (!htmlEnabled)
        {
            htmlEnabled = !string.IsNullOrWhiteSpace(htmlOutput) ||
                          !string.IsNullOrWhiteSpace(htmlTemplate) ||
                          !string.IsNullOrWhiteSpace(htmlCss) ||
                          !string.IsNullOrWhiteSpace(htmlTitle);
        }

        var res = WebSitemapGenerator.Generate(new WebSitemapOptions
        {
            SiteRoot = siteRoot,
            BaseUrl = baseUrl,
            OutputPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output")),
            ApiSitemapPath = ResolvePath(baseDir, GetString(step, "apiSitemap") ?? GetString(step, "api-sitemap")),
            ExtraPaths = GetArrayOfStrings(step, "extraPaths") ?? GetArrayOfStrings(step, "extra-paths"),
            Entries = entries.Length == 0 ? null : entries,
            IncludeHtmlFiles = includeHtml ?? true,
            IncludeTextFiles = includeText ?? true,
            GenerateHtml = htmlEnabled,
            HtmlOutputPath = htmlOutput,
            HtmlTemplatePath = htmlTemplate,
            HtmlCssHref = htmlCss,
            HtmlTitle = htmlTitle
        });

        stepResult.Success = true;
        stepResult.Message = $"Sitemap {res.UrlCount} urls";
    }

    private static void ExecuteXrefMerge(
        JsonElement step,
        string label,
        string baseDir,
        bool fast,
        string effectiveMode,
        WebConsoleLogger? logger,
        WebPipelineStepResult stepResult)
    {
        var outPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output"));
        if (string.IsNullOrWhiteSpace(outPath))
            throw new InvalidOperationException("xref-merge requires out.");

        var inputs = new List<string>();
        AddInputPaths(inputs, GetString(step, "map"));
        AddInputPaths(inputs, GetString(step, "maps"));
        AddInputPaths(inputs, GetString(step, "input"));
        AddInputPaths(inputs, GetString(step, "inputs"));
        AddInputPaths(inputs, GetString(step, "source"));
        AddInputPaths(inputs, GetString(step, "sources"));
        AddInputPaths(inputs, GetArrayOfStrings(step, "mapFiles"));
        AddInputPaths(inputs, GetArrayOfStrings(step, "map-files"));
        AddInputPaths(inputs, GetArrayOfStrings(step, "inputsArray"));
        AddInputPaths(inputs, GetArrayOfStrings(step, "inputs-array"));

        var resolvedInputs = inputs
            .Where(static input => !string.IsNullOrWhiteSpace(input))
            .Select(input => ResolvePath(baseDir, input) ?? input)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (resolvedInputs.Count == 0)
            throw new InvalidOperationException("xref-merge requires at least one input map path (map/maps/input/inputs/source/sources).");

        var topOnly = GetBool(step, "topOnly") ?? GetBool(step, "top-only") ?? false;
        var recursive = GetBool(step, "recursive") ?? GetBool(step, "includeSubdirectories") ?? !topOnly;
        var pattern = GetString(step, "pattern") ?? "*.json";
        var preferLast = GetBool(step, "preferLast") ?? GetBool(step, "prefer-last") ?? false;
        var failOnDuplicates = GetBool(step, "failOnDuplicates") ?? GetBool(step, "fail-on-duplicates") ?? false;
        var maxReferences = GetInt(step, "maxReferences") ?? GetInt(step, "max-references") ?? 0;
        var maxDuplicates = GetInt(step, "maxDuplicates") ?? GetInt(step, "max-duplicates") ?? 0;
        var maxReferenceGrowthCount = GetInt(step, "maxReferenceGrowthCount") ?? GetInt(step, "max-reference-growth-count") ?? 0;
        var maxReferenceGrowthPercent = GetDouble(step, "maxReferenceGrowthPercent") ?? GetDouble(step, "max-reference-growth-percent") ?? 0;
        var isDev = string.Equals(effectiveMode, "dev", StringComparison.OrdinalIgnoreCase) || fast;
        var ciStrictDefaults = ConsoleEnvironment.IsCI && !isDev;
        var failOnWarnings = GetBool(step, "failOnWarnings") ?? ciStrictDefaults;
        var warningPreviewCount = GetInt(step, "warningPreviewCount") ?? GetInt(step, "warning-preview") ?? (isDev ? 2 : 5);

        var options = new WebXrefMergeOptions
        {
            OutputPath = outPath,
            Pattern = string.IsNullOrWhiteSpace(pattern) ? "*.json" : pattern,
            Recursive = recursive,
            PreferLast = preferLast,
            FailOnDuplicateIds = failOnDuplicates,
            MaxReferences = maxReferences <= 0 ? 0 : maxReferences,
            MaxDuplicates = maxDuplicates <= 0 ? 0 : maxDuplicates,
            MaxReferenceGrowthCount = maxReferenceGrowthCount <= 0 ? 0 : maxReferenceGrowthCount,
            MaxReferenceGrowthPercent = maxReferenceGrowthPercent <= 0 ? 0 : maxReferenceGrowthPercent
        };
        options.Inputs.AddRange(resolvedInputs);

        var result = WebXrefMapMerger.Merge(options);
        var warnings = result.Warnings ?? Array.Empty<string>();
        if (warnings.Length > 0)
        {
            logger?.Warn($"{label}: xref-merge warnings: {warnings.Length}");

            var previewLimit = Math.Clamp(warningPreviewCount, 0, 20);
            if (previewLimit > 0)
            {
                foreach (var warning in warnings.Where(static warning => !string.IsNullOrWhiteSpace(warning)).Take(previewLimit))
                    logger?.Warn($"{label}: {warning}");

                var remaining = warnings.Length - previewLimit;
                if (remaining > 0)
                    logger?.Warn($"{label}: (+{remaining} more warnings)");
            }

            if (failOnWarnings)
            {
                var headline = warnings.FirstOrDefault(static warning => !string.IsNullOrWhiteSpace(warning))
                               ?? "xref-merge warnings encountered.";
                throw new InvalidOperationException(headline);
            }
        }

        var noteParts = new List<string>();
        if (result.DuplicateCount > 0)
            noteParts.Add($"duplicates: {result.DuplicateCount}");
        if (result.ReferenceDeltaCount.HasValue)
        {
            var deltaText = result.ReferenceDeltaCount.Value >= 0 ? $"+{result.ReferenceDeltaCount.Value}" : result.ReferenceDeltaCount.Value.ToString();
            if (result.ReferenceDeltaPercent.HasValue)
                noteParts.Add($"delta: {deltaText} ({result.ReferenceDeltaPercent.Value:0.##}%)");
            else
                noteParts.Add($"delta: {deltaText}");
        }
        var note = noteParts.Count > 0 ? $" ({string.Join(", ", noteParts)})" : string.Empty;
        stepResult.Success = true;
        stepResult.Message = $"Xref merge {result.ReferenceCount} refs from {result.SourceCount} sources{note}";
    }

    private static void AddInputPaths(List<string> target, string? value)
    {
        if (target is null || string.IsNullOrWhiteSpace(value))
            return;
        foreach (var item in CliPatternHelper.SplitPatterns(value))
        {
            if (!string.IsNullOrWhiteSpace(item))
                target.Add(item);
        }
    }

    private static void AddInputPaths(List<string> target, string[]? values)
    {
        if (target is null || values is null || values.Length == 0)
            return;
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            AddInputPaths(target, value);
        }
    }
}
