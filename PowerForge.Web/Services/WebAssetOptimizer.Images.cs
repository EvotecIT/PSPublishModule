using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlTinkerX;
using ImageMagick;

namespace PowerForge.Web;

/// <summary>Optimizes generated site assets (critical CSS, minification).</summary>
public static partial class WebAssetOptimizer
{
    private static void OptimizeImages(
        string siteRoot,
        string[] htmlFiles,
        WebAssetOptimizerOptions options,
        WebOptimizeResult result,
        Action<string>? onUpdated = null)
    {
        var extensionSet = NormalizeExtensions(options.ImageExtensions, new[] { ".png", ".jpg", ".jpeg", ".webp" });
        var quality = Math.Clamp(options.ImageQuality, 1, 100);
        var responsiveWidths = (options.ResponsiveImageWidths ?? Array.Empty<int>())
            .Where(width => width > 0)
            .Distinct()
            .OrderBy(width => width)
            .ToArray();
        var optimizedImages = new List<WebOptimizeImageEntry>();
        var failures = new List<WebOptimizeImageFailureEntry>();
        var generatedVariants = new List<WebOptimizeImageVariantEntry>();
        var budgetWarnings = new List<string>();
        var rewritePlans = new Dictionary<string, ImageRewritePlan>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(siteRoot, "*.*", SearchOption.AllDirectories)
                     .Where(path => extensionSet.Contains(Path.GetExtension(path))))
        {
            var relative = ToRelative(siteRoot, file);
            if (options.ImageInclude is { Length: > 0 } && !IsIncluded(relative, options.ImageInclude))
                continue;
            if (IsExcluded(relative, options.ImageExclude))
                continue;

            result.ImageFileCount++;
            var originalBytes = new FileInfo(file).Length;
            var finalBytes = originalBytes;
            var imageWidth = 0;
            var imageHeight = 0;
            result.ImageBytesBefore += originalBytes;

            try
            {
                using var image = new MagickImage(file);
                imageWidth = (int)image.Width;
                imageHeight = (int)image.Height;
                if (options.ImageStripMetadata)
                    image.Strip();
                if (SupportsQualitySetting(image.Format))
                    image.Quality = (uint)quality;

                using var optimizedStream = new MemoryStream();
                image.Write(optimizedStream);
                var optimizedBytes = optimizedStream.Length;
                if (optimizedBytes > 0 && optimizedBytes < originalBytes)
                {
                    optimizedStream.Position = 0;
                    using var fileStream = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None);
                    optimizedStream.CopyTo(fileStream);
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

                var plan = new ImageRewritePlan
                {
                    SourceRelativePath = relative,
                    PreferredRelativePath = relative,
                    OriginalWidth = imageWidth,
                    OriginalHeight = imageHeight
                };
                var preferredBytes = finalBytes;

                if (options.ImageGenerateWebp && TryEncodeVariant(image, null, MagickFormat.WebP, quality, out var webpBytes))
                {
                    if (webpBytes.LongLength > 0 && webpBytes.LongLength < finalBytes &&
                        TryWriteVariant(siteRoot, relative, null, "webp", webpBytes, out var webpRelative, onUpdated))
                    {
                        generatedVariants.Add(new WebOptimizeImageVariantEntry
                        {
                            SourcePath = relative,
                            VariantPath = webpRelative,
                            Format = "webp",
                            Width = null,
                            Bytes = webpBytes.LongLength
                        });
                        if (options.ImagePreferNextGen && webpBytes.LongLength < preferredBytes)
                        {
                            preferredBytes = webpBytes.LongLength;
                            plan.PreferredRelativePath = webpRelative;
                        }
                    }
                }

                if (options.ImageGenerateAvif && TryEncodeVariant(image, null, MagickFormat.Avif, quality, out var avifBytes))
                {
                    if (avifBytes.LongLength > 0 && avifBytes.LongLength < finalBytes &&
                        TryWriteVariant(siteRoot, relative, null, "avif", avifBytes, out var avifRelative, onUpdated))
                    {
                        generatedVariants.Add(new WebOptimizeImageVariantEntry
                        {
                            SourcePath = relative,
                            VariantPath = avifRelative,
                            Format = "avif",
                            Width = null,
                            Bytes = avifBytes.LongLength
                        });
                        if (options.ImagePreferNextGen && avifBytes.LongLength < preferredBytes)
                        {
                            preferredBytes = avifBytes.LongLength;
                            plan.PreferredRelativePath = avifRelative;
                        }
                    }
                }

                if (responsiveWidths.Length > 0 && imageWidth > 0)
                {
                    var responsiveExtension = Path.GetExtension(plan.PreferredRelativePath).Trim('.').ToLowerInvariant();
                    var responsiveFormat = ResolveMagickFormatForExtension(responsiveExtension);
                    foreach (var width in responsiveWidths)
                    {
                        if (width >= imageWidth)
                            continue;
                        if (!TryEncodeVariant(image, width, responsiveFormat, quality, out var responsiveBytes))
                            continue;
                        if (responsiveBytes.LongLength <= 0 || responsiveBytes.LongLength >= preferredBytes)
                            continue;
                        if (!TryWriteVariant(siteRoot, plan.PreferredRelativePath, width, responsiveExtension, responsiveBytes, out var variantRelative, onUpdated))
                            continue;

                        plan.ResponsiveVariants.Add(new ImageVariantPlan
                        {
                            RelativePath = variantRelative,
                            Width = width
                        });
                        generatedVariants.Add(new WebOptimizeImageVariantEntry
                        {
                            SourcePath = relative,
                            VariantPath = variantRelative,
                            Format = responsiveExtension,
                            Width = width,
                            Bytes = responsiveBytes.LongLength
                        });
                    }
                }

                if (options.ImagePreferNextGen || options.EnhanceImageTags || plan.ResponsiveVariants.Count > 0)
                    rewritePlans[relative] = plan;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Image optimize failed for {file}: {ex.GetType().Name}: {ex.Message}");
                result.ImageFailedCount++;
                failures.Add(new WebOptimizeImageFailureEntry
                {
                    Path = relative,
                    Error = $"{ex.GetType().Name}: {ex.Message}"
                });
            }

            result.ImageBytesAfter += finalBytes;
            if (options.ImageMaxBytesPerFile > 0 && finalBytes > options.ImageMaxBytesPerFile)
                budgetWarnings.Add($"Image '{relative}' exceeds max-bytes-per-file ({finalBytes} > {options.ImageMaxBytesPerFile}).");
        }

