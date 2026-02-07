using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static class WebPipelineRunner
{
    private const long MaxStateFileSizeBytes = 10 * 1024 * 1024;
    private const int MaxStampFileCount = 1000;
    private static readonly StringComparison FileSystemPathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private static readonly HashSet<string> FingerprintPathKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "config", "siteRoot", "site-root", "project", "solution", "path",
        "out", "output", "source", "destination", "dest",
        "xml", "help", "helpPath", "assembly",
        "changelog", "changelogPath",
        "apiIndex", "apiSitemap", "criticalCss", "hashManifest", "reportPath", "report-path",
        "summaryPath", "sarifPath", "baselinePath", "navCanonicalPath", "navProfiles",
        "summary-path", "sarif-path", "baseline-path", "nav-canonical-path", "nav-profiles",
        "templateRoot", "templateIndex", "templateType",
        "templateDocsIndex", "templateDocsType",
        "docsScript", "searchScript",
        "headerHtml", "footerHtml", "quickstart", "extra",
        "htmlOutput", "htmlTemplate", "cachePath", "profilePath"
    };

    private sealed class WebPipelineCacheState
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, WebPipelineCacheEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class WebPipelineCacheEntry
    {
        public string Fingerprint { get; set; } = string.Empty;
        public string? Message { get; set; }
    }

    private sealed class PipelineStepDefinition
    {
        public int Index { get; set; }
        public string Task { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public string[] DependsOn { get; set; } = Array.Empty<string>();
        public int[] DependencyIndexes { get; set; } = Array.Empty<int>();
        public JsonElement Element { get; set; }
    }

    internal static WebPipelineResult RunPipeline(string pipelinePath, WebConsoleLogger? logger, bool forceProfile = false)
    {
        var json = File.ReadAllText(pipelinePath);
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var root = doc.RootElement;
        if (!root.TryGetProperty("steps", out var stepsElement) || stepsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Pipeline config must include a steps array.");

        var baseDir = Path.GetDirectoryName(pipelinePath) ?? ".";
        var profileEnabled = (GetBool(root, "profile") ?? false) || forceProfile;
        var profileWriteOnFail = GetBool(root, "profileOnFail") ?? GetBool(root, "profile-on-fail") ?? true;
        var profilePath = ResolvePathWithinRoot(baseDir, GetString(root, "profilePath") ?? GetString(root, "profile-path"), Path.Combine(".powerforge", "pipeline-profile.json"));
        var cacheEnabled = GetBool(root, "cache") ?? false;
        var cachePath = ResolvePathWithinRoot(baseDir, GetString(root, "cachePath") ?? GetString(root, "cache-path"), Path.Combine(".powerforge", "pipeline-cache.json"));
        var cacheState = cacheEnabled ? LoadPipelineCache(cachePath, logger) : null;
        var cacheUpdated = false;
        var runStopwatch = Stopwatch.StartNew();

        var result = new WebPipelineResult
        {
            CachePath = cacheEnabled ? cachePath : null
        };
        var steps = BuildStepDefinitions(stepsElement);
        var totalSteps = steps.Count;
        var stepResultsByIndex = new Dictionary<int, WebPipelineStepResult>();

        foreach (var definition in steps)
        {
            var step = definition.Element;
            var task = definition.Task;
            var stepIndex = definition.Index;
            var label = $"[{stepIndex}/{totalSteps}] {task}";
            logger?.Info($"Starting {label}...");
            var stopwatch = Stopwatch.StartNew();
            var stepResult = new WebPipelineStepResult { Task = task };
            var cacheKey = $"{stepIndex}:{task}";
            var stepFingerprint = string.Empty;
            var expectedOutputs = GetExpectedStepOutputs(task, step, baseDir);
            if (definition.DependencyIndexes.Length > 0)
            {
                foreach (var dependencyIndex in definition.DependencyIndexes)
                {
                    if (!stepResultsByIndex.TryGetValue(dependencyIndex, out var dependencyResult) || !dependencyResult.Success)
                    {
                        throw new InvalidOperationException($"Step '{definition.Id}' dependency #{dependencyIndex} failed or was not executed.");
                    }
                }
            }

            var dependencyMiss = definition.DependencyIndexes.Any(index =>
                !stepResultsByIndex.TryGetValue(index, out var dependencyResult) || !dependencyResult.Cached);
            if (cacheEnabled && cacheState is not null)
            {
                stepFingerprint = ComputeStepFingerprint(baseDir, step);
                if (cacheState.Entries.TryGetValue(cacheKey, out var cacheEntry) &&
                    string.Equals(cacheEntry.Fingerprint, stepFingerprint, StringComparison.Ordinal) &&
                    !dependencyMiss &&
                    AreExpectedOutputsPresent(expectedOutputs))
                {
                    stepResult.Success = true;
                    stepResult.Cached = true;
                    stepResult.Message = AppendDuration(cacheEntry.Message ?? "cache hit", stopwatch);
                    stepResult.DurationMs = (long)Math.Round(stopwatch.Elapsed.TotalMilliseconds);
                    result.Steps.Add(stepResult);
                    stepResultsByIndex[stepIndex] = stepResult;
                    if (profileEnabled)
                        logger?.Info($"Finished {label} (cache hit) in {FormatDuration(stopwatch.Elapsed)}");
                    continue;
                }
            }

            try
            {
                switch (task)
                {
                    case "build":
                    {
                        var config = ResolvePath(baseDir, GetString(step, "config"));
                        var outPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output"));
                        var cleanOutput = GetBool(step, "clean") ?? false;
                        if (string.IsNullOrWhiteSpace(config) || string.IsNullOrWhiteSpace(outPath))
                            throw new InvalidOperationException("build requires config and out.");

                        var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(config, WebCliJson.Options);
                        var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);
                        if (cleanOutput)
                            WebCliFileSystem.CleanOutputDirectory(outPath);
                        var build = WebSiteBuilder.Build(spec, plan, outPath, WebCliJson.Options);
                        stepResult.Success = true;
                        stepResult.Message = $"Built {build.OutputPath}";
                        break;
                    }
                    case "verify":
                    {
                        var config = ResolvePath(baseDir, GetString(step, "config"));
                        if (string.IsNullOrWhiteSpace(config))
                            throw new InvalidOperationException("verify requires config.");
                        var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(config, WebCliJson.Options);
                        var failOnWarnings = GetBool(step, "failOnWarnings") ?? spec.Verify?.FailOnWarnings ?? false;
                        var failOnNavLint = GetBool(step, "failOnNavLint") ?? GetBool(step, "failOnNavLintWarnings") ?? spec.Verify?.FailOnNavLint ?? false;
                        var failOnThemeContract = GetBool(step, "failOnThemeContract") ?? spec.Verify?.FailOnThemeContract ?? false;
                        var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);
                        var verify = WebSiteVerifier.Verify(spec, plan);
                        var (verifySuccess, verifyPolicyFailures) = WebVerifyPolicy.EvaluateOutcome(
                            verify,
                            failOnWarnings,
                            failOnNavLint,
                            failOnThemeContract);
                        if (!verifySuccess)
                        {
                            var firstFailure = verifyPolicyFailures.Length > 0
                                ? verifyPolicyFailures[0]
                                : "Web verify failed.";
                            throw new InvalidOperationException(firstFailure);
                        }

                        var warnCount = verify.Warnings.Length;
                        stepResult.Success = true;
                        stepResult.Message = warnCount > 0
                            ? $"Verify {warnCount} warnings"
                            : "Verify ok";
                        break;
                    }
                    case "markdown-fix":
                    {
                        var config = ResolvePath(baseDir, GetString(step, "config"));
                        var rootPath = ResolvePath(baseDir, GetString(step, "root") ?? GetString(step, "path") ?? GetString(step, "siteRoot"));
                        var include = GetString(step, "include");
                        var exclude = GetString(step, "exclude");
                        var applyFixes = GetBool(step, "apply") ?? false;

                        if (string.IsNullOrWhiteSpace(rootPath) && !string.IsNullOrWhiteSpace(config))
                        {
                            var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(config, WebCliJson.Options);
                            var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);
                            var contentRoot = string.IsNullOrWhiteSpace(spec.ContentRoot) ? "content" : spec.ContentRoot;
                            rootPath = Path.IsPathRooted(contentRoot)
                                ? contentRoot
                                : Path.Combine(plan.RootPath, contentRoot);
                        }

                        if (string.IsNullOrWhiteSpace(rootPath))
                            throw new InvalidOperationException("markdown-fix requires root/path/siteRoot or config.");

                        var fix = WebMarkdownHygieneFixer.Fix(new WebMarkdownFixOptions
                        {
                            RootPath = rootPath,
                            Include = CliPatternHelper.SplitPatterns(include),
                            Exclude = CliPatternHelper.SplitPatterns(exclude),
                            ApplyChanges = applyFixes
                        });

                        stepResult.Success = fix.Success;
                        stepResult.Message = applyFixes
                            ? $"Markdown fix updated {fix.ChangedFileCount}/{fix.FileCount} files ({fix.ReplacementCount} replacements)"
                            : $"Markdown fix dry-run {fix.ChangedFileCount}/{fix.FileCount} files ({fix.ReplacementCount} replacements)";
                        break;
                    }
                    case "apidocs":
                    {
                        var typeText = GetString(step, "type");
                        var xml = ResolvePath(baseDir, GetString(step, "xml"));
                        var help = ResolvePath(baseDir, GetString(step, "help") ?? GetString(step, "helpPath") ?? GetString(step, "help-path"));
                        var outPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output"));
                        var assembly = ResolvePath(baseDir, GetString(step, "assembly"));
                        var title = GetString(step, "title");
                        var baseUrl = GetString(step, "baseUrl") ?? GetString(step, "base-url") ?? "/api";
                        var format = GetString(step, "format");
                        var css = GetString(step, "css");
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
                        var sourceUrl = GetString(step, "sourceUrl") ?? GetString(step, "source-url") ?? GetString(step, "sourcePattern") ?? GetString(step, "source-pattern");
                        var includeUndocumented = GetBool(step, "includeUndocumented") ?? GetBool(step, "include-undocumented") ?? true;
                        var nav = ResolvePath(baseDir, GetString(step, "nav") ?? GetString(step, "navJson") ?? GetString(step, "nav-json"));
                        var includeNamespaces = GetString(step, "includeNamespace") ?? GetString(step, "include-namespace");
                        var excludeNamespaces = GetString(step, "excludeNamespace") ?? GetString(step, "exclude-namespace");
                        var includeTypes = GetString(step, "includeType") ?? GetString(step, "include-type");
                        var excludeTypes = GetString(step, "excludeType") ?? GetString(step, "exclude-type");
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
                            SourceUrlPattern = sourceUrl,
                            IncludeUndocumentedTypes = includeUndocumented,
                            NavJsonPath = nav
                        };
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

                        var res = WebApiDocsGenerator.Generate(options);
                        var note = res.UsedReflectionFallback ? " (reflection)" : string.Empty;
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
                            else
                            {
                                note += $" ({res.Warnings.Length} warnings)";
                            }
                        }
                        stepResult.Success = true;
                        stepResult.Message = $"API docs {res.TypeCount} types{note}";
                        break;
                    }
                    case "changelog":
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
                        break;
                    }
                    case "llms":
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
                        break;
                    }
                    case "sitemap":
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
                        break;
                    }
                    case "optimize":
                    {
                        var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
                        if (string.IsNullOrWhiteSpace(siteRoot))
                            throw new InvalidOperationException("optimize requires siteRoot.");

                        var configPath = ResolvePath(baseDir, GetString(step, "config"));
                        var minifyHtml = GetBool(step, "minifyHtml") ?? false;
                        var minifyCss = GetBool(step, "minifyCss") ?? false;
                        var minifyJs = GetBool(step, "minifyJs") ?? false;
                        var optimizeImages = GetBool(step, "optimizeImages") ?? GetBool(step, "images") ?? false;
                        var imageExtensions = GetArrayOfStrings(step, "imageExtensions") ?? GetArrayOfStrings(step, "image-ext");
                        var imageInclude = GetArrayOfStrings(step, "imageInclude") ?? GetArrayOfStrings(step, "image-include");
                        var imageExclude = GetArrayOfStrings(step, "imageExclude") ?? GetArrayOfStrings(step, "image-exclude");
                        var imageQuality = GetInt(step, "imageQuality") ?? GetInt(step, "image-quality") ?? 82;
                        var imageStripMetadata = GetBool(step, "imageStripMetadata") ?? GetBool(step, "image-strip-metadata") ?? true;
                        var imageGenerateWebp = GetBool(step, "imageGenerateWebp") ?? GetBool(step, "image-generate-webp") ?? false;
                        var imageGenerateAvif = GetBool(step, "imageGenerateAvif") ?? GetBool(step, "image-generate-avif") ?? false;
                        var imagePreferNextGen = GetBool(step, "imagePreferNextGen") ?? GetBool(step, "image-prefer-nextgen") ?? false;
                        var imageWidths = GetArrayOfStrings(step, "imageWidths") ?? GetArrayOfStrings(step, "image-widths");
                        var imageEnhanceTags = GetBool(step, "imageEnhanceTags") ?? GetBool(step, "image-enhance-tags") ?? false;
                        var imageMaxBytes = GetLong(step, "imageMaxBytesPerFile") ?? GetLong(step, "image-max-bytes") ?? 0;
                        var imageMaxTotalBytes = GetLong(step, "imageMaxTotalBytes") ?? GetLong(step, "image-max-total-bytes") ?? 0;
                        var imageFailOnBudget = GetBool(step, "imageFailOnBudget") ?? GetBool(step, "image-fail-on-budget") ?? false;
                        var hashAssets = GetBool(step, "hashAssets") ?? false;
                        var hashExtensions = GetArrayOfStrings(step, "hashExtensions") ?? GetArrayOfStrings(step, "hash-ext");
                        var hashExclude = GetArrayOfStrings(step, "hashExclude") ?? GetArrayOfStrings(step, "hash-exclude");
                        var hashManifest = GetString(step, "hashManifest") ?? GetString(step, "hash-manifest");
                        var reportPath = GetString(step, "reportPath") ?? GetString(step, "report-path");
                        var cacheHeaders = GetBool(step, "cacheHeaders") ?? GetBool(step, "headers") ?? false;
                        var cacheHeadersOut = GetString(step, "cacheHeadersOut") ?? GetString(step, "headersOut") ?? GetString(step, "headers-out");
                        var cacheHeadersHtml = GetString(step, "cacheHeadersHtml") ?? GetString(step, "headersHtml");
                        var cacheHeadersAssets = GetString(step, "cacheHeadersAssets") ?? GetString(step, "headersAssets");
                        var cacheHeadersPaths = GetArrayOfStrings(step, "cacheHeadersPaths") ?? GetArrayOfStrings(step, "headersPaths");
                        if (string.IsNullOrWhiteSpace(reportPath) &&
                            (minifyHtml || minifyCss || minifyJs || optimizeImages || hashAssets || cacheHeaders))
                        {
                            reportPath = ResolvePathWithinRoot(baseDir, null, Path.Combine(".powerforge", "optimize-report.json"));
                        }

                        AssetPolicySpec? policy = null;
                        if (!string.IsNullOrWhiteSpace(configPath))
                        {
                            var (spec, _) = WebSiteSpecLoader.LoadWithPath(configPath, WebCliJson.Options);
                            policy = spec.AssetPolicy;
                        }
                        if (cacheHeaders)
                        {
                            policy ??= new AssetPolicySpec();
                            policy.CacheHeaders ??= new CacheHeadersSpec { Enabled = true };
                            policy.CacheHeaders.Enabled = true;
                            if (!string.IsNullOrWhiteSpace(cacheHeadersOut))
                                policy.CacheHeaders.OutputPath = cacheHeadersOut;
                            if (!string.IsNullOrWhiteSpace(cacheHeadersHtml))
                                policy.CacheHeaders.HtmlCacheControl = cacheHeadersHtml;
                            if (!string.IsNullOrWhiteSpace(cacheHeadersAssets))
                                policy.CacheHeaders.ImmutableCacheControl = cacheHeadersAssets;
                            if (cacheHeadersPaths is { Length: > 0 })
                                policy.CacheHeaders.ImmutablePaths = cacheHeadersPaths;
                        }

                        var optimize = WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions
                        {
                            SiteRoot = siteRoot,
                            CriticalCssPath = ResolvePath(baseDir, GetString(step, "criticalCss") ?? GetString(step, "critical-css")),
                            CssLinkPattern = GetString(step, "cssPattern") ?? "(app|api-docs)\\.css",
                            MinifyHtml = minifyHtml,
                            MinifyCss = minifyCss,
                            MinifyJs = minifyJs,
                            OptimizeImages = optimizeImages,
                            ImageExtensions = imageExtensions ?? new[] { ".png", ".jpg", ".jpeg", ".webp" },
                            ImageInclude = imageInclude ?? Array.Empty<string>(),
                            ImageExclude = imageExclude ?? Array.Empty<string>(),
                            ImageQuality = imageQuality,
                            ImageStripMetadata = imageStripMetadata,
                            ImageGenerateWebp = imageGenerateWebp,
                            ImageGenerateAvif = imageGenerateAvif,
                            ImagePreferNextGen = imagePreferNextGen,
                            ResponsiveImageWidths = ParseIntList(imageWidths),
                            EnhanceImageTags = imageEnhanceTags,
                            ImageMaxBytesPerFile = imageMaxBytes,
                            ImageMaxTotalBytes = imageMaxTotalBytes,
                            HashAssets = hashAssets,
                            HashExtensions = hashExtensions ?? new[] { ".css", ".js" },
                            HashExclude = hashExclude ?? Array.Empty<string>(),
                            HashManifestPath = hashManifest,
                            ReportPath = reportPath,
                            AssetPolicy = policy
                        });
                        if (imageFailOnBudget && optimize.ImageBudgetExceeded)
                            throw new InvalidOperationException($"Image budget exceeded: {string.Join(" | ", optimize.ImageBudgetWarnings)}");
                        stepResult.Success = true;
                        stepResult.Message = BuildOptimizeSummary(optimize);
                        break;
                    }
                    case "audit":
                    {
                        var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
                        if (string.IsNullOrWhiteSpace(siteRoot))
                            throw new InvalidOperationException("audit requires siteRoot.");

                        var include = GetString(step, "include");
                        var exclude = GetString(step, "exclude");
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
                        if ((baselineGenerate || baselineUpdate) && string.IsNullOrWhiteSpace(baselinePath))
                            baselinePath = "audit-baseline.json";
                        var useDefaultExclude = !(GetBool(step, "noDefaultExclude") ?? false);
                        var useDefaultIgnoreNav = !(GetBool(step, "noDefaultIgnoreNav") ?? false);
                        var ignoreNavList = CliPatternHelper.SplitPatterns(ignoreNav).ToList();
                        var ignoreNavPatterns = BuildIgnoreNavPatternsForPipeline(ignoreNavList, useDefaultIgnoreNav);
                        var navRequiredValue = navRequired ?? !(navOptional ?? false);
                        var navIgnorePrefixList = CliPatternHelper.SplitPatterns(navIgnorePrefixes);
                        var navProfiles = LoadAuditNavProfilesForPipeline(baseDir, navProfilesPath);
                        var resolvedSummaryPath = ResolveSummaryPathForPipeline(summary, summaryPath);
                        if (string.IsNullOrWhiteSpace(resolvedSummaryPath) && summaryOnFail)
                            resolvedSummaryPath = ResolvePathWithinRoot(baseDir, null, Path.Combine(".powerforge", "audit-summary.json"));

                        var resolvedSarifPath = ResolveSarifPathForPipeline(sarif, sarifPath);
                        if (string.IsNullOrWhiteSpace(resolvedSarifPath) && sarifOnFail)
                            resolvedSarifPath = ResolvePathWithinRoot(baseDir, null, Path.Combine(".powerforge", "audit.sarif"));

                        var ensureInstall = rendered && (renderedEnsureInstalled ?? true);
                        var audit = WebSiteAuditor.Audit(new WebAuditOptions
                        {
                            SiteRoot = siteRoot,
                            Include = CliPatternHelper.SplitPatterns(include),
                            Exclude = CliPatternHelper.SplitPatterns(exclude),
                            UseDefaultExcludes = useDefaultExclude,
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
                            SummaryPath = ResolveSummaryPathForPipeline(summary, summaryPath),
                            SarifPath = resolvedSarifPath,
                            SummaryMaxIssues = summaryMax,
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
                            baselineWrittenPath = WebAuditBaselineStore.Write(siteRoot, baselinePath, audit, baselineUpdate, logger);
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
                        break;
                    }
                    case "doctor":
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
                            var failOnWarnings = GetBool(step, "failOnWarnings") ?? spec.Verify?.FailOnWarnings ?? false;
                            var failOnNavLint = GetBool(step, "failOnNavLint") ?? GetBool(step, "failOnNavLintWarnings") ?? spec.Verify?.FailOnNavLint ?? false;
                            var failOnThemeContract = GetBool(step, "failOnThemeContract") ?? spec.Verify?.FailOnThemeContract ?? false;
                            var (verifySuccess, policyFailures) = WebVerifyPolicy.EvaluateOutcome(
                                verify,
                                failOnWarnings,
                                failOnNavLint,
                                failOnThemeContract);
                            verifyPolicyFailures = policyFailures;
                            if (!verifySuccess)
                            {
                                var firstFailure = policyFailures.Length > 0
                                    ? policyFailures[0]
                                    : "Web verify failed.";
                                throw new InvalidOperationException(firstFailure);
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
                            var resolvedSummaryPath = ResolveSummaryPathForPipeline(summary, summaryPath);
                            if (string.IsNullOrWhiteSpace(resolvedSummaryPath) && summaryOnFail)
                                resolvedSummaryPath = ResolvePathWithinRoot(baseDir, null, Path.Combine(".powerforge", "audit-summary.json"));

                            var resolvedSarifPath = ResolveSarifPathForPipeline(sarif, sarifPath);
                            if (string.IsNullOrWhiteSpace(resolvedSarifPath) && sarifOnFail)
                                resolvedSarifPath = ResolvePathWithinRoot(baseDir, null, Path.Combine(".powerforge", "audit.sarif"));

                            audit = WebSiteAuditor.Audit(new WebAuditOptions
                            {
                                SiteRoot = effectiveSiteRoot!,
                                Include = CliPatternHelper.SplitPatterns(include),
                                Exclude = CliPatternHelper.SplitPatterns(exclude),
                                UseDefaultExcludes = useDefaultExclude,
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
                        break;
                    }
                    case "dotnet-build":
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
                        if (!res.Success) throw new InvalidOperationException(buildError);
                        break;
                    }
                    case "dotnet-publish":
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
                        if (!res.Success) throw new InvalidOperationException(publishError);
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
                        break;
                    }
                    case "overlay":
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
                        break;
                    }
                    default:
                        stepResult.Success = false;
                        stepResult.Message = "Unknown task";
                        break;
                }
            }
            catch (Exception ex)
            {
                stepResult.Success = false;
                stepResult.Message = AppendDuration(ex.Message, stopwatch);
                stepResult.DurationMs = (long)Math.Round(stopwatch.Elapsed.TotalMilliseconds);
                result.Steps.Add(stepResult);
                stepResultsByIndex[stepIndex] = stepResult;
                result.StepCount = result.Steps.Count;
                result.Success = false;
                result.DurationMs = (long)Math.Round(runStopwatch.Elapsed.TotalMilliseconds);
                if (cacheEnabled && cacheState is not null && cacheUpdated)
                    SavePipelineCache(cachePath, cacheState, logger);
                if (!string.IsNullOrWhiteSpace(profilePath) && (profileEnabled || profileWriteOnFail))
                {
                    WritePipelineProfile(profilePath, result, logger);
                    result.ProfilePath = profilePath;
                }
                return result;
            }

            stepResult.Message = AppendDuration(stepResult.Message, stopwatch);
            stepResult.DurationMs = (long)Math.Round(stopwatch.Elapsed.TotalMilliseconds);
            if (cacheEnabled && cacheState is not null && !string.IsNullOrWhiteSpace(stepFingerprint))
            {
                cacheState.Entries[cacheKey] = new WebPipelineCacheEntry
                {
                    Fingerprint = stepFingerprint,
                    Message = stepResult.Message
                };
                cacheUpdated = true;
            }
            result.Steps.Add(stepResult);
            stepResultsByIndex[stepIndex] = stepResult;
            if (profileEnabled)
                logger?.Info($"Finished {label} in {FormatDuration(stopwatch.Elapsed)}");
        }

        runStopwatch.Stop();
        result.StepCount = result.Steps.Count;
        result.Success = result.Steps.All(s => s.Success);
        result.DurationMs = (long)Math.Round(runStopwatch.Elapsed.TotalMilliseconds);
        if (cacheEnabled && cacheState is not null && cacheUpdated)
            SavePipelineCache(cachePath, cacheState, logger);
        if (!string.IsNullOrWhiteSpace(profilePath) && profileEnabled)
        {
            WritePipelineProfile(profilePath, result, logger);
            result.ProfilePath = profilePath;
        }
        return result;
    }

    private static List<PipelineStepDefinition> BuildStepDefinitions(JsonElement stepsElement)
    {
        var steps = new List<PipelineStepDefinition>();
        var aliases = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var step in stepsElement.EnumerateArray())
        {
            var task = GetString(step, "task")?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(task))
                continue;

            index++;
            var id = GetString(step, "id");
            if (string.IsNullOrWhiteSpace(id))
                id = $"{task}-{index}";

            if (aliases.ContainsKey(id))
                throw new InvalidOperationException($"Duplicate pipeline step id '{id}'.");

            aliases[id] = index;
            aliases[$"{task}#{index}"] = index;
            if (!aliases.ContainsKey(task))
                aliases[task] = index;

            steps.Add(new PipelineStepDefinition
            {
                Index = index,
                Task = task,
                Id = id,
                DependsOn = ParseDependsOn(step),
                Element = step
            });
        }

        foreach (var step in steps)
        {
            if (step.DependsOn.Length == 0)
                continue;

            var resolved = new List<int>();
            foreach (var dependency in step.DependsOn)
            {
                if (string.IsNullOrWhiteSpace(dependency))
                    continue;

                if (int.TryParse(dependency, out var numeric))
                {
                    if (numeric <= 0 || numeric > steps.Count)
                        throw new InvalidOperationException($"Step '{step.Id}' has invalid dependsOn reference '{dependency}'.");
                    resolved.Add(numeric);
                    continue;
                }

                if (!aliases.TryGetValue(dependency, out var dependencyIndex))
                    throw new InvalidOperationException($"Step '{step.Id}' has unknown dependsOn reference '{dependency}'.");

                resolved.Add(dependencyIndex);
            }

            step.DependencyIndexes = resolved
                .Distinct()
                .OrderBy(value => value)
                .ToArray();

            if (step.DependencyIndexes.Any(value => value >= step.Index))
                throw new InvalidOperationException($"Step '{step.Id}' has dependsOn reference to current/future step.");
        }

        return steps;
    }

    private static string[] ParseDependsOn(JsonElement step)
    {
        var array = GetArrayOfStrings(step, "dependsOn") ?? GetArrayOfStrings(step, "depends-on");
        if (array is { Length: > 0 })
            return array;

        var value = GetString(step, "dependsOn") ?? GetString(step, "depends-on");
        return CliPatternHelper.SplitPatterns(value);
    }

    private static string AppendDuration(string? message, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        var duration = FormatDuration(stopwatch.Elapsed);
        var baseMessage = string.IsNullOrWhiteSpace(message) ? "Completed" : message;
        return $"{baseMessage} ({duration})";
    }

    private static string FormatDuration(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 1)
            return $"{elapsed.TotalMilliseconds:0} ms";
        if (elapsed.TotalMinutes < 1)
            return $"{elapsed.TotalSeconds:0.0} s";
        return $"{elapsed.TotalMinutes:0.0} min";
    }

    private static string BuildOptimizeSummary(WebOptimizeResult result)
    {
        var parts = new List<string> { $"updated {result.UpdatedCount}" };

        if (result.CriticalCssInlinedCount > 0)
            parts.Add($"critical-css {result.CriticalCssInlinedCount}");
        if (result.HtmlMinifiedCount > 0)
            parts.Add($"html {result.HtmlMinifiedCount}");
        if (result.CssMinifiedCount > 0)
            parts.Add($"css {result.CssMinifiedCount}");
        if (result.JsMinifiedCount > 0)
            parts.Add($"js {result.JsMinifiedCount}");
        if (result.HtmlBytesSaved > 0)
            parts.Add($"html-saved {result.HtmlBytesSaved}B");
        if (result.CssBytesSaved > 0)
            parts.Add($"css-saved {result.CssBytesSaved}B");
        if (result.JsBytesSaved > 0)
            parts.Add($"js-saved {result.JsBytesSaved}B");
        if (result.ImageOptimizedCount > 0)
            parts.Add($"images {result.ImageOptimizedCount}");
        if (result.ImageBytesSaved > 0)
            parts.Add($"images-saved {result.ImageBytesSaved}B");
        if (result.ImageVariantCount > 0)
            parts.Add($"image-variants {result.ImageVariantCount}");
        if (result.ImageHtmlRewriteCount > 0)
            parts.Add($"image-rewrites {result.ImageHtmlRewriteCount}");
        if (result.ImageHintedCount > 0)
            parts.Add($"image-hints {result.ImageHintedCount}");
        if (result.OptimizedImages.Length > 0)
        {
            var top = result.OptimizedImages[0];
            parts.Add($"top-image {top.Path}(-{top.BytesSaved}B)");
        }
        if (result.ImageBudgetExceeded)
            parts.Add("image-budget-exceeded");

        if (result.HashedAssetCount > 0)
            parts.Add($"hashed {result.HashedAssetCount}");
        if (result.CacheHeadersWritten)
            parts.Add("headers");
        if (!string.IsNullOrWhiteSpace(result.ReportPath))
            parts.Add("report");

        return $"Optimize {string.Join(", ", parts)}";
    }

    private static string BuildAuditSummary(WebAuditResult result)
    {
        var parts = new List<string>
        {
            $"pages {result.PageCount}",
            $"links {result.LinkCount}",
            $"assets {result.AssetCount}"
        };

        if (result.BrokenLinkCount > 0)
            parts.Add($"broken-links {result.BrokenLinkCount}");
        if (result.MissingAssetCount > 0)
            parts.Add($"missing-assets {result.MissingAssetCount}");
        parts.Add($"nav-checked {result.NavCheckedCount}");
        if (result.NavIgnoredCount > 0)
            parts.Add($"nav-ignored {result.NavIgnoredCount}");
        parts.Add($"nav-coverage {result.NavCoveragePercent:0.0}%");
        if (result.NavMismatchCount > 0)
            parts.Add($"nav-mismatches {result.NavMismatchCount}");
        if (result.RequiredRouteCount > 0)
            parts.Add($"required-routes {result.RequiredRouteCount}");
        if (result.MissingRequiredRouteCount > 0)
            parts.Add($"missing-required-routes {result.MissingRequiredRouteCount}");
        if (result.WarningCount > 0)
            parts.Add($"warnings {result.WarningCount}");
        if (result.NewIssueCount > 0)
            parts.Add($"new {result.NewIssueCount}");
        if (!string.IsNullOrWhiteSpace(result.SarifPath))
            parts.Add("sarif");

        return $"Audit ok {string.Join(", ", parts)}";
    }

    private static string BuildDoctorSummary(
        WebVerifyResult? verify,
        WebAuditResult? audit,
        bool buildExecuted,
        bool verifyExecuted,
        bool auditExecuted,
        string[]? policyFailures = null)
    {
        var parts = new List<string>();
        parts.Add(buildExecuted ? "build" : "no-build");
        parts.Add(verifyExecuted ? "verify" : "no-verify");
        parts.Add(auditExecuted ? "audit" : "no-audit");

        if (verify is not null)
            parts.Add($"verify {verify.Errors.Length}e/{verify.Warnings.Length}w");
        if (audit is not null)
            parts.Add($"audit {audit.ErrorCount}e/{audit.WarningCount}w");
        if (audit is not null && !string.IsNullOrWhiteSpace(audit.SummaryPath))
            parts.Add("summary");
        if (audit is not null && !string.IsNullOrWhiteSpace(audit.SarifPath))
            parts.Add("sarif");
        if (policyFailures is { Length: > 0 })
            parts.Add($"verify-policy {policyFailures.Length}");

        return $"Doctor ok {string.Join(", ", parts)}";
    }

    private static string BuildAuditFailureSummary(WebAuditResult result, int previewCount)
    {
        var safePreviewCount = Math.Clamp(previewCount, 0, 50);
        var parts = new List<string>
        {
            $"Audit failed ({result.Errors.Length} errors)"
        };

        if (!string.IsNullOrWhiteSpace(result.SummaryPath))
            parts.Add($"summary {result.SummaryPath}");
        if (!string.IsNullOrWhiteSpace(result.SarifPath))
            parts.Add($"sarif {result.SarifPath}");

        if (safePreviewCount <= 0 || result.Errors.Length == 0)
            return string.Join(", ", parts);

        var preview = result.Errors
            .Where(static error => !string.IsNullOrWhiteSpace(error))
            .Take(safePreviewCount)
            .Select(error => TruncateForLog(error, 220))
            .ToArray();

        if (preview.Length == 0)
            return string.Join(", ", parts);

        var previewText = string.Join(" | ", preview);
        var remaining = result.Errors.Length - preview.Length;
        if (remaining > 0)
            previewText += $" | +{remaining} more";

        parts.Add($"sample: {previewText}");

        if (result.Issues.Length > 0)
        {
            var issuePreviewCount = Math.Min(safePreviewCount, 5);
            if (issuePreviewCount > 0)
            {
                var candidateIssues = result.Issues
                    .Where(static issue => !IsGateIssue(issue))
                    .ToArray();
                if (candidateIssues.Length == 0)
                    candidateIssues = result.Issues;

                var issueSample = candidateIssues
                    .Where(static issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase))
                    .Take(issuePreviewCount)
                    .ToArray();

                if (issueSample.Length == 0)
                {
                    issueSample = candidateIssues
                        .Where(static issue => !string.IsNullOrWhiteSpace(issue.Message))
                        .Take(issuePreviewCount)
                        .ToArray();
                }

                if (issueSample.Length > 0)
                {
                    var issueText = string.Join(" | ", issueSample.Select(FormatIssueForLog));
                    var issueRemaining = result.Issues.Length - issueSample.Length;
                    if (issueRemaining > 0)
                        issueText += $" | +{issueRemaining} more issues";

                    parts.Add($"issues: {issueText}");
                }
            }
        }

        return string.Join(", ", parts);
    }

    private static bool IsGateIssue(WebAuditIssue issue)
    {
        if (string.Equals(issue.Category, "gate", StringComparison.OrdinalIgnoreCase))
            return true;

        return issue.Message.StartsWith("Audit gate failed", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatIssueForLog(WebAuditIssue issue)
    {
        var severity = string.IsNullOrWhiteSpace(issue.Severity) ? "warning" : issue.Severity;
        var category = string.IsNullOrWhiteSpace(issue.Category) ? "general" : issue.Category;
        var location = string.IsNullOrWhiteSpace(issue.Path) ? string.Empty : $" {issue.Path}";
        var message = string.IsNullOrWhiteSpace(issue.Message) ? "issue reported" : issue.Message;
        return TruncateForLog($"[{severity}] [{category}]{location} {message}", 220);
    }

    private static string TruncateForLog(string text, int maxLength)
    {
        var normalized = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (normalized.Length <= maxLength)
            return normalized;

        return normalized[..Math.Max(0, maxLength - 3)] + "...";
    }

    private static WebPipelineCacheState LoadPipelineCache(string cachePath, WebConsoleLogger? logger)
    {
        try
        {
            if (!File.Exists(cachePath))
                return new WebPipelineCacheState();

            var fileInfo = new FileInfo(cachePath);
            if (fileInfo.Length > MaxStateFileSizeBytes)
            {
                logger?.Warn($"Pipeline cache file too large ({fileInfo.Length} bytes), ignoring cache.");
                return new WebPipelineCacheState();
            }

            using var stream = File.OpenRead(cachePath);
            var state = JsonSerializer.Deserialize<WebPipelineCacheState>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return state ?? new WebPipelineCacheState();
        }
        catch (Exception ex)
        {
            logger?.Warn($"Pipeline cache load failed: {ex.Message}");
            return new WebPipelineCacheState();
        }
    }

    private static void SavePipelineCache(string cachePath, WebPipelineCacheState state, WebConsoleLogger? logger)
    {
        try
        {
            var directory = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(cachePath, json);
        }
        catch (Exception ex)
        {
            logger?.Warn($"Pipeline cache save failed: {ex.Message}");
        }
    }

    private static void WritePipelineProfile(string profilePath, WebPipelineResult result, WebConsoleLogger? logger)
    {
        try
        {
            var directory = Path.GetDirectoryName(profilePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(result, WebCliJson.Context.WebPipelineResult);
            File.WriteAllText(profilePath, json);
        }
        catch (Exception ex)
        {
            logger?.Warn($"Pipeline profile write failed: {ex.Message}");
        }
    }

    private static string ComputeStepFingerprint(string baseDir, JsonElement step)
    {
        var parts = new List<string> { step.GetRawText() };
        var paths = EnumerateFingerprintPaths(baseDir, step)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            parts.Add(BuildPathStamp(path));
        }

        var payload = string.Join('\n', parts);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static IEnumerable<string> EnumerateFingerprintPaths(string baseDir, JsonElement step)
    {
        if (step.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (var property in step.EnumerateObject())
        {
            if (!FingerprintPathKeys.Contains(property.Name))
                continue;

            if (property.Value.ValueKind == JsonValueKind.String)
            {
                var value = property.Value.GetString();
                if (string.IsNullOrWhiteSpace(value) || IsExternalUri(value))
                    continue;
                var resolved = ResolvePath(baseDir, value);
                if (!string.IsNullOrWhiteSpace(resolved))
                    yield return Path.GetFullPath(resolved);
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in property.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                    continue;
                var value = item.GetString();
                if (string.IsNullOrWhiteSpace(value) || IsExternalUri(value))
                    continue;
                var resolved = ResolvePath(baseDir, value);
                if (!string.IsNullOrWhiteSpace(resolved))
                    yield return Path.GetFullPath(resolved);
            }
        }
    }

    private static string BuildPathStamp(string path)
    {
        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            return $"f|{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }

        if (!Directory.Exists(path))
            return $"m|{path}";

        try
        {
            var maxTicks = Directory.GetLastWriteTimeUtc(path).Ticks;
            var fileCount = 0;
            var truncated = false;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                if (fileCount >= MaxStampFileCount)
                {
                    truncated = true;
                    break;
                }

                fileCount++;
                var ticks = File.GetLastWriteTimeUtc(file).Ticks;
                if (ticks > maxTicks)
                    maxTicks = ticks;
            }

            return truncated
                ? $"d|{path}|{fileCount}|{maxTicks}|truncated"
                : $"d|{path}|{fileCount}|{maxTicks}";
        }
        catch
        {
            return $"d|{path}|unreadable";
        }
    }

    private static bool IsExternalUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;
        return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
               uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] GetExpectedStepOutputs(string task, JsonElement step, string baseDir)
    {
        switch (task)
        {
            case "build":
                return ResolveOutputCandidates(baseDir, GetString(step, "out") ?? GetString(step, "output"));
            case "apidocs":
                return ResolveOutputCandidates(baseDir, GetString(step, "out") ?? GetString(step, "output"));
            case "dotnet-publish":
                return ResolveOutputCandidates(baseDir, GetString(step, "out") ?? GetString(step, "output"));
            case "overlay":
                return ResolveOutputCandidates(baseDir, GetString(step, "destination") ?? GetString(step, "dest"));
            case "changelog":
                return ResolveOutputCandidates(baseDir, GetString(step, "out") ?? GetString(step, "output"));
            case "llms":
            {
                var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
                if (string.IsNullOrWhiteSpace(siteRoot))
                    return Array.Empty<string>();
                return new[]
                {
                    Path.Combine(siteRoot, "llms.txt"),
                    Path.Combine(siteRoot, "llms.json"),
                    Path.Combine(siteRoot, "llms-full.txt")
                };
            }
            case "sitemap":
            {
                var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
                var outPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output"));
                if (string.IsNullOrWhiteSpace(outPath) && !string.IsNullOrWhiteSpace(siteRoot))
                    outPath = Path.Combine(siteRoot, "sitemap.xml");
                return ResolveOutputCandidates(baseDir, outPath);
            }
            case "optimize":
            {
                var outputs = new List<string>();
                var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
                var reportPath = GetString(step, "reportPath") ?? GetString(step, "report-path");
                var hashManifest = GetString(step, "hashManifest") ?? GetString(step, "hash-manifest");
                var cacheHeaders = GetBool(step, "cacheHeaders") ?? GetBool(step, "headers") ?? false;
                var cacheHeadersOut = GetString(step, "cacheHeadersOut") ?? GetString(step, "headersOut") ?? GetString(step, "headers-out");

                if (!string.IsNullOrWhiteSpace(siteRoot))
                {
                    if (!string.IsNullOrWhiteSpace(reportPath))
                        outputs.AddRange(ResolveOutputCandidates(siteRoot, reportPath));
                    if (!string.IsNullOrWhiteSpace(hashManifest))
                        outputs.AddRange(ResolveOutputCandidates(siteRoot, hashManifest));
                    if (cacheHeaders)
                    {
                        var headersPath = string.IsNullOrWhiteSpace(cacheHeadersOut) ? "_headers" : cacheHeadersOut;
                        outputs.AddRange(ResolveOutputCandidates(siteRoot, headersPath));
                    }
                }

                return outputs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            case "audit":
            {
                var outputs = new List<string>();
                var summaryEnabled = GetBool(step, "summary") ?? false;
                var summaryPath = GetString(step, "summaryPath");
                var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
                if (summaryEnabled || !string.IsNullOrWhiteSpace(summaryPath))
                {
                    if (string.IsNullOrWhiteSpace(summaryPath))
                        summaryPath = "audit-summary.json";
                    if (!string.IsNullOrWhiteSpace(siteRoot) && !Path.IsPathRooted(summaryPath))
                        summaryPath = Path.Combine(siteRoot, summaryPath);
                    outputs.AddRange(ResolveOutputCandidates(baseDir, summaryPath));
                }

                var sarifEnabled = GetBool(step, "sarif") ?? false;
                var sarifPath = GetString(step, "sarifPath") ?? GetString(step, "sarif-path");
                if (sarifEnabled || !string.IsNullOrWhiteSpace(sarifPath))
                {
                    if (string.IsNullOrWhiteSpace(sarifPath))
                        sarifPath = "audit.sarif.json";
                    if (!string.IsNullOrWhiteSpace(siteRoot) && !Path.IsPathRooted(sarifPath))
                        sarifPath = Path.Combine(siteRoot, sarifPath);
                    outputs.AddRange(ResolveOutputCandidates(baseDir, sarifPath));
                }

                return outputs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            case "doctor":
            {
                var outputs = new List<string>();
                var configPath = ResolvePath(baseDir, GetString(step, "config"));
                var outPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output"));
                var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
                var runBuild = GetBool(step, "build");
                var noBuild = GetBool(step, "noBuild") ?? false;
                var executeBuild = runBuild ?? !noBuild;
                if (string.IsNullOrWhiteSpace(outPath) && !string.IsNullOrWhiteSpace(configPath))
                    outPath = Path.Combine(Path.GetDirectoryName(configPath) ?? ".", "_site");
                var effectiveSiteRoot = string.IsNullOrWhiteSpace(siteRoot) ? outPath : siteRoot;

                if (executeBuild && !string.IsNullOrWhiteSpace(outPath))
                    outputs.AddRange(ResolveOutputCandidates(baseDir, outPath));

                var runAudit = GetBool(step, "audit");
                var noAudit = GetBool(step, "noAudit") ?? false;
                var executeAudit = runAudit ?? !noAudit;
                if (executeAudit && !string.IsNullOrWhiteSpace(effectiveSiteRoot))
                {
                    var summaryEnabled = GetBool(step, "summary") ?? false;
                    var summaryPath = GetString(step, "summaryPath");
                    if (summaryEnabled || !string.IsNullOrWhiteSpace(summaryPath))
                    {
                        if (string.IsNullOrWhiteSpace(summaryPath))
                            summaryPath = "audit-summary.json";
                        if (!Path.IsPathRooted(summaryPath))
                            summaryPath = Path.Combine(effectiveSiteRoot, summaryPath);
                        outputs.AddRange(ResolveOutputCandidates(baseDir, summaryPath));
                    }

                    var sarifEnabled = GetBool(step, "sarif") ?? false;
                    var sarifPath = GetString(step, "sarifPath") ?? GetString(step, "sarif-path");
                    if (sarifEnabled || !string.IsNullOrWhiteSpace(sarifPath))
                    {
                        if (string.IsNullOrWhiteSpace(sarifPath))
                            sarifPath = "audit.sarif.json";
                        if (!Path.IsPathRooted(sarifPath))
                            sarifPath = Path.Combine(effectiveSiteRoot, sarifPath);
                        outputs.AddRange(ResolveOutputCandidates(baseDir, sarifPath));
                    }
                }

                return outputs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            default:
                return Array.Empty<string>();
        }
    }

    private static string[] ResolveOutputCandidates(string baseDir, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();
        if (IsExternalUri(value))
            return Array.Empty<string>();
        var resolved = ResolvePath(baseDir, value);
        if (string.IsNullOrWhiteSpace(resolved))
            return Array.Empty<string>();
        return new[] { Path.GetFullPath(resolved) };
    }

    private static bool AreExpectedOutputsPresent(string[] outputs)
    {
        if (outputs.Length == 0)
            return true;

        foreach (var output in outputs)
        {
            if (File.Exists(output))
                continue;
            if (Directory.Exists(output))
                continue;
            return false;
        }

        return true;
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static bool? GetBool(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind == JsonValueKind.True ? true :
               value.ValueKind == JsonValueKind.False ? false : null;
    }

    private static int? GetInt(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var num)) return num;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed)) return parsed;
        return null;
    }

    private static long? GetLong(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var num)) return num;
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed)) return parsed;
        return null;
    }

    private static int[] ParseIntList(string[]? values)
    {
        if (values is null || values.Length == 0)
            return Array.Empty<int>();

        var list = new List<int>();
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            foreach (var token in value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(token.Trim(), out var parsed) && parsed > 0)
                    list.Add(parsed);
            }
        }

        return list
            .Distinct()
            .OrderBy(v => v)
            .ToArray();
    }

    private static WebApiDetailLevel ParseApiDetailLevel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return WebApiDetailLevel.None;
        return Enum.TryParse<WebApiDetailLevel>(value, true, out var parsed) ? parsed : WebApiDetailLevel.None;
    }

    private static string[]? GetArrayOfStrings(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return null;

        var list = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
                list.Add(item.GetString() ?? string.Empty);
            else if (item.ValueKind != JsonValueKind.Null)
                list.Add(item.ToString());
        }
        return list.Count == 0 ? null : list.ToArray();
    }

    private static WebSitemapEntry[] GetSitemapEntries(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return Array.Empty<WebSitemapEntry>();
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return Array.Empty<WebSitemapEntry>();

        var list = new List<WebSitemapEntry>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var path = GetString(item, "path") ?? GetString(item, "route") ?? GetString(item, "url");
            if (string.IsNullOrWhiteSpace(path)) continue;
            list.Add(new WebSitemapEntry
            {
                Path = path,
                ChangeFrequency = GetString(item, "changefreq") ?? GetString(item, "changeFrequency"),
                Priority = GetString(item, "priority"),
                LastModified = GetString(item, "lastmod") ?? GetString(item, "lastModified")
            });
        }
        return list.ToArray();
    }

    private static string? ResolvePath(string baseDir, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Path.IsPathRooted(value) ? value : Path.Combine(baseDir, value);
    }

    private static string ResolvePathWithinRoot(string baseDir, string? value, string defaultRelativePath)
    {
        var normalizedRoot = NormalizeRootPath(baseDir);
        var candidate = string.IsNullOrWhiteSpace(value)
            ? Path.Combine(baseDir, defaultRelativePath)
            : ResolvePath(baseDir, value);
        var resolved = Path.GetFullPath(candidate ?? Path.Combine(baseDir, defaultRelativePath));
        if (!IsPathWithinRoot(normalizedRoot, resolved))
            throw new InvalidOperationException($"Path must resolve under pipeline root: {value}");
        return resolved;
    }

    private static string NormalizeRootPath(string path)
    {
        var full = Path.GetFullPath(path);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    private static bool IsPathWithinRoot(string normalizedRoot, string candidatePath)
    {
        var full = Path.GetFullPath(candidatePath);
        return full.StartsWith(normalizedRoot, FileSystemPathComparison);
    }

    private static string[] BuildIgnoreNavPatternsForPipeline(List<string> userPatterns, bool useDefaults)
    {
        if (!useDefaults)
            return userPatterns.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();

        var defaults = new WebAuditOptions().IgnoreNavFor;
        if (userPatterns.Count == 0)
            return defaults;

        return defaults.Concat(userPatterns)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ResolveSummaryPathForPipeline(bool summaryEnabled, string? summaryPath)
    {
        if (!summaryEnabled && string.IsNullOrWhiteSpace(summaryPath))
            return null;

        return string.IsNullOrWhiteSpace(summaryPath) ? "audit-summary.json" : summaryPath;
    }

    private static string? ResolveSarifPathForPipeline(bool sarifEnabled, string? sarifPath)
    {
        if (!sarifEnabled && string.IsNullOrWhiteSpace(sarifPath))
            return null;

        return string.IsNullOrWhiteSpace(sarifPath) ? "audit.sarif.json" : sarifPath;
    }

    private static WebAuditNavProfile[] LoadAuditNavProfilesForPipeline(string baseDir, string? navProfilesPath)
    {
        if (string.IsNullOrWhiteSpace(navProfilesPath))
            return Array.Empty<WebAuditNavProfile>();

        var resolvedPath = ResolvePath(baseDir, navProfilesPath);
        if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            throw new FileNotFoundException($"Nav profile file not found: {navProfilesPath}", resolvedPath ?? navProfilesPath);

        using var stream = File.OpenRead(resolvedPath);
        var profiles = JsonSerializer.Deserialize(stream, WebCliJson.Context.WebAuditNavProfileArray)
                       ?? Array.Empty<WebAuditNavProfile>();
        return profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Match))
            .ToArray();
    }
}
