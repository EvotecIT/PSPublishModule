using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace PowerForge.Web;

/// <summary>Builds a static site from configuration and content.</summary>
public static partial class WebSiteBuilder
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly AsyncLocal<Action<string>?> UpdatedSink = new();
    private static readonly StringComparison FileSystemPathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex TocHeaderRegex = new("<h(?<level>[2-3])[^>]*>(?<text>.*?)</h\\1>", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex StripTagsRegex = new("<.*?>", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex SnippetParagraphRegex = new("<p\\b[^>]*>(?<text>[\\s\\S]*?)</p>", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex HrefRegex = new("href\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex WhitespaceRegex = new("\\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex CodeBlockRegex = new("<pre(?<preAttrs>[^>]*)>\\s*<code(?<codeAttrs>[^>]*)>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex ClassAttrRegex = new("class\\s*=\\s*\"(?<value>[^\"]*)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly AsyncLocal<BuildLanguageContext?> BuildLanguageContextScope = new();
    private static readonly AsyncLocal<BuildRenderCache?> BuildRenderCacheScope = new();
    private static readonly AsyncLocal<Action<string>?> BuildProgressSink = new();
    private static readonly AsyncLocal<Dictionary<string, IReadOnlyDictionary<string, object?>>?> BuildProjectDataCacheScope = new();
    /// <summary>Builds the site output.</summary>
    /// <param name="spec">Site configuration.</param>
    /// <param name="plan">Resolved site plan.</param>
    /// <param name="outputPath">Output directory.</param>
    /// <param name="options">Optional JSON serializer options.</param>
    /// <param name="language">Optional language code filter (for example: en, pl).</param>
    /// <param name="languageAsRoot">When true and language is set, render selected language routes without language prefix.</param>
    /// <param name="progress">Optional progress sink for long-running build phases.</param>
    /// <returns>Result payload describing the build output.</returns>
    public static WebBuildResult Build(
        SiteSpec spec,
        WebSitePlan plan,
        string outputPath,
        JsonSerializerOptions? options = null,
        string? language = null,
        bool languageAsRoot = false,
        Action<string>? progress = null)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("Output path is required.", nameof(outputPath));

        var outDir = Path.GetFullPath(outputPath.Trim().Trim('"'));
        Directory.CreateDirectory(outDir);

        var metaDir = Path.Combine(outDir, "_powerforge");
        Directory.CreateDirectory(metaDir);

        var jsonOptions = WebJson.Options;
        var planPath = Path.Combine(metaDir, "site-plan.json");
        var specPath = Path.Combine(metaDir, "site-spec.json");
        var redirectsPath = Path.Combine(metaDir, "redirects.json");

        var updated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedOut = NormalizeRootPathForSink(outDir);
        var buildStopwatch = Stopwatch.StartNew();
        void ReportProgress(string message)
        {
            if (progress is null || string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                progress($"[{buildStopwatch.Elapsed:mm\\:ss}] {message}");
            }
            catch
            {
                // Progress reporting must never break the build flow.
            }
        }
        void MarkUpdated(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var full = Path.GetFullPath(path);
            if (!IsPathWithinRoot(normalizedOut, full)) return;
            var relative = Path.GetRelativePath(outDir, full).Replace('\\', '/');
            updated.Add(relative);
        }

        var prevSink = UpdatedSink.Value;
        var prevBuildLanguageContext = BuildLanguageContextScope.Value;
        var prevBuildRenderCache = BuildRenderCacheScope.Value;
        var prevBuildProgressSink = BuildProgressSink.Value;
        var prevBuildProjectDataCache = BuildProjectDataCacheScope.Value;
        UpdatedSink.Value = MarkUpdated;
        BuildLanguageContextScope.Value = new BuildLanguageContext
        {
            Language = language,
            LanguageAsRoot = languageAsRoot
        };
        BuildRenderCacheScope.Value = CreateBuildRenderCache(spec, plan.RootPath);
        BuildProgressSink.Value = ReportProgress;
        BuildProjectDataCacheScope.Value = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            ReportProgress($"build start (output={outDir})");
            WriteAllTextIfChanged(planPath, JsonSerializer.Serialize(plan, jsonOptions));
            WriteAllTextIfChanged(specPath, JsonSerializer.Serialize(spec, jsonOptions));

            var redirects = new List<RedirectSpec>();
            if (spec.RouteOverrides is { Length: > 0 }) redirects.AddRange(spec.RouteOverrides);
            if (spec.Redirects is { Length: > 0 }) redirects.AddRange(spec.Redirects);

            ReportProgress("loading project specs");
            var projectSpecs = LoadProjectSpecs(plan.ProjectsRoot, options ?? WebJson.Options).ToList();
            foreach (var project in projectSpecs)
            {
                if (project.Redirects is { Length: > 0 })
                    redirects.AddRange(project.Redirects);
            }
            AddVersioningAliasRedirects(spec, redirects);

            ReportProgress($"loaded project specs: {projectSpecs.Count}");
            ReportProgress("loading data sources");
            var data = LoadData(spec, plan, projectSpecs);
            var projectMap = projectSpecs
                .Where(p => !string.IsNullOrWhiteSpace(p.Slug))
                .ToDictionary(p => p.Slug, StringComparer.OrdinalIgnoreCase);
            var projectContentMap = projectSpecs
                .Where(p => p.Content is not null && !string.IsNullOrWhiteSpace(p.Slug))
                .ToDictionary(p => p.Slug, p => p.Content!, StringComparer.OrdinalIgnoreCase);
            var cacheRoot = ResolveCacheRoot(spec, plan.RootPath);
            ReportProgress("building content items");
            var allItems = BuildContentItems(spec, plan, redirects, data, projectMap, projectContentMap, cacheRoot);
            ReportProgress($"content items: {allItems.Count}");
            allItems.AddRange(BuildTaxonomyItems(spec, allItems));
            ReportProgress($"with taxonomy items: {allItems.Count}");
            allItems = BuildPaginatedItems(spec, allItems);
            ReportProgress($"with pagination items: {allItems.Count}");
            var renderItems = FilterItemsForLanguage(spec, allItems, language);
            ReportProgress($"render queue size: {renderItems.Count}");

            AddLegacyAmpRedirects(spec, redirects, renderItems);
            ResolveXrefs(spec, plan.RootPath, metaDir, renderItems);
            var menuSpecs = BuildMenuSpecs(spec, renderItems, plan.RootPath);
            var totalRenderItems = renderItems.Count;
            var renderInterval = totalRenderItems switch
            {
                >= 5000 => 250,
                >= 2000 => 50,
                >= 1000 => 50,
                >= 500 => 25,
                >= 100 => 10,
                _ => 2
            };
            var rendered = 0;
            var lastProgressReportAt = buildStopwatch.Elapsed;
            ReportProgress("rendering content");
            foreach (var item in renderItems)
            {
                var currentIndex = rendered + 1;
                var currentRoute = string.IsNullOrWhiteSpace(item.OutputPath) ? "/" : item.OutputPath;
                if (currentIndex <= 3 || currentIndex == totalRenderItems || (currentIndex % renderInterval == 0))
                {
                    ReportProgress($"render start: {currentIndex}/{totalRenderItems} route={currentRoute}");
                }

                var itemStopwatch = Stopwatch.StartNew();
                using var itemHeartbeat = progress is null
                    ? null
                    : new Timer(_ =>
                    {
                        ReportProgress(
                            $"render working: {currentIndex}/{totalRenderItems} route={currentRoute} elapsed={itemStopwatch.Elapsed:mm\\:ss}");
                    }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

                WriteContentItem(outDir, spec, plan.RootPath, item, allItems, data, projectMap, menuSpecs);
                itemHeartbeat?.Dispose();
                rendered++;
                var itemElapsed = itemStopwatch.Elapsed;
                if (itemElapsed >= TimeSpan.FromSeconds(3))
                    ReportProgress(
                        $"render slow-item: {currentIndex}/{totalRenderItems} route={currentRoute} took {itemElapsed.TotalSeconds:F1}s");
                var nowElapsed = buildStopwatch.Elapsed;
                var shouldReportByIndex = rendered <= 3 || rendered == totalRenderItems || (rendered % renderInterval == 0);
                var shouldReportByTime = nowElapsed - lastProgressReportAt >= TimeSpan.FromSeconds(5);
                if (shouldReportByIndex || shouldReportByTime)
                {
                    var percent = totalRenderItems <= 0 ? 100 : (int)Math.Round((double)rendered * 100 / totalRenderItems);
                    ReportProgress($"render progress: {rendered}/{totalRenderItems} ({percent}%)");
                    lastProgressReportAt = nowElapsed;
                }
            }

            ReportProgress("copying theme assets");
            CopyThemeAssets(spec, plan.RootPath, outDir);
            ReportProgress("copying static assets");
            CopyStaticAssets(spec, plan.RootPath, outDir);
            ReportProgress("writing navigation/search/diagnostic outputs");
            WriteSiteNavData(spec, outDir, menuSpecs);
            WriteSearchIndex(spec, outDir, renderItems);
            WriteLinkCheckReport(spec, renderItems, metaDir);
            WriteSeoPreviewReport(spec, renderItems, metaDir);
            WriteCrawlPolicyReport(spec, renderItems, metaDir);

            var redirectsPayload = new
            {
                routeOverrides = spec.RouteOverrides,
                redirects = redirects
            };
            WriteAllTextIfChanged(redirectsPath, JsonSerializer.Serialize(redirectsPayload, jsonOptions));
            WriteRedirectOutputs(outDir, redirects);
            EnsureNoJekyllFile(outDir);
            ReportProgress($"build complete: updated files {updated.Count}");

            return new WebBuildResult
            {
                OutputPath = outDir,
                PlanPath = planPath,
                SpecPath = specPath,
                RedirectsPath = redirectsPath,
                UpdatedFiles = updated
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                GeneratedAtUtc = DateTime.UtcNow
            };
        }
        finally
        {
            UpdatedSink.Value = prevSink;
            BuildLanguageContextScope.Value = prevBuildLanguageContext;
            BuildRenderCacheScope.Value = prevBuildRenderCache;
            BuildProgressSink.Value = prevBuildProgressSink;
            BuildProjectDataCacheScope.Value = prevBuildProjectDataCache;
        }
    }

    private static void ReportBuildProgress(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var sink = BuildProgressSink.Value;
        if (sink is null)
            return;

        try
        {
            sink(message);
        }
        catch
        {
            // Build diagnostics must not break rendering.
        }
    }

    private static List<ContentItem> FilterItemsForLanguage(SiteSpec spec, IReadOnlyList<ContentItem> items, string? language)
    {
        if (items.Count == 0 || string.IsNullOrWhiteSpace(language))
            return items.ToList();

        var localization = ResolveLocalizationConfig(spec);
        var targetLanguage = ResolveEffectiveLanguageCode(localization, language);
        return items
            .Where(item => ResolveEffectiveLanguageCode(localization, item.Language)
                .Equals(targetLanguage, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private sealed class BuildLanguageContext
    {
        public string? Language { get; init; }
        public bool LanguageAsRoot { get; init; }
    }

    private static void EnsureNoJekyllFile(string outputRoot)
    {
        var markerPath = Path.Combine(outputRoot, ".nojekyll");
        if (!File.Exists(markerPath))
            WriteAllTextIfChanged(markerPath, string.Empty);
    }

    private static void CopyThemeAssets(SiteSpec spec, string rootPath, string outputRoot)
    {
        if (string.IsNullOrWhiteSpace(spec.DefaultTheme))
            return;

        var themeRoot = ResolveThemeRoot(spec, rootPath);
        if (string.IsNullOrWhiteSpace(themeRoot) || !Directory.Exists(themeRoot))
            return;

        var loader = new ThemeLoader();
        var manifest = loader.Load(themeRoot, ResolveThemesRoot(spec, rootPath));
        if (manifest is null)
            return;

        var outputThemesFolder = ResolveThemesFolder(spec);
        var normalizedOutputRoot = NormalizeRootPathForSink(outputRoot);

        var chain = BuildThemeChain(themeRoot, manifest);
        foreach (var entry in chain)
        {
            var assetsDir = entry.Manifest.AssetsPath ?? "assets";
            if (string.IsNullOrWhiteSpace(assetsDir))
                continue;

            var source = Path.GetFullPath(Path.Combine(entry.Root, assetsDir));
            if (!IsPathWithinBase(entry.Root, source))
            {
                Trace.TraceWarning($"Skipping theme assets outside theme root: {assetsDir}");
                continue;
            }
            if (!Directory.Exists(source))
                continue;

            var entryThemeName = string.IsNullOrWhiteSpace(entry.Manifest.Name)
                ? Path.GetFileName(entry.Root)
                : entry.Manifest.Name;
            if (string.IsNullOrWhiteSpace(entryThemeName))
                entryThemeName = spec.DefaultTheme ?? "theme";

            var destination = Path.GetFullPath(Path.Combine(outputRoot, outputThemesFolder, entryThemeName, assetsDir));
            if (!IsPathWithinRoot(normalizedOutputRoot, destination))
            {
                Trace.TraceWarning($"Skipping theme assets destination outside output root: {destination}");
                continue;
            }
            CopyDirectory(source, destination);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
        {
            var destFile = Path.Combine(destination, Path.GetFileName(file));
            CopyFileIfChanged(file, destFile);
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            var destDir = Path.Combine(destination, Path.GetFileName(dir));
            CopyDirectory(dir, destDir);
        }
    }

    private static void CopyStaticAssets(SiteSpec spec, string rootPath, string outputRoot)
    {
        if (spec.StaticAssets is null || spec.StaticAssets.Length == 0)
            return;

        var normalizedOutputRoot = NormalizeRootPathForSink(outputRoot);

        foreach (var asset in spec.StaticAssets)
        {
            if (string.IsNullOrWhiteSpace(asset.Source))
                continue;

            var sourcePath = Path.IsPathRooted(asset.Source)
                ? asset.Source
                : Path.Combine(rootPath, asset.Source);

            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
                continue;

            var destination = (asset.Destination ?? string.Empty).TrimStart('/', '\\');
            if (File.Exists(sourcePath))
            {
                var destPath = string.IsNullOrWhiteSpace(destination)
                    ? Path.Combine(outputRoot, Path.GetFileName(sourcePath))
                    : (Path.HasExtension(destination)
                        ? Path.Combine(outputRoot, destination)
                        : Path.Combine(outputRoot, destination, Path.GetFileName(sourcePath)));
                destPath = Path.GetFullPath(destPath);
                if (!IsPathWithinRoot(normalizedOutputRoot, destPath))
                {
                    Trace.TraceWarning($"Skipping static asset file copy outside output root: {asset.Source} -> {asset.Destination}");
                    continue;
                }

                var destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrWhiteSpace(destDir))
                    Directory.CreateDirectory(destDir);

                CopyFileIfChanged(sourcePath, destPath);
                continue;
            }

            var targetRoot = string.IsNullOrWhiteSpace(destination)
                ? outputRoot
                : Path.Combine(outputRoot, destination);
            targetRoot = Path.GetFullPath(targetRoot);
            if (!IsPathWithinRoot(normalizedOutputRoot, targetRoot))
            {
                Trace.TraceWarning($"Skipping static asset directory copy outside output root: {asset.Source} -> {asset.Destination}");
                continue;
            }
            CopyDirectory(sourcePath, targetRoot);
        }
    }

    private static bool WriteAllTextIfChanged(string path, string content)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        // Keep output timestamps stable when content didn't change. This enables pipeline step caching
        // (and avoids expensive optimize/audit reruns) on large sites during local iteration.
        try
        {
            if (File.Exists(path))
            {
                var existing = File.ReadAllText(path);
                if (string.Equals(existing, content, StringComparison.Ordinal))
                    return false;
            }
        }
        catch
        {
            // If comparison fails, fall back to writing to keep the build correct.
        }

        File.WriteAllText(path, content, Utf8NoBom);
        UpdatedSink.Value?.Invoke(path);
        return true;
    }

    private static bool WriteAllLinesIfChanged(string path, IEnumerable<string> lines)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var payload = string.Join(Environment.NewLine, lines);
        return WriteAllTextIfChanged(path, payload);
    }

    private static bool CopyFileIfChanged(string sourcePath, string destPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(destPath))
            return false;

        try
        {
            if (File.Exists(destPath))
            {
                var srcInfo = new FileInfo(sourcePath);
                var dstInfo = new FileInfo(destPath);

                // Fast path: same size and destination is at least as new as source.
                if (srcInfo.Length == dstInfo.Length && dstInfo.LastWriteTimeUtc >= srcInfo.LastWriteTimeUtc)
                    return false;
            }
        }
        catch
        {
            // If file metadata comparison fails, fall back to copying.
        }

        var destDir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrWhiteSpace(destDir))
            Directory.CreateDirectory(destDir);

        File.Copy(sourcePath, destPath, overwrite: true);
        try
        {
            // Preserve source timestamp so downstream caching based on mtimes behaves predictably.
            File.SetLastWriteTimeUtc(destPath, File.GetLastWriteTimeUtc(sourcePath));
        }
        catch
        {
            // best-effort
        }

        UpdatedSink.Value?.Invoke(destPath);
        return true;
    }

    private static string NormalizeRootPathForSink(string path)
    {
        var full = Path.GetFullPath(path);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    private static bool IsPathWithinRoot(string normalizedRoot, string candidatePath)
    {
        var root = normalizedRoot
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var full = Path.GetFullPath(candidatePath);
        if (full.Equals(root, FileSystemPathComparison))
            return true;

        return full.StartsWith(root + Path.DirectorySeparatorChar, FileSystemPathComparison);
    }

}