        if (options.ImageMaxTotalBytes > 0 && result.ImageBytesAfter > options.ImageMaxTotalBytes)
            budgetWarnings.Add($"Total image bytes exceed max-total-bytes ({result.ImageBytesAfter} > {options.ImageMaxTotalBytes}).");

        if (rewritePlans.Count > 0)
            RewriteHtmlImageTags(siteRoot, htmlFiles, rewritePlans, options, result, onUpdated);

        result.OptimizedImages = optimizedImages
            .OrderByDescending(entry => entry.BytesSaved)
            .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        result.ImageFailures = failures
            .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        result.GeneratedImageVariants = generatedVariants
            .OrderBy(entry => entry.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Width ?? int.MaxValue)
            .ThenBy(entry => entry.VariantPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        result.ImageVariantCount = result.GeneratedImageVariants.Length;
        result.ImageBudgetWarnings = budgetWarnings.ToArray();
        result.ImageBudgetExceeded = budgetWarnings.Count > 0;
    }

    private static void RewriteHtmlImageTags(
        string siteRoot,
        string[] htmlFiles,
        Dictionary<string, ImageRewritePlan> rewritePlans,
        WebAssetOptimizerOptions options,
        WebOptimizeResult result,
        Action<string>? onUpdated)
    {
        foreach (var htmlFile in htmlFiles)
        {
            var html = File.ReadAllText(htmlFile);
            if (string.IsNullOrWhiteSpace(html))
                continue;

            var fileChanged = false;
            var hintedInFile = 0;
            var rewritten = ImgTagRegex.Replace(html, match =>
            {
                var attrs = match.Groups["attrs"].Value;
                var srcMatch = ImgSrcAttrRegex.Match(attrs);
                if (!srcMatch.Success)
                    return match.Value;

                var srcValue = srcMatch.Groups["value"].Value;
                if (!TryResolveImageReference(siteRoot, htmlFile, srcValue, out var resolvedRelative))
                    return match.Value;
                if (!rewritePlans.TryGetValue(resolvedRelative, out var plan))
                    return match.Value;

                var changed = false;
                var attrsUpdated = attrs;
                var preferredSrc = BuildUrlForReference(siteRoot, htmlFile, srcValue, plan.PreferredRelativePath);

                if (options.ImagePreferNextGen &&
                    !string.Equals(plan.PreferredRelativePath, resolvedRelative, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(preferredSrc, srcValue, StringComparison.Ordinal))
                {
                    attrsUpdated = ImgSrcAttrRegex.Replace(attrsUpdated, m => $"src={m.Groups["quote"].Value}{preferredSrc}{m.Groups["quote"].Value}", 1);
                    changed = true;
                }

                if (plan.ResponsiveVariants.Count > 0 && !ImgSrcSetAttrRegex.IsMatch(attrsUpdated))
                {
                    var srcsetEntries = plan.ResponsiveVariants
                        .OrderBy(variant => variant.Width ?? int.MaxValue)
                        .Select(variant =>
                        {
                            var url = BuildUrlForReference(siteRoot, htmlFile, srcValue, variant.RelativePath);
                            return $"{url} {variant.Width}w";
                        })
                        .ToList();

                    if (plan.OriginalWidth > 0)
                    {
                        var baseSrc = BuildUrlForReference(siteRoot, htmlFile, srcValue, plan.PreferredRelativePath);
                        srcsetEntries.Add($"{baseSrc} {plan.OriginalWidth}w");
                    }

                    if (srcsetEntries.Count > 0)
                    {
                        attrsUpdated += $" srcset=\"{string.Join(", ", srcsetEntries)}\"";
                        if (!ImgSizesAttrRegex.IsMatch(attrsUpdated))
                            attrsUpdated += " sizes=\"100vw\"";
                        changed = true;
                    }
                }

                if (options.EnhanceImageTags)
                {
                    if (plan.OriginalWidth > 0 && !ImgWidthAttrRegex.IsMatch(attrsUpdated))
                    {
                        attrsUpdated += $" width=\"{plan.OriginalWidth}\"";
                        hintedInFile++;
                        changed = true;
                    }
                    if (plan.OriginalHeight > 0 && !ImgHeightAttrRegex.IsMatch(attrsUpdated))
                    {
                        attrsUpdated += $" height=\"{plan.OriginalHeight}\"";
                        hintedInFile++;
                        changed = true;
                    }
                    if (!ImgLoadingAttrRegex.IsMatch(attrsUpdated))
                    {
                        attrsUpdated += " loading=\"lazy\"";
                        hintedInFile++;
                        changed = true;
                    }
                    if (!ImgDecodingAttrRegex.IsMatch(attrsUpdated))
                    {
                        attrsUpdated += " decoding=\"async\"";
                        hintedInFile++;
                        changed = true;
                    }
                }

                if (!changed)
                    return match.Value;

                fileChanged = true;
                return $"<img{attrsUpdated}>";
            });

            if (!fileChanged || string.Equals(rewritten, html, StringComparison.Ordinal))
                continue;

            File.WriteAllText(htmlFile, rewritten);
            result.ImageHtmlRewriteCount++;
            result.ImageHintedCount += hintedInFile;
            onUpdated?.Invoke(htmlFile);
        }
    }

    private static bool TryResolveImageReference(string siteRoot, string htmlFile, string sourceUrl, out string relativePath)
    {
        relativePath = string.Empty;
        if (string.IsNullOrWhiteSpace(sourceUrl))
            return false;

        var baseUrl = SplitUrlPath(sourceUrl, out _);
        if (string.IsNullOrWhiteSpace(baseUrl))
            return false;
        if (baseUrl.StartsWith("#", StringComparison.Ordinal) ||
            baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            baseUrl.StartsWith("//", StringComparison.OrdinalIgnoreCase) ||
            baseUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            baseUrl.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            return false;

        if (baseUrl.StartsWith("/", StringComparison.Ordinal))
        {
            relativePath = baseUrl.TrimStart('/').Replace('\\', '/');
            return !string.IsNullOrWhiteSpace(relativePath);
        }

        var htmlDir = Path.GetDirectoryName(htmlFile) ?? siteRoot;
        var candidate = Path.GetFullPath(Path.Combine(htmlDir, baseUrl.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsUnderRoot(candidate, siteRoot))
            return false;

        relativePath = ToRelative(siteRoot, candidate);
        return !string.IsNullOrWhiteSpace(relativePath);
    }

    private static string BuildUrlForReference(string siteRoot, string htmlFile, string originalUrl, string targetRelativePath)
    {
        var baseUrl = SplitUrlPath(originalUrl, out var suffix);
        var targetRelative = targetRelativePath.Replace('\\', '/');
        string rewritten;
        if (baseUrl.StartsWith("/", StringComparison.Ordinal))
        {
            rewritten = "/" + targetRelative;
        }
        else
        {
            var htmlDir = Path.GetDirectoryName(htmlFile) ?? siteRoot;
            var targetFull = Path.GetFullPath(Path.Combine(siteRoot, targetRelative.Replace('/', Path.DirectorySeparatorChar)));
            var relative = Path.GetRelativePath(htmlDir, targetFull).Replace('\\', '/');
            rewritten = string.IsNullOrWhiteSpace(relative) ? "./" : relative;
        }

        return rewritten + suffix;
    }

    private static string SplitUrlPath(string url, out string suffix)
    {
        suffix = string.Empty;
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        var q = url.IndexOf('?');
        var h = url.IndexOf('#');
        var splitIndex = q >= 0 && h >= 0 ? Math.Min(q, h) : Math.Max(q, h);
        if (splitIndex < 0)
            return url;
        suffix = url.Substring(splitIndex);
        return url.Substring(0, splitIndex);
    }

    private static bool TryEncodeVariant(MagickImage sourceImage, int? width, MagickFormat format, int quality, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (sourceImage is null)
            return false;

        try
        {
            using var variant = sourceImage.Clone();
            if (width.HasValue && width.Value > 0 && width.Value < variant.Width)
                variant.Resize((uint)width.Value, 0);
            if (SupportsQualitySetting(format))
                variant.Quality = (uint)quality;
            variant.Format = format;
            using var stream = new MemoryStream();
            variant.Write(stream);
            bytes = stream.ToArray();
            return bytes.Length > 0;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Image variant encode failed ({format}, width {width}): {ex.GetType().Name}: {ex.Message}");
            bytes = Array.Empty<byte>();
            return false;
        }
    }

    private static bool TryWriteVariant(
        string siteRoot,
        string sourceRelativePath,
        int? width,
        string formatExtension,
        byte[] bytes,
        out string variantRelativePath,
        Action<string>? onUpdated = null)
    {
        variantRelativePath = string.Empty;
        if (bytes is null || bytes.Length == 0)
            return false;

        var ext = formatExtension.Trim().TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(ext))
            return false;

        var sourceExt = Path.GetExtension(sourceRelativePath).TrimStart('.').ToLowerInvariant();
        if (!width.HasValue && string.Equals(sourceExt, ext, StringComparison.OrdinalIgnoreCase))
            return false;

        var relativeNoExt = Path.Combine(
            Path.GetDirectoryName(sourceRelativePath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(sourceRelativePath))
            .Replace('\\', '/');

        var variantName = width.HasValue
            ? $"{relativeNoExt}.w{width.Value}.{ext}"
            : $"{relativeNoExt}.{ext}";

        var variantPath = Path.Combine(siteRoot, variantName.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(variantPath)!);
        File.WriteAllBytes(variantPath, bytes);
        onUpdated?.Invoke(variantPath);
        variantRelativePath = ToRelative(siteRoot, variantPath);
        return true;
    }

    private static MagickFormat ResolveMagickFormatForExtension(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            "jpg" => MagickFormat.Jpeg,
            "jpeg" => MagickFormat.Jpeg,
            "png" => MagickFormat.Png,
            "gif" => MagickFormat.Gif,
            "avif" => MagickFormat.Avif,
            "webp" => MagickFormat.WebP,
            _ => MagickFormat.WebP
        };
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
        return format is MagickFormat.Jpeg or MagickFormat.Jpg or MagickFormat.WebP or MagickFormat.Heic or MagickFormat.Heif or MagickFormat.Avif;
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
