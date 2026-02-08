using System;
using System.Collections.Generic;
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
    private static readonly Regex HrefRegex = new("href\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex WhitespaceRegex = new("\\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex CodeBlockRegex = new("<pre(?<preAttrs>[^>]*)>\\s*<code(?<codeAttrs>[^>]*)>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex ClassAttrRegex = new("class\\s*=\\s*\"(?<value>[^\"]*)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    /// <summary>Builds the site output.</summary>
    /// <param name="spec">Site configuration.</param>
    /// <param name="plan">Resolved site plan.</param>
    /// <param name="outputPath">Output directory.</param>
    /// <param name="options">Optional JSON serializer options.</param>
    /// <returns>Result payload describing the build output.</returns>
    public static WebBuildResult Build(SiteSpec spec, WebSitePlan plan, string outputPath, JsonSerializerOptions? options = null)
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
        void MarkUpdated(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var full = Path.GetFullPath(path);
            if (!IsPathWithinRoot(normalizedOut, full)) return;
            var relative = Path.GetRelativePath(outDir, full).Replace('\\', '/');
            updated.Add(relative);
        }

        var prevSink = UpdatedSink.Value;
        UpdatedSink.Value = MarkUpdated;
        try
        {
            WriteAllTextIfChanged(planPath, JsonSerializer.Serialize(plan, jsonOptions));
            WriteAllTextIfChanged(specPath, JsonSerializer.Serialize(spec, jsonOptions));

        var redirects = new List<RedirectSpec>();
        if (spec.RouteOverrides is { Length: > 0 }) redirects.AddRange(spec.RouteOverrides);
        if (spec.Redirects is { Length: > 0 }) redirects.AddRange(spec.Redirects);

        var projectSpecs = LoadProjectSpecs(plan.ProjectsRoot, options ?? WebJson.Options).ToList();
        foreach (var project in projectSpecs)
        {
            if (project.Redirects is { Length: > 0 })
                redirects.AddRange(project.Redirects);
        }

        var data = LoadData(spec, plan, projectSpecs);
        var projectMap = projectSpecs
            .Where(p => !string.IsNullOrWhiteSpace(p.Slug))
            .ToDictionary(p => p.Slug, StringComparer.OrdinalIgnoreCase);
        var projectContentMap = projectSpecs
            .Where(p => p.Content is not null && !string.IsNullOrWhiteSpace(p.Slug))
            .ToDictionary(p => p.Slug, p => p.Content!, StringComparer.OrdinalIgnoreCase);
        var cacheRoot = ResolveCacheRoot(spec, plan.RootPath);
        var items = BuildContentItems(spec, plan, redirects, data, projectMap, projectContentMap, cacheRoot);
        items.AddRange(BuildTaxonomyItems(spec, items));
        items = BuildPaginatedItems(spec, items);
        var menuSpecs = BuildMenuSpecs(spec, items, plan.RootPath);
        foreach (var item in items)
        {
            WriteContentItem(outDir, spec, plan.RootPath, item, items, data, projectMap, menuSpecs);
        }

        CopyThemeAssets(spec, plan.RootPath, outDir);
        CopyStaticAssets(spec, plan.RootPath, outDir);
        WriteSiteNavData(spec, outDir, menuSpecs);
        WriteSearchIndex(outDir, items);
        WriteLinkCheckReport(spec, items, metaDir);

        var redirectsPayload = new
        {
            routeOverrides = spec.RouteOverrides,
            redirects = redirects
        };
            WriteAllTextIfChanged(redirectsPath, JsonSerializer.Serialize(redirectsPayload, jsonOptions));
            WriteRedirectOutputs(outDir, redirects);
            EnsureNoJekyllFile(outDir);

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
        }
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

        var chain = BuildThemeChain(themeRoot, manifest);
        foreach (var entry in chain)
        {
            var assetsDir = entry.Manifest.AssetsPath ?? "assets";
            if (string.IsNullOrWhiteSpace(assetsDir))
                continue;

            var source = Path.Combine(entry.Root, assetsDir);
            if (!Directory.Exists(source))
                continue;

            var entryThemeName = string.IsNullOrWhiteSpace(entry.Manifest.Name)
                ? Path.GetFileName(entry.Root)
                : entry.Manifest.Name;
            if (string.IsNullOrWhiteSpace(entryThemeName))
                entryThemeName = spec.DefaultTheme ?? "theme";

            var destination = Path.Combine(outputRoot, outputThemesFolder, entryThemeName, assetsDir);
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

                var destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrWhiteSpace(destDir))
                    Directory.CreateDirectory(destDir);

                CopyFileIfChanged(sourcePath, destPath);
                continue;
            }

            var targetRoot = string.IsNullOrWhiteSpace(destination)
                ? outputRoot
                : Path.Combine(outputRoot, destination);
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
        var full = Path.GetFullPath(candidatePath);
        return full.StartsWith(normalizedRoot, FileSystemPathComparison);
    }

}

