using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
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
}
