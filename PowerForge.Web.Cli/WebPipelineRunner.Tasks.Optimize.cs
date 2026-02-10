using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecuteOptimize(
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
            throw new InvalidOperationException("optimize requires siteRoot.");

        var configPath = ResolvePath(baseDir, GetString(step, "config"));
        var minifyHtml = GetBool(step, "minifyHtml") ?? false;
        var minifyCss = GetBool(step, "minifyCss") ?? false;
        var minifyJs = GetBool(step, "minifyJs") ?? false;
        var htmlInclude = GetArrayOfStrings(step, "htmlInclude") ?? GetArrayOfStrings(step, "html-include");
        var htmlExclude = GetArrayOfStrings(step, "htmlExclude") ?? GetArrayOfStrings(step, "html-exclude");
        var maxHtmlFiles = GetInt(step, "maxHtmlFiles") ?? GetInt(step, "max-html-files") ?? 0;
        var htmlScopeFromBuildUpdated = GetBool(step, "scopeFromBuildUpdated") ?? GetBool(step, "scope-from-build-updated");
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
        var imageFailOnFailures = GetBool(step, "imageFailOnFailures") ?? GetBool(step, "image-fail-on-failures") ??
                                  GetBool(step, "imageFailOnErrors") ?? GetBool(step, "image-fail-on-errors") ?? false;
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

        if (htmlScopeFromBuildUpdated != false &&
            (htmlScopeFromBuildUpdated == true || fast) &&
            (htmlInclude is null || htmlInclude.Length == 0) &&
            lastBuildUpdatedFiles.Length > 0 &&
            string.Equals(Path.GetFullPath(siteRoot), lastBuildOutPath, FileSystemPathComparison))
        {
            var updatedHtml = lastBuildUpdatedFiles
                .Where(static p => p.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                                   p.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (updatedHtml.Length > 0)
            {
                htmlInclude = updatedHtml;
                var modeLabel = fast ? "fast incremental" : "incremental";
                logger?.Info($"{label}: {modeLabel} html scope: {updatedHtml.Length} updated page(s)");
            }
        }

        if (fast)
        {
            var forced = new List<string>();
            if (optimizeImages)
            {
                optimizeImages = false;
                forced.Add("optimizeImages=false");
            }
            if (hashAssets)
            {
                hashAssets = false;
                forced.Add("hashAssets=false");
            }
            if (cacheHeaders)
            {
                cacheHeaders = false;
                forced.Add("cacheHeaders=false");
            }
            if (minifyCss)
            {
                minifyCss = false;
                forced.Add("minifyCss=false");
            }
            if (minifyJs)
            {
                minifyJs = false;
                forced.Add("minifyJs=false");
            }
            if (maxHtmlFiles <= 0)
            {
                // Optimize touches HTML multiple times (critical-css, rewrites, minify),
                // so default to a small scope for local iteration.
                maxHtmlFiles = 50;
                forced.Add("maxHtmlFiles=50");
            }
            if (forced.Count > 0)
                logger?.Warn($"{label}: fast mode overrides: {string.Join(", ", forced)}");
        }

        var optimize = WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions
        {
            SiteRoot = siteRoot,
            CriticalCssPath = ResolvePath(baseDir, GetString(step, "criticalCss") ?? GetString(step, "critical-css")),
            CssLinkPattern = GetString(step, "cssPattern") ?? "(app|api-docs)\\.css",
            MinifyHtml = minifyHtml,
            MinifyCss = minifyCss,
            MinifyJs = minifyJs,
            HtmlInclude = htmlInclude ?? Array.Empty<string>(),
            HtmlExclude = htmlExclude ?? Array.Empty<string>(),
            MaxHtmlFiles = Math.Max(0, maxHtmlFiles),
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

        if (imageFailOnFailures && optimize.ImageFailedCount > 0)
        {
            var sample = optimize.ImageFailures
                .Where(static f => f is not null && !string.IsNullOrWhiteSpace(f.Path))
                .Take(3)
                .Select(f => $"{f.Path} ({TruncateForLog(f.Error, 120)})")
                .ToArray();
            var sampleText = sample.Length > 0 ? $" Sample: {string.Join(" | ", sample)}" : string.Empty;
            throw new InvalidOperationException($"Image optimization failures: {optimize.ImageFailedCount}.{sampleText}");
        }

        stepResult.Success = true;
        stepResult.Message = BuildOptimizeSummary(optimize);
    }
}

