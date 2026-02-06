using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlTinkerX;
using ImageMagick;

namespace PowerForge.Web;

/// <summary>Options for asset optimization.</summary>
public sealed class WebAssetOptimizerOptions
{
    /// <summary>Root directory of the generated site.</summary>
    public string SiteRoot { get; set; } = ".";
    /// <summary>Optional critical CSS file path.</summary>
    public string? CriticalCssPath { get; set; }
    /// <summary>Regex pattern used to match stylesheet links.</summary>
    public string CssLinkPattern { get; set; } = "(app|api-docs)\\.css";
    /// <summary>When true, minify HTML files.</summary>
    public bool MinifyHtml { get; set; } = false;
    /// <summary>When true, minify CSS files.</summary>
    public bool MinifyCss { get; set; } = false;
    /// <summary>When true, minify JavaScript files.</summary>
    public bool MinifyJs { get; set; } = false;
    /// <summary>When true, optimize image files.</summary>
    public bool OptimizeImages { get; set; } = false;
    /// <summary>File extensions considered for image optimization.</summary>
    public string[] ImageExtensions { get; set; } = new[] { ".png", ".jpg", ".jpeg", ".webp", ".gif" };
    /// <summary>Glob-style include patterns for image optimization.</summary>
    public string[] ImageInclude { get; set; } = Array.Empty<string>();
    /// <summary>Glob-style exclude patterns for image optimization.</summary>
    public string[] ImageExclude { get; set; } = Array.Empty<string>();
    /// <summary>Image quality target in range 1-100.</summary>
    public int ImageQuality { get; set; } = 82;
    /// <summary>When true, strip metadata from optimized images.</summary>
    public bool ImageStripMetadata { get; set; } = true;
    /// <summary>Enable asset hashing (fingerprinting).</summary>
    public bool HashAssets { get; set; }
    /// <summary>File extensions to hash.</summary>
    public string[] HashExtensions { get; set; } = new[] { ".css", ".js" };
    /// <summary>Glob-style exclude patterns for hashing.</summary>
    public string[] HashExclude { get; set; } = Array.Empty<string>();
    /// <summary>Optional manifest path for hashed assets.</summary>
    public string? HashManifestPath { get; set; }
    /// <summary>Optional optimization report output path (relative to site root).</summary>
    public string? ReportPath { get; set; }
    /// <summary>Optional asset policy for rewrites and headers.</summary>
    public AssetPolicySpec? AssetPolicy { get; set; }
}

