using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlTinkerX;
using ImageMagick;

namespace PowerForge.Web;

/// <summary>Optimizes generated site assets (critical CSS, minification).</summary>
public static partial class WebAssetOptimizer
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly StringComparer FileSystemPathComparer = GetFileSystemPathComparer(
        OperatingSystem.IsWindows(),
        OperatingSystem.IsMacOS());
    private static readonly HttpClient RewriteDownloadClient = CreateRewriteDownloadClient();
    private static readonly Regex HtmlAttrRegex = new("(?<attr>href|src)=\"(?<url>[^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex HtmlSrcSetAttrRegex = new("(?<attr>\\b(?:srcset|imagesrcset))\\s*=\\s*(?<quote>['\"])(?<value>[^'\"]+)\\k<quote>", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex CssUrlRegex = new("url\\((?<quote>['\"]?)(?<url>[^'\")]+)\\k<quote>\\)", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex StylesheetLinkRegex = new("<link\\s+rel=\"stylesheet\"\\s+href=\"([^\"]+)\"\\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex ImgTagRegex = new("<img\\b(?<attrs>[^>]*?)>", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex ImgSrcAttrRegex = new("\\bsrc\\s*=\\s*(?<quote>['\"])(?<value>[^'\"]+)\\k<quote>", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex ImgSrcSetAttrRegex = new("\\bsrcset\\s*=\\s*(?<quote>['\"])(?<value>[^'\"]+)\\k<quote>", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex ImgSizesAttrRegex = new("\\bsizes\\s*=\\s*(?<quote>['\"])(?<value>[^'\"]+)\\k<quote>", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex ImgWidthAttrRegex = new("\\bwidth\\s*=\\s*(?<quote>['\"])(?<value>[^'\"]+)\\k<quote>", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex ImgHeightAttrRegex = new("\\bheight\\s*=\\s*(?<quote>['\"])(?<value>[^'\"]+)\\k<quote>", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex ImgLoadingAttrRegex = new("\\bloading\\s*=\\s*(?<quote>['\"])(?<value>[^'\"]+)\\k<quote>", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex ImgDecodingAttrRegex = new("\\bdecoding\\s*=\\s*(?<quote>['\"])(?<value>[^'\"]+)\\k<quote>", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex JsonLdScriptTypeRegex = new(
        "(<script\\b[^>]*?\\btype\\s*=\\s*)application/ld\\+json(?=(?:\\s|>|/))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    private sealed class ImageVariantPlan
    {
        public string RelativePath { get; init; } = string.Empty;
        public int? Width { get; init; }
    }

    private sealed class ImageRewritePlan
    {
        public string SourceRelativePath { get; init; } = string.Empty;
        public string PreferredRelativePath { get; set; } = string.Empty;
        public int OriginalWidth { get; set; }
        public int OriginalHeight { get; set; }
        public List<ImageVariantPlan> ResponsiveVariants { get; } = new();
    }

    private static HttpClient CreateRewriteDownloadClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        };
        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
        return client;
    }

    internal static StringComparer GetFileSystemPathComparer(bool isWindows, bool isMacOS)
        => isWindows || isMacOS
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    /// <summary>Runs asset optimization and returns the count of updated HTML files.</summary>
    /// <param name="options">Optimization options.</param>
    /// <returns>Number of HTML files updated with critical CSS.</returns>
    public static int Optimize(WebAssetOptimizerOptions options)
    {
        return OptimizeDetailed(options).UpdatedCount;
    }

    /// <summary>Runs asset optimization and returns detailed counters.</summary>
    /// <param name="options">Optimization options.</param>
    /// <returns>Detailed optimization result.</returns>
    public static WebOptimizeResult OptimizeDetailed(WebAssetOptimizerOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        var siteRoot = Path.GetFullPath(options.SiteRoot);
        if (!Directory.Exists(siteRoot))
            throw new DirectoryNotFoundException($"Site root not found: {siteRoot}");

        var result = new WebOptimizeResult();
        var updatedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void MarkUpdated(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var full = Path.GetFullPath(path);
            if (updatedFiles.Add(full))
                result.UpdatedCount++;
        }

        var protectedStoryArtifacts = FindVisualStoryArtifactPaths(siteRoot);
        var allHtmlFiles = Directory.EnumerateFiles(siteRoot, "*.html", SearchOption.AllDirectories).ToArray();
        var mutableHtmlFiles = allHtmlFiles
            .Where(path => !protectedStoryArtifacts.Contains(Path.GetFullPath(path)))
            .ToArray();
        var htmlFiles = mutableHtmlFiles;
        var cssFiles = Directory.EnumerateFiles(siteRoot, "*.css", SearchOption.AllDirectories).ToArray();
        var jsFiles = Directory.EnumerateFiles(siteRoot, "*.js", SearchOption.AllDirectories).ToArray();
        result.HtmlFileCount = allHtmlFiles.Length;
        result.CssFileCount = cssFiles.Length;
        result.JsFileCount = jsFiles.Length;

        if (options.HtmlInclude is { Length: > 0 } || options.HtmlExclude is { Length: > 0 } || options.MaxHtmlFiles > 0)
        {
            htmlFiles = htmlFiles
                .Where(path =>
                {
                    var relative = ToRelative(siteRoot, path);
                    if (options.HtmlInclude is { Length: > 0 } && !IsIncluded(relative, options.HtmlInclude))
                        return false;
                    if (IsExcluded(relative, options.HtmlExclude))
                        return false;
                    return true;
                })
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (options.MaxHtmlFiles > 0 && htmlFiles.Length > options.MaxHtmlFiles)
                htmlFiles = htmlFiles.Take(options.MaxHtmlFiles).ToArray();
        }

        result.HtmlSelectedFileCount = htmlFiles.Length;
        var policy = options.AssetPolicy;
        if (policy?.Rewrites is { Length: > 0 })
        {
            var applicableRewrites = CopyRewriteAssets(
                policy.Rewrites,
                siteRoot,
                protectedStoryArtifacts);
            protectedStoryArtifacts.UnionWith(FindVisualStoryArtifactPaths(siteRoot));
            mutableHtmlFiles = mutableHtmlFiles
                .Where(path => !protectedStoryArtifacts.Contains(Path.GetFullPath(path)))
                .ToArray();
            htmlFiles = htmlFiles
                .Where(path => !protectedStoryArtifacts.Contains(Path.GetFullPath(path)))
                .ToArray();
            result.HtmlSelectedFileCount = htmlFiles.Length;
            foreach (var htmlFile in htmlFiles)
            {
                var content = File.ReadAllText(htmlFile);
                if (string.IsNullOrWhiteSpace(content)) continue;
                var updated = RewriteHtmlAssets(content, applicableRewrites);
                updated = WebSiteBuilder.OptimizeNetworkHints(updated);
                if (!string.Equals(updated, content, StringComparison.Ordinal))
                {
                    File.WriteAllText(htmlFile, updated);
                    MarkUpdated(htmlFile);
                }
            }
        }
        var criticalCss = LoadCriticalCss(options.CriticalCssPath);
        Regex cssPattern;
        try
        {
            cssPattern = new Regex(options.CssLinkPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Invalid CssLinkPattern '{options.CssLinkPattern}': {ex.GetType().Name}: {ex.Message}");
            cssPattern = new Regex("(app|api-docs)\\.css", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
        }

        foreach (var htmlFile in htmlFiles)
        {
            var content = File.ReadAllText(htmlFile);
            if (string.IsNullOrWhiteSpace(content)) continue;

            if (!string.IsNullOrWhiteSpace(criticalCss) && !content.Contains("<!-- critical-css -->", StringComparison.OrdinalIgnoreCase))
            {
                var updated = InlineCriticalCss(content, criticalCss, cssPattern, options.CssStrategy);
                if (!string.Equals(updated, content, StringComparison.Ordinal))
                {
                    File.WriteAllText(htmlFile, updated);
                    result.CriticalCssInlinedCount++;
                    MarkUpdated(htmlFile);
                }
            }
        }

        // Optimize images before hashing so hashed filenames always match final image bytes.
        if (options.OptimizeImages)
        {
            OptimizeImages(siteRoot, htmlFiles, protectedStoryArtifacts, options, result, MarkUpdated);
        }

        var hashSpec = ResolveHashSpec(options, policy);
        Dictionary<string, string>? hashMap = null;
        // Fingerprinting moves shared assets and therefore requires every generated HTML
        // reference to be rewritten in the same pass. A sampled/incremental HTML scope
        // must never hash assets because untouched pages would retain broken old URLs.
        var hasCompleteHtmlScope = htmlFiles.Length == mutableHtmlFiles.Length;
        if (hashSpec.Enabled && hasCompleteHtmlScope)
        {
            hashMap = HashAssets(
                siteRoot,
                hashSpec,
                protectedStoryArtifacts,
                out var hashedAssetCount,
                out var hashedAssets,
                MarkUpdated);
            result.HashedAssetCount = hashedAssetCount;
            result.HashedAssets = hashedAssets.ToArray();
            if (hashMap.Count > 0)
            {
                var rewrites = RewriteHashedReferences(
                    siteRoot,
                    htmlFiles,
                    hashMap,
                    protectedStoryArtifacts,
                    MarkUpdated);
                result.HtmlHashRewriteCount = rewrites.HtmlFilesRewritten;
                result.CssHashRewriteCount = rewrites.CssFilesRewritten;
                var manifestPath = WriteHashManifest(
                    siteRoot,
                    hashSpec,
                    hashMap,
                    protectedStoryArtifacts);
                if (!string.IsNullOrWhiteSpace(manifestPath))
                {
                    result.HashManifestPath = manifestPath;
                    MarkUpdated(manifestPath);
                }
            }
        }

        if (options.MinifyHtml)
        {
            foreach (var htmlFile in htmlFiles)
            {
                var html = File.ReadAllText(htmlFile);
                if (string.IsNullOrWhiteSpace(html)) continue;
                string? minified = null;
                try
                {
                    minified = HtmlOptimizer.OptimizeHtml(html, cssDecodeEscapes: true, treatAsDocument: true);
                    minified = PreserveJsonLdScriptTypeQuotes(minified);
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"HTML minify failed for {htmlFile}: {ex.GetType().Name}: {ex.Message}");
                    minified = null;
                }
                if (!string.IsNullOrWhiteSpace(minified) && !string.Equals(html, minified, StringComparison.Ordinal))
                {
                    var beforeBytes = Encoding.UTF8.GetByteCount(html);
                    var afterBytes = Encoding.UTF8.GetByteCount(minified);
                    File.WriteAllText(htmlFile, minified);
                    result.HtmlMinifiedCount++;
                    result.HtmlBytesSaved += Math.Max(0, beforeBytes - afterBytes);
                    MarkUpdated(htmlFile);
                }
            }
        }

        if (options.MinifyCss)
        {
            foreach (var cssFile in Directory.EnumerateFiles(siteRoot, "*.css", SearchOption.AllDirectories)
                         .Where(path => !protectedStoryArtifacts.Contains(Path.GetFullPath(path))))
            {
                var css = File.ReadAllText(cssFile);
                if (string.IsNullOrWhiteSpace(css)) continue;
                string? minified = null;
                try
                {
                    minified = HtmlOptimizer.OptimizeCss(css);
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"CSS minify failed for {cssFile}: {ex.GetType().Name}: {ex.Message}");
                    minified = null;
                }
                if (!string.IsNullOrWhiteSpace(minified) && !string.Equals(css, minified, StringComparison.Ordinal))
                {
                    var beforeBytes = Encoding.UTF8.GetByteCount(css);
                    var afterBytes = Encoding.UTF8.GetByteCount(minified);
                    File.WriteAllText(cssFile, minified);
                    result.CssMinifiedCount++;
                    result.CssBytesSaved += Math.Max(0, beforeBytes - afterBytes);
                    MarkUpdated(cssFile);
                }
            }
        }

        if (options.MinifyJs)
        {
            foreach (var jsFile in Directory.EnumerateFiles(siteRoot, "*.js", SearchOption.AllDirectories)
                         .Where(path => !protectedStoryArtifacts.Contains(Path.GetFullPath(path))))
            {
                var js = File.ReadAllText(jsFile);
                if (string.IsNullOrWhiteSpace(js)) continue;
                string? minified = null;
                try
                {
                    minified = HtmlOptimizer.OptimizeJavaScript(js);
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"JS minify failed for {jsFile}: {ex.GetType().Name}: {ex.Message}");
                    minified = null;
                }
                if (!string.IsNullOrWhiteSpace(minified) && !string.Equals(js, minified, StringComparison.Ordinal))
                {
                    var beforeBytes = Encoding.UTF8.GetByteCount(js);
                    var afterBytes = Encoding.UTF8.GetByteCount(minified);
                    File.WriteAllText(jsFile, minified);
                    result.JsMinifiedCount++;
                    result.JsBytesSaved += Math.Max(0, beforeBytes - afterBytes);
                    MarkUpdated(jsFile);
                }
            }
        }

        if (policy?.CacheHeaders?.Enabled == true)
        {
            var headersPath = WriteCacheHeaders(
                siteRoot,
                policy.CacheHeaders,
                hashMap,
                protectedStoryArtifacts);
            if (!string.IsNullOrWhiteSpace(headersPath))
            {
                result.CacheHeadersWritten = true;
                result.CacheHeadersPath = headersPath;
                MarkUpdated(headersPath);
            }
        }

        result.UpdatedFiles = updatedFiles
            .Select(path => ToRelative(siteRoot, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Convenience summary sections for large reports.
        const int reportTopCount = 5;
        result.TopOptimizedImages = (result.OptimizedImages ?? Array.Empty<WebOptimizeImageEntry>())
            .Take(reportTopCount)
            .ToArray();
        result.TopImageFailures = (result.ImageFailures ?? Array.Empty<WebOptimizeImageFailureEntry>())
            .Take(reportTopCount)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(options.ReportPath))
        {
            if (TryResolveUnderRoot(siteRoot, options.ReportPath.TrimStart('/', '\\'), out var reportPath))
            {
                if (protectedStoryArtifacts.Contains(Path.GetFullPath(reportPath)))
                {
                    Trace.TraceWarning($"Optimize report path is protected by a visual-story manifest: {options.ReportPath}");
                }
                else
                {
                    result.ReportPath = reportPath;
                    var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    var write = true;
                    if (File.Exists(reportPath))
                    {
                        var existing = File.ReadAllText(reportPath);
                        write = !string.Equals(existing, json, StringComparison.Ordinal);
                    }
                    if (write)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
                        File.WriteAllText(reportPath, json);
                    }
                }
            }
            else
            {
                Trace.TraceWarning($"Optimize report path outside site root: {options.ReportPath}");
            }
        }

        return result;
    }

    private static string? PreserveJsonLdScriptTypeQuotes(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return html;

        return JsonLdScriptTypeRegex.Replace(html, "$1\"application/ld+json\"");
    }

    private static AssetHashSpec ResolveHashSpec(WebAssetOptimizerOptions options, AssetPolicySpec? policy)
    {
        if (options.HashAssets)
        {
            return new AssetHashSpec
            {
                Enabled = true,
                Extensions = options.HashExtensions ?? new[] { ".css", ".js" },
                Exclude = options.HashExclude ?? Array.Empty<string>(),
                ManifestPath = options.HashManifestPath
            };
        }

        return policy?.Hashing ?? new AssetHashSpec { Enabled = false };
    }

    private static AssetRewriteSpec[] CopyRewriteAssets(
        IEnumerable<AssetRewriteSpec> rewrites,
        string siteRoot,
        IReadOnlySet<string> protectedStoryArtifacts)
    {
        var applicable = new List<AssetRewriteSpec>();
        foreach (var rewrite in rewrites)
        {
            var hasCopySource =
                !string.IsNullOrWhiteSpace(rewrite.Source) ||
                !string.IsNullOrWhiteSpace(rewrite.SourceUrl);
            if (!hasCopySource)
            {
                applicable.Add(rewrite);
                continue;
            }

            if (string.IsNullOrWhiteSpace(rewrite.Destination))
            {
                Trace.TraceWarning("Asset rewrite with a copy source requires a destination.");
                continue;
            }

            var destRelative = rewrite.Destination.TrimStart('/', '\\');
            if (!TryResolveUnderRoot(siteRoot, destRelative, out var dest))
            {
                Trace.TraceWarning($"Asset rewrite destination outside site root: {rewrite.Destination}");
                continue;
            }
            if (protectedStoryArtifacts.Contains(Path.GetFullPath(dest)))
            {
                Trace.TraceWarning($"Asset rewrite destination is protected by a visual-story manifest: {rewrite.Destination}");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(rewrite.Source))
            {
                var source = Path.GetFullPath(rewrite.Source);
                if (!File.Exists(source))
                {
                    Trace.TraceWarning($"Asset rewrite source file was not found: {rewrite.Source}");
                    continue;
                }

                var destinationDirectory = Path.GetDirectoryName(dest);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                    Directory.CreateDirectory(destinationDirectory);
                File.Copy(source, dest, overwrite: true);
                applicable.Add(rewrite);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(rewrite.SourceUrl) &&
                DownloadRewriteAsset(rewrite, dest, protectedStoryArtifacts))
            {
                applicable.Add(rewrite);
            }
        }
        return applicable.ToArray();
    }

    private static string RewriteHtmlAssets(string html, AssetRewriteSpec[] rewrites)
    {
        if (rewrites.Length == 0) return html;
        return HtmlAttrRegex.Replace(html, match =>
        {
            var url = match.Groups["url"].Value;
            var decodedUrl = System.Web.HttpUtility.HtmlDecode(url);
            var replaced = ApplyRewriteRules(decodedUrl, rewrites);
            if (string.Equals(replaced, decodedUrl, StringComparison.Ordinal))
                return match.Value;

            var encoded = System.Web.HttpUtility.HtmlAttributeEncode(replaced) ?? string.Empty;
            return $"{match.Groups["attr"].Value}=\"{encoded}\"";
        });
    }

    private static string ApplyRewriteRules(string url, AssetRewriteSpec[] rewrites)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            return url;

        foreach (var rewrite in rewrites)
        {
            if (string.IsNullOrWhiteSpace(rewrite.Match)) continue;
            var kind = rewrite.MatchType?.ToLowerInvariant() ?? "contains";
            switch (kind)
            {
                case "exact":
                    if (string.Equals(url, rewrite.Match, StringComparison.OrdinalIgnoreCase))
                        return rewrite.Replace;
                    break;
                case "prefix":
                    if (url.StartsWith(rewrite.Match, StringComparison.OrdinalIgnoreCase))
                        return rewrite.Replace + url.Substring(rewrite.Match.Length);
                    break;
                case "regex":
                    try
                    {
                        var regex = new Regex(rewrite.Match, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
                        if (regex.IsMatch(url))
                            return regex.Replace(url, rewrite.Replace);
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceWarning($"Invalid rewrite regex '{rewrite.Match}': {ex.GetType().Name}: {ex.Message}");
                    }
                    break;
                default:
                    if (url.IndexOf(rewrite.Match, StringComparison.OrdinalIgnoreCase) >= 0)
                        return url.Replace(rewrite.Match, rewrite.Replace, StringComparison.OrdinalIgnoreCase);
                    break;
            }
        }
        return url;
    }

    private static Dictionary<string, string> HashAssets(
        string siteRoot,
        AssetHashSpec spec,
        IReadOnlySet<string> protectedStoryArtifacts,
        out int hashedAssetCount,
        out List<WebOptimizeHashedAssetEntry> hashedAssets,
        Action<string>? onUpdated = null)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        hashedAssets = new List<WebOptimizeHashedAssetEntry>();
        hashedAssetCount = 0;
        var extensions = (spec.Extensions?.Length ?? 0) == 0 ? new[] { ".css", ".js" } : spec.Extensions!;
        var files = Directory.EnumerateFiles(siteRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => extensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var file in files)
        {
            if (protectedStoryArtifacts.Contains(Path.GetFullPath(file)))
                continue;
            var relative = Path.GetRelativePath(siteRoot, file).Replace('\\', '/');
            if (IsExcluded(relative, spec.Exclude))
                continue;

            var hash = ComputeShortHash(File.ReadAllBytes(file));
            var ext = Path.GetExtension(relative);
            if (relative.Length <= ext.Length) continue;
            var without = relative.Substring(0, relative.Length - ext.Length);
            var hashedName = $"{without}.{hash}{ext}";
            var target = Path.GetFullPath(Path.Combine(siteRoot, hashedName.Replace('/', Path.DirectorySeparatorChar)));
            if (protectedStoryArtifacts.Contains(target))
            {
                Trace.TraceWarning($"Hashed asset destination is protected by a visual-story manifest: {hashedName}");
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(file, target, overwrite: true);
            onUpdated?.Invoke(target);
            hashedAssetCount++;

            map[$"/{relative}"] = $"/{hashedName}";
            map[relative] = hashedName;
            hashedAssets.Add(new WebOptimizeHashedAssetEntry
            {
                OriginalPath = "/" + relative,
                HashedPath = "/" + hashedName
            });
        }

        return map;
    }

    private static (int HtmlFilesRewritten, int CssFilesRewritten) RewriteHashedReferences(
        string siteRoot,
        string[] htmlFiles,
        Dictionary<string, string> map,
        IReadOnlySet<string> protectedStoryArtifacts,
        Action<string>? onUpdated = null)
    {
        var htmlFilesRewritten = 0;
        var cssFilesRewritten = 0;
        foreach (var htmlFile in htmlFiles)
        {
            var html = File.ReadAllText(htmlFile);
            if (string.IsNullOrWhiteSpace(html)) continue;
            var updated = RewriteReferences(html, map);
            if (!string.Equals(updated, html, StringComparison.Ordinal))
            {
                File.WriteAllText(htmlFile, updated);
                htmlFilesRewritten++;
                onUpdated?.Invoke(htmlFile);
            }
        }

        foreach (var cssFile in Directory.EnumerateFiles(siteRoot, "*.css", SearchOption.AllDirectories))
        {
            if (protectedStoryArtifacts.Contains(Path.GetFullPath(cssFile)))
                continue;
            var css = File.ReadAllText(cssFile);
            if (string.IsNullOrWhiteSpace(css)) continue;
            var updated = RewriteCssUrls(css, map);
            if (!string.Equals(updated, css, StringComparison.Ordinal))
            {
                File.WriteAllText(cssFile, updated);
                cssFilesRewritten++;
                onUpdated?.Invoke(cssFile);
            }
        }

        return (htmlFilesRewritten, cssFilesRewritten);
    }

    private static string RewriteReferences(string html, Dictionary<string, string> map)
    {
        var rewrittenHtml = HtmlAttrRegex.Replace(html, match =>
        {
            var url = match.Groups["url"].Value;
            var rewritten = RewriteUrlWithMap(url, map);
            return rewritten == url ? match.Value : $"{match.Groups["attr"].Value}=\"{rewritten}\"";
        });

        return HtmlSrcSetAttrRegex.Replace(rewrittenHtml, match =>
        {
            var value = match.Groups["value"].Value;
            var rewritten = RewriteSrcSetWithMap(value, map);
            if (string.Equals(rewritten, value, StringComparison.Ordinal))
                return match.Value;

            var quote = match.Groups["quote"].Value;
            return $"{match.Groups["attr"].Value}={quote}{rewritten}{quote}";
        });
    }

    private static string RewriteSrcSetWithMap(string srcSet, Dictionary<string, string> map)
    {
        StringBuilder? rewritten = null;
        var copyFrom = 0;
        var index = 0;
        while (index < srcSet.Length)
        {
            while (index < srcSet.Length && (char.IsWhiteSpace(srcSet[index]) || srcSet[index] == ','))
                index++;
            if (index >= srcSet.Length)
                break;

            var urlStart = index;
            while (index < srcSet.Length && !char.IsWhiteSpace(srcSet[index]))
                index++;

            var urlEnd = index;
            while (urlEnd > urlStart && srcSet[urlEnd - 1] == ',')
                urlEnd--;
            var candidateEndedWithComma = urlEnd < index;

            if (urlEnd > urlStart)
            {
                var url = srcSet[urlStart..urlEnd];
                var mapped = RewriteUrlWithMap(url, map);
                if (!string.Equals(mapped, url, StringComparison.Ordinal))
                {
                    rewritten ??= new StringBuilder(srcSet.Length + 32);
                    rewritten.Append(srcSet, copyFrom, urlStart - copyFrom);
                    rewritten.Append(mapped);
                    copyFrom = urlEnd;
                }
            }

            if (candidateEndedWithComma)
                continue;

            var parentheses = 0;
            while (index < srcSet.Length)
            {
                var current = srcSet[index++];
                if (current == '(')
                    parentheses++;
                else if (current == ')' && parentheses > 0)
                    parentheses--;
                else if (current == ',' && parentheses == 0)
                    break;
            }
        }

        if (rewritten is null)
            return srcSet;

        rewritten.Append(srcSet, copyFrom, srcSet.Length - copyFrom);
        return rewritten.ToString();
    }

    private static string RewriteCssUrls(string css, Dictionary<string, string> map)
    {
        return CssUrlRegex.Replace(css, match =>
        {
            var url = match.Groups["url"].Value;
            var rewritten = RewriteUrlWithMap(url, map);
            if (rewritten == url) return match.Value;
            return $"url({match.Groups["quote"].Value}{rewritten}{match.Groups["quote"].Value})";
        });
    }

    private static string RewriteUrlWithMap(string url, Dictionary<string, string> map)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase) || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return url;

        var split = url.Split('?', '#');
        var baseUrl = split[0];
        if (map.TryGetValue(baseUrl, out var mapped))
        {
            var suffix = url.Substring(baseUrl.Length);
            return mapped + suffix;
        }
        return url;
    }
}
