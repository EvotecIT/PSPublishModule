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
    private static readonly HttpClient RewriteDownloadClient = CreateRewriteDownloadClient();
    private static readonly Regex HtmlAttrRegex = new("(?<attr>href|src)=\"(?<url>[^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
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

        var allHtmlFiles = Directory.EnumerateFiles(siteRoot, "*.html", SearchOption.AllDirectories).ToArray();
        var htmlFiles = allHtmlFiles;
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
            CopyRewriteAssets(policy.Rewrites, siteRoot);
            foreach (var htmlFile in htmlFiles)
            {
                var content = File.ReadAllText(htmlFile);
                if (string.IsNullOrWhiteSpace(content)) continue;
                var updated = RewriteHtmlAssets(content, policy.Rewrites);
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
            OptimizeImages(siteRoot, htmlFiles, options, result, MarkUpdated);
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

    private static void CopyRewriteAssets(IEnumerable<AssetRewriteSpec> rewrites, string siteRoot)
    {
        foreach (var rewrite in rewrites)
        {
            if (string.IsNullOrWhiteSpace(rewrite.Destination))
                continue;

            var destRelative = rewrite.Destination.TrimStart('/', '\\');
            if (!TryResolveUnderRoot(siteRoot, destRelative, out var dest))
            {
                Trace.TraceWarning($"Asset rewrite destination outside site root: {rewrite.Destination}");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(rewrite.Source))
            {
                var source = Path.GetFullPath(rewrite.Source);
                if (!File.Exists(source))
                    continue;

                var destinationDirectory = Path.GetDirectoryName(dest);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                    Directory.CreateDirectory(destinationDirectory);
                File.Copy(source, dest, overwrite: true);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(rewrite.SourceUrl))
            {
                DownloadRewriteAsset(rewrite, dest);
            }
        }
    }

    private static void DownloadRewriteAsset(AssetRewriteSpec rewrite, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(rewrite.SourceUrl))
            return;

        if (!Uri.TryCreate(rewrite.SourceUrl, UriKind.Absolute, out var sourceUri) ||
            sourceUri.Scheme != Uri.UriSchemeHttps)
        {
            Trace.TraceWarning($"Asset rewrite sourceUrl is not a valid https URL: {rewrite.SourceUrl}");
            return;
        }

        if (!IsAllowedRewriteSourceUri(sourceUri, rewrite.SourceUrlAllowedHosts))
        {
            Trace.TraceWarning($"Asset rewrite sourceUrl host is not allowed for remote download: {sourceUri.Host}");
            return;
        }

        try
        {
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            if (destinationPath.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            {
                var css = DownloadText(sourceUri, rewrite.UserAgent);
                if (rewrite.DownloadDependencies)
                    css = RewriteDownloadedCssDependencies(css, sourceUri, destinationPath, rewrite.UserAgent);
                File.WriteAllText(destinationPath, css, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                return;
            }

            var bytes = DownloadBytes(sourceUri, rewrite.UserAgent);
            File.WriteAllBytes(destinationPath, bytes);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Asset rewrite download failed for {rewrite.SourceUrl}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string RewriteDownloadedCssDependencies(string css, Uri sourceUri, string destinationPath, string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(css))
            return css;

        var destinationDir = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(destinationDir))
            return css;

        var downloaded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return CssUrlRegex.Replace(css, match =>
        {
            var url = match.Groups["url"].Value.Trim();
            if (!TryResolveDownloadUri(sourceUri, url, out var resolvedUri))
                return match.Value;

            if (!downloaded.TryGetValue(resolvedUri.AbsoluteUri, out var fileName))
            {
                fileName = BuildDownloadedAssetFileName(resolvedUri);
                var localPath = Path.Combine(destinationDir, fileName);
                try
                {
                    var bytes = DownloadBytes(resolvedUri, userAgent);
                    File.WriteAllBytes(localPath, bytes);
                    downloaded[resolvedUri.AbsoluteUri] = fileName;
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"Asset rewrite dependency download failed for {resolvedUri}: {ex.GetType().Name}: {ex.Message}");
                    return match.Value;
                }
            }

            var quote = match.Groups["quote"].Value;
            return $"url({quote}{fileName}{quote})";
        });
    }

    private static bool TryResolveDownloadUri(Uri baseUri, string rawUrl, out Uri resolvedUri)
    {
        resolvedUri = baseUri;
        if (string.IsNullOrWhiteSpace(rawUrl) ||
            rawUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            rawUrl.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            rawUrl.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
            rawUrl.StartsWith("#", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var absoluteUri))
        {
            resolvedUri = absoluteUri;
            return resolvedUri.Scheme == Uri.UriSchemeHttp || resolvedUri.Scheme == Uri.UriSchemeHttps;
        }

        if (rawUrl.StartsWith("//", StringComparison.Ordinal))
        {
            if (Uri.TryCreate($"{baseUri.Scheme}:{rawUrl}", UriKind.Absolute, out var protocolRelativeUri))
            {
                resolvedUri = protocolRelativeUri;
                return true;
            }

            return false;
        }

        if (!Uri.TryCreate(baseUri, rawUrl, out var relativeUri))
            return false;

        resolvedUri = relativeUri;
        return resolvedUri.Scheme == Uri.UriSchemeHttp || resolvedUri.Scheme == Uri.UriSchemeHttps;
    }

    private static string BuildDownloadedAssetFileName(Uri uri)
    {
        var pathName = Path.GetFileName(uri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(pathName))
            pathName = "asset";

        foreach (var invalid in Path.GetInvalidFileNameChars())
            pathName = pathName.Replace(invalid, '-');

        var extension = Path.GetExtension(pathName);
        var stem = pathName[..Math.Max(0, pathName.Length - extension.Length)];
        if (string.IsNullOrWhiteSpace(stem))
            stem = "asset";

        var hash = ComputeShortHash(Encoding.UTF8.GetBytes(uri.AbsoluteUri));
        return $"{stem}.{hash}{extension}";
    }

    private static string DownloadText(Uri uri, string? userAgent)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var safeUserAgent = NormalizeHeaderSingleLine(userAgent);
        if (!string.IsNullOrWhiteSpace(safeUserAgent))
            request.Headers.TryAddWithoutValidation("User-Agent", safeUserAgent);

        using var response = RewriteDownloadClient.Send(request);
        response.EnsureSuccessStatusCode();
        using var stream = response.Content.ReadAsStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static byte[] DownloadBytes(Uri uri, string? userAgent)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var safeUserAgent = NormalizeHeaderSingleLine(userAgent);
        if (!string.IsNullOrWhiteSpace(safeUserAgent))
            request.Headers.TryAddWithoutValidation("User-Agent", safeUserAgent);

        using var response = RewriteDownloadClient.Send(request);
        response.EnsureSuccessStatusCode();
        using var stream = response.Content.ReadAsStream();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    internal static bool IsAllowedRewriteSourceUriForTesting(Uri uri, string[]? allowedHosts = null)
    {
        return IsAllowedRewriteSourceUri(uri, allowedHosts);
    }

    internal static string NormalizeHeaderSingleLineForTesting(string? value)
    {
        return NormalizeHeaderSingleLine(value);
    }

    private static string NormalizeHeaderSingleLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var firstLine = value.Split(new[] { '\r', '\n' }, StringSplitOptions.None)[0].Trim();
        var builder = new StringBuilder(Math.Min(firstLine.Length, 512));
        foreach (var character in firstLine)
        {
            if (character is >= ' ' and <= '~')
                builder.Append(character);

            if (builder.Length >= 512)
                break;
        }

        return builder.ToString().Trim();
    }

    private static bool IsAllowedRewriteSourceUri(Uri uri, string[]? allowedHosts)
    {
        if (uri is null ||
            uri.Scheme != Uri.UriSchemeHttps ||
            IsUnsafeRemoteHost(uri.Host))
        {
            return false;
        }

        var configuredHosts = allowedHosts?
            .Select(static host => host?.Trim().TrimEnd('.').ToLowerInvariant() ?? string.Empty)
            .Where(static host => host.Length > 0)
            .ToArray() ?? Array.Empty<string>();
        if (configuredHosts.Length == 0)
            return false;

        var sourceHost = uri.Host.TrimEnd('.').ToLowerInvariant();
        foreach (var allowedHost in configuredHosts)
        {
            if (string.Equals(allowedHost, "*", StringComparison.Ordinal))
                return true;

            if (string.Equals(sourceHost, allowedHost, StringComparison.OrdinalIgnoreCase))
                return true;

            if (allowedHost.StartsWith("*.", StringComparison.Ordinal) &&
                sourceHost.EndsWith(allowedHost[1..], StringComparison.OrdinalIgnoreCase) &&
                sourceHost.Length > allowedHost.Length - 1)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnsafeRemoteHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return true;

        var normalized = host.Trim().TrimEnd('.');
        if (normalized.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(normalized, out var address) && IsUnsafeRemoteAddress(address);
    }

    private static bool IsUnsafeRemoteAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10 ||
                   (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   (bytes[0] == 169 && bytes[1] == 254) ||
                   bytes[0] == 0;
        }

        return address.IsIPv6LinkLocal ||
               address.IsIPv6SiteLocal ||
               address.IsIPv6Multicast ||
               address.Equals(IPAddress.IPv6None) ||
               address.Equals(IPAddress.IPv6Any);
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
}