/// <summary>Optimizes generated site assets (critical CSS, minification).</summary>
public static class WebAssetOptimizer
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex HtmlAttrRegex = new("(?<attr>href|src)=\"(?<url>[^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex CssUrlRegex = new("url\\((?<quote>['\"]?)(?<url>[^'\")]+)\\k<quote>\\)", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex StylesheetLinkRegex = new("<link\\s+rel=\"stylesheet\"\\s+href=\"([^\"]+)\"\\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
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

        var htmlFiles = Directory.EnumerateFiles(siteRoot, "*.html", SearchOption.AllDirectories).ToArray();
        var cssFiles = Directory.EnumerateFiles(siteRoot, "*.css", SearchOption.AllDirectories).ToArray();
        var jsFiles = Directory.EnumerateFiles(siteRoot, "*.js", SearchOption.AllDirectories).ToArray();
        result.HtmlFileCount = htmlFiles.Length;
        result.CssFileCount = cssFiles.Length;
        result.JsFileCount = jsFiles.Length;
        var policy = options.AssetPolicy;
        if (policy?.Rewrites is { Length: > 0 })
        {
            CopyRewriteAssets(policy.Rewrites, siteRoot);
            foreach (var htmlFile in htmlFiles)
            {
                var content = File.ReadAllText(htmlFile);
                if (string.IsNullOrWhiteSpace(content)) continue;
                var updated = RewriteHtmlAssets(content, policy.Rewrites);
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
                var updated = InlineCriticalCss(content, criticalCss, cssPattern);
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
            OptimizeImages(siteRoot, options, result, MarkUpdated);
        }

        var hashSpec = ResolveHashSpec(options, policy);
        Dictionary<string, string>? hashMap = null;
        if (hashSpec.Enabled)
        {
            hashMap = HashAssets(siteRoot, hashSpec, out var hashedAssetCount, out var hashedAssets, MarkUpdated);
            result.HashedAssetCount = hashedAssetCount;
            result.HashedAssets = hashedAssets.ToArray();
            if (hashMap.Count > 0)
            {
                var rewrites = RewriteHashedReferences(siteRoot, htmlFiles, hashMap, MarkUpdated);
                result.HtmlHashRewriteCount = rewrites.HtmlFilesRewritten;
                result.CssHashRewriteCount = rewrites.CssFilesRewritten;
                var manifestPath = WriteHashManifest(siteRoot, hashSpec, hashMap);
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
            foreach (var cssFile in Directory.EnumerateFiles(siteRoot, "*.css", SearchOption.AllDirectories))
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
            foreach (var jsFile in Directory.EnumerateFiles(siteRoot, "*.js", SearchOption.AllDirectories))
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
            var headersPath = WriteCacheHeaders(siteRoot, policy.CacheHeaders, hashMap);
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

        if (!string.IsNullOrWhiteSpace(options.ReportPath))
        {
            if (TryResolveUnderRoot(siteRoot, options.ReportPath.TrimStart('/', '\\'), out var reportPath))
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
            else
            {
                Trace.TraceWarning($"Optimize report path outside site root: {options.ReportPath}");
            }
        }

        return result;
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

    private static void CopyRewriteAssets(IEnumerable<AssetRewriteSpec> rewrites, string siteRoot)
    {
        foreach (var rewrite in rewrites)
        {
            if (string.IsNullOrWhiteSpace(rewrite.Source) || string.IsNullOrWhiteSpace(rewrite.Destination))
                continue;

            var source = Path.GetFullPath(rewrite.Source);
            if (!File.Exists(source)) continue;

            var destRelative = rewrite.Destination.TrimStart('/', '\\');
            if (!TryResolveUnderRoot(siteRoot, destRelative, out var dest))
            {
                Trace.TraceWarning($"Asset rewrite destination outside site root: {rewrite.Destination}");
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(source, dest, overwrite: true);
        }
    }

    private static string RewriteHtmlAssets(string html, AssetRewriteSpec[] rewrites)
    {
        if (rewrites.Length == 0) return html;
        return HtmlAttrRegex.Replace(html, match =>
        {
            var url = match.Groups["url"].Value;
            var replaced = ApplyRewriteRules(url, rewrites);
            return replaced == url ? match.Value : $"{match.Groups["attr"].Value}=\"{replaced}\"";
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
            var relative = Path.GetRelativePath(siteRoot, file).Replace('\\', '/');
            if (IsExcluded(relative, spec.Exclude))
                continue;

            var hash = ComputeShortHash(File.ReadAllBytes(file));
            var ext = Path.GetExtension(relative);
            if (relative.Length <= ext.Length) continue;
            var without = relative.Substring(0, relative.Length - ext.Length);
            var hashedName = $"{without}.{hash}{ext}";
            var target = Path.Combine(siteRoot, hashedName.Replace('/', Path.DirectorySeparatorChar));
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
        return HtmlAttrRegex.Replace(html, match =>
        {
            var url = match.Groups["url"].Value;
            var rewritten = RewriteUrlWithMap(url, map);
            return rewritten == url ? match.Value : $"{match.Groups["attr"].Value}=\"{rewritten}\"";
        });
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

    private static void OptimizeImages(
        string siteRoot,
        WebAssetOptimizerOptions options,
        WebOptimizeResult result,
        Action<string>? onUpdated = null)
    {
        var extensionSet = NormalizeExtensions(options.ImageExtensions, new[] { ".png", ".jpg", ".jpeg", ".webp" });
        var allImageFiles = Directory.EnumerateFiles(siteRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => extensionSet.Contains(Path.GetExtension(path)))
            .ToList();
        var optimizedImages = new List<WebOptimizeImageEntry>();
        var quality = Math.Clamp(options.ImageQuality, 1, 100);

        foreach (var file in allImageFiles)
        {
            var relative = ToRelative(siteRoot, file);
            if (options.ImageInclude is { Length: > 0 } && !IsIncluded(relative, options.ImageInclude))
                continue;
            if (IsExcluded(relative, options.ImageExclude))
                continue;

            result.ImageFileCount++;
            var originalBytes = new FileInfo(file).Length;
            var finalBytes = originalBytes;
            result.ImageBytesBefore += originalBytes;

            try
            {
                using var image = new MagickImage(file);
                if (options.ImageStripMetadata)
                    image.Strip();
                if (SupportsQualitySetting(image.Format))
                    image.Quality = (uint)quality;

                using var stream = new MemoryStream();
                image.Write(stream);
                var optimizedBytes = stream.Length;
                if (optimizedBytes > 0 && optimizedBytes < originalBytes)
                {
                    File.WriteAllBytes(file, stream.ToArray());
                    finalBytes = optimizedBytes;
                    var savedBytes = originalBytes - optimizedBytes;
                    result.ImageOptimizedCount++;
                    result.ImageBytesSaved += savedBytes;
                    optimizedImages.Add(new WebOptimizeImageEntry
                    {
                        Path = relative,
                        BytesBefore = originalBytes,
                        BytesAfter = optimizedBytes,
                        BytesSaved = savedBytes
                    });
                    onUpdated?.Invoke(file);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Image optimize failed for {file}: {ex.GetType().Name}: {ex.Message}");
            }

            result.ImageBytesAfter += finalBytes;
        }

        result.OptimizedImages = optimizedImages
            .OrderByDescending(entry => entry.BytesSaved)
            .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static HashSet<string> NormalizeExtensions(string[]? extensions, string[] defaults)
    {
        var source = (extensions?.Length ?? 0) == 0 ? defaults : extensions!;
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ext in source)
        {
            if (string.IsNullOrWhiteSpace(ext))
                continue;
            var normalized = ext.Trim();
            if (!normalized.StartsWith(".", StringComparison.Ordinal))
                normalized = "." + normalized;
            set.Add(normalized);
        }
        return set;
    }

    private static bool SupportsQualitySetting(MagickFormat format)
    {
        var name = format.ToString();
        return name.Contains("Jpeg", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Jpg", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("WebP", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Heic", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Heif", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Avif", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIncluded(string relativePath, string[] patterns)
    {
        if (patterns is null || patterns.Length == 0) return true;
        var normalized = relativePath.Replace('\\', '/');
        var withLeadingSlash = "/" + normalized;
        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern)) continue;
            var normalizedPattern = pattern.Replace('\\', '/');
            if (GlobMatch(normalizedPattern, normalized) || GlobMatch(normalizedPattern, withLeadingSlash))
                return true;
        }
        return false;
    }

    private static bool IsExcluded(string relativePath, string[] patterns)
    {
        if (patterns is null || patterns.Length == 0) return false;
        var normalized = relativePath.Replace('\\', '/');
        var withLeadingSlash = "/" + normalized;
        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern)) continue;
            var normalizedPattern = pattern.Replace('\\', '/');
            if (GlobMatch(normalizedPattern, normalized) || GlobMatch(normalizedPattern, withLeadingSlash))
                return true;
        }
        return false;
    }

    private static string ComputeShortHash(byte[] bytes)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).Substring(0, 8).ToLowerInvariant();
    }

    private static string? WriteHashManifest(string siteRoot, AssetHashSpec spec, Dictionary<string, string> map)
    {
        if (map.Count == 0) return null;
        var manifestRelative = string.IsNullOrWhiteSpace(spec.ManifestPath)
            ? "asset-manifest.json"
            : spec.ManifestPath.TrimStart('/', '\\');
        if (!TryResolveUnderRoot(siteRoot, manifestRelative, out var path))
        {
            Trace.TraceWarning($"Hash manifest path outside site root: {spec.ManifestPath}");
            return null;
        }
        var json = System.Text.Json.JsonSerializer.Serialize(map, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(path, json);
        return path;
    }

    private static string? WriteCacheHeaders(string siteRoot, CacheHeadersSpec headers, Dictionary<string, string>? map)
    {
        var output = string.IsNullOrWhiteSpace(headers.OutputPath) ? "_headers" : headers.OutputPath;
        if (!TryResolveUnderRoot(siteRoot, output.TrimStart('/', '\\'), out var outputPath))
        {
            Trace.TraceWarning($"Cache headers output path outside site root: {headers.OutputPath}");
            return null;
        }
        var htmlCache = string.IsNullOrWhiteSpace(headers.HtmlCacheControl)
            ? "public, max-age=0, must-revalidate"
            : headers.HtmlCacheControl!;
        var assetCache = string.IsNullOrWhiteSpace(headers.ImmutableCacheControl)
            ? "public, max-age=31536000, immutable"
            : headers.ImmutableCacheControl!;

        var immutablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (headers.ImmutablePaths is { Length: > 0 })
        {
            foreach (var path in headers.ImmutablePaths)
                immutablePaths.Add(path);
        }
        else if (map is not null)
        {
            foreach (var path in map.Values)
            {
                var segments = path.TrimStart('/').Split('/');
                if (segments.Length > 1)
                    immutablePaths.Add($"/{segments[0]}/*");
            }
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("/*");
        sb.AppendLine($"  Cache-Control: {htmlCache}");
        sb.AppendLine();
        foreach (var path in immutablePaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine(path);
            sb.AppendLine($"  Cache-Control: {assetCache}");
            sb.AppendLine();
        }

        File.WriteAllText(outputPath, sb.ToString().TrimEnd() + Environment.NewLine);
        return outputPath;
    }

    private static bool GlobMatch(string pattern, string value)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        if (pattern == "*") return true;
        var regex = "^" + Regex.Escape(pattern)
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*")
            .Replace("\\?", ".") + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
    }

    private static string InlineCriticalCss(string content, string criticalCss, Regex cssPattern)
    {
        var match = StylesheetLinkRegex.Match(content);
        if (!match.Success) return content;

        var href = match.Groups[1].Value;
        if (!cssPattern.IsMatch(href)) return content;

        var asyncCss = $"<!-- critical-css -->\n<style>{criticalCss}</style>\n<link rel=\"preload\" href=\"{href}\" as=\"style\" onload=\"this.onload=null;this.rel='stylesheet'\">\n<noscript><link rel=\"stylesheet\" href=\"{href}\"></noscript>";
        return content.Replace(match.Value, asyncCss);
    }

    private static string LoadCriticalCss(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) return string.Empty;
        var css = File.ReadAllText(full);
        try
        {
            var optimized = HtmlOptimizer.OptimizeCss(css);
            return string.IsNullOrWhiteSpace(optimized) ? css : optimized;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Critical CSS optimize failed for {full}: {ex.GetType().Name}: {ex.Message}");
            return css;
        }
    }

    private static bool TryResolveUnderRoot(string siteRoot, string relativePath, out string resolved)
    {
        resolved = string.Empty;
        if (string.IsNullOrWhiteSpace(siteRoot) || string.IsNullOrWhiteSpace(relativePath))
            return false;

        var rootFull = Path.GetFullPath(siteRoot);
        var combined = Path.GetFullPath(Path.Combine(rootFull, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsUnderRoot(combined, rootFull))
            return false;

        resolved = combined;
        return true;
    }

    private static bool IsUnderRoot(string path, string root)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(path, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return true;
        normalizedRoot += Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string ToRelative(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            return fullPath;
        return Path.GetRelativePath(fullRoot, fullPath).Replace('\\', '/');
    }

}
