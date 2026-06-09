using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static readonly Regex WordPressAbsoluteMediaUrlRegex = new(
        @"(?<url>(?:https?:)?\/\/(?:www\.)?(?:evotec\.xyz|evotec\.pl)\/wp-content\/uploads\/[^\s""'<>)]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WordPressLocalMediaUrlRegex = new(
        @"(?<![A-Za-z0-9])(?<route>/wp-content/uploads/[^\s""'<>)\]]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static void ExecuteWordPressMediaSync(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root")) ?? baseDir;
        siteRoot = Path.GetFullPath(siteRoot);

        var noDownload = GetBool(step, "noDownload") ?? GetBool(step, "no-download") ?? false;
        var whatIf = GetBool(step, "whatIf") ?? GetBool(step, "what-if") ?? false;
        var timeoutSeconds = GetInt(step, "timeoutSeconds") ??
                             GetInt(step, "timeout-seconds") ??
                             30;
        if (timeoutSeconds <= 0)
            timeoutSeconds = 30;

        var summaryPath = ResolvePath(baseDir, GetString(step, "summaryPath") ?? GetString(step, "summary-path"))
                          ?? Path.Combine(siteRoot, "Build", "sync-wordpress-media-last-run.json");

        var contentTargets = (GetArrayOfStrings(step, "targets") ?? GetArrayOfStrings(step, "paths") ?? Array.Empty<string>())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(value => ResolvePath(baseDir, value))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => Path.GetFullPath(value!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (contentTargets.Count == 0)
        {
            contentTargets.Add(Path.Combine(siteRoot, "content", "blog"));
            contentTargets.Add(Path.Combine(siteRoot, "content", "pages"));
        }

        var processed = 0;
        var changed = 0;
        var rewrittenUrlCount = 0;
        var discoveredUrlSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var downloaded = 0;
        var downloadFailed = 0;
        var imageHintsAdded = 0;
        var iframeHintsAdded = 0;
        var sanitizedHtmlAssets = 0;
        var mediaAvailabilityCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in contentTargets)
        {
            if (!Directory.Exists(target))
                continue;

            foreach (var filePath in Directory.EnumerateFiles(target, "*.md", SearchOption.AllDirectories))
            {
                if (!IsImportedMarkdownFile(filePath))
                    continue;

                processed++;
                var original = SafeReadAllText(filePath);
                if (string.IsNullOrWhiteSpace(original))
                    continue;

                var parts = SplitFrontMatter(original);
                var body = parts.Body;
                var preferredHosts = GetPreferredWordPressHosts(parts.FrontMatter);

                WordPressMediaReferenceResult EnsureLocalMedia(string reference)
                {
                    var mediaRef = TryGetWordPressMediaLocalReference(reference);
                    if (mediaRef is null)
                        return WordPressMediaReferenceResult.Failed;

                    var cacheKey = mediaRef.LocalRouteWithQuery;
                    if (mediaAvailabilityCache.TryGetValue(cacheKey, out var cached))
                        return new WordPressMediaReferenceResult(mediaRef, cached);

                    var destinationPath = ConvertToWordPressLocalMediaPath(siteRoot, mediaRef.LocalRoute);
                    var success = File.Exists(destinationPath);
                    if (!success && !noDownload)
                    {
                        foreach (var host in preferredHosts)
                        {
                            var downloadUrl = $"https://{host}{mediaRef.LocalRoute}";
                            discoveredUrlSet.Add(downloadUrl);

                            if (whatIf)
                            {
                                downloaded++;
                                success = true;
                                break;
                            }

                            if (!TryDownloadFile(downloadUrl, destinationPath, timeoutSeconds))
                                continue;

                            downloaded++;
                            if (NormalizeDownloadedHtmlAsset(destinationPath))
                                sanitizedHtmlAssets++;
                            success = true;
                            break;
                        }

                        if (!success && !whatIf)
                            downloadFailed++;
                    }

                    mediaAvailabilityCache[cacheKey] = success;
                    return new WordPressMediaReferenceResult(mediaRef, success);
                }

                body = WordPressAbsoluteMediaUrlRegex.Replace(body, match =>
                {
                    var rawUrl = match.Groups["url"].Value;
                    var ensure = EnsureLocalMedia(rawUrl);
                    if (ensure.Reference is null || !ensure.Success)
                        return rawUrl;

                    rewrittenUrlCount++;
                    return ensure.Reference.LocalRouteWithQuery;
                });

                body = WordPressLocalMediaUrlRegex.Replace(body, match =>
                {
                    var rawRoute = match.Groups["route"].Value;
                    var ensure = EnsureLocalMedia(rawRoute);
                    if (ensure.Reference is null)
                        return rawRoute;

                    if (!ensure.Success && noDownload)
                        return rawRoute;

                    return ensure.Reference.LocalRouteWithQuery;
                });

                body = Regex.Replace(body, "(?is)<img\\b(?<attrs>[^>]*)>", match =>
                {
                    var attrs = ParseHtmlAttributes(match.Groups["attrs"].Value);
                    if (!attrs.TryGetValue("src", out var src) || string.IsNullOrWhiteSpace(src))
                        return match.Value;

                    if (!attrs.ContainsKey("loading"))
                    {
                        attrs["loading"] = "lazy";
                        imageHintsAdded++;
                    }

                    if (!attrs.ContainsKey("decoding"))
                    {
                        attrs["decoding"] = "async";
                        imageHintsAdded++;
                    }

                    var hasWidth = attrs.TryGetValue("width", out var widthValue) &&
                                   int.TryParse(widthValue, out var width) &&
                                   width > 0;
                    var hasHeight = attrs.TryGetValue("height", out var heightValue) &&
                                    int.TryParse(heightValue, out var height) &&
                                    height > 0;
                    var hasAspectRatioStyle =
                        attrs.TryGetValue("style", out var styleValue) &&
                        styleValue.IndexOf("aspect-ratio", StringComparison.OrdinalIgnoreCase) >= 0;

                    if ((!hasWidth || !hasHeight) && !hasAspectRatioStyle)
                    {
                        var existingStyle = attrs.TryGetValue("style", out var existing) ? existing : string.Empty;
                        attrs["style"] = SetStyleAspectRatioHint(existingStyle);
                        imageHintsAdded++;
                    }

                    return $"<img {ConvertToHtmlAttributeString(attrs)}>";
                });

                body = Regex.Replace(body, "(?is)<iframe\\b(?<attrs>[^>]*)>", match =>
                {
                    var attrs = ParseHtmlAttributes(match.Groups["attrs"].Value);
                    if (!attrs.TryGetValue("src", out var src) || string.IsNullOrWhiteSpace(src))
                        return match.Value;

                    var rewritten = ConvertToYouTubeNoCookieUrl(src);
                    if (!string.Equals(rewritten, src, StringComparison.Ordinal))
                    {
                        attrs["src"] = rewritten;
                        iframeHintsAdded++;
                    }

                    if (!attrs.ContainsKey("loading"))
                    {
                        attrs["loading"] = "lazy";
                        iframeHintsAdded++;
                    }

                    if (!attrs.ContainsKey("title"))
                    {
                        var currentSrc = attrs.TryGetValue("src", out var srcValue) ? srcValue : src;
                        var isYouTube = currentSrc.IndexOf("youtube", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        currentSrc.IndexOf("youtu.be", StringComparison.OrdinalIgnoreCase) >= 0;
                        attrs["title"] = isYouTube ? "YouTube video player" : "Embedded content";
                        iframeHintsAdded++;
                    }

                    if (!attrs.ContainsKey("referrerpolicy"))
                    {
                        attrs["referrerpolicy"] = "strict-origin-when-cross-origin";
                        iframeHintsAdded++;
                    }

                    return $"<iframe {ConvertToHtmlAttributeString(attrs)}>";
                });

                var updated = parts.FrontMatter + body;
                if (string.Equals(updated, original, StringComparison.Ordinal))
                    continue;

                changed++;
                if (!whatIf)
                    File.WriteAllText(filePath, updated, new UTF8Encoding(false));
            }
        }

        var staticUploadsPath = Path.Combine(siteRoot, "static", "wp-content", "uploads");
        if (!whatIf && Directory.Exists(staticUploadsPath))
        {
            foreach (var asset in Directory.EnumerateFiles(staticUploadsPath, "*.*", SearchOption.AllDirectories))
            {
                var extension = Path.GetExtension(asset);
                if (!extension.Equals(".html", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".htm", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (NormalizeDownloadedHtmlAsset(asset))
                    sanitizedHtmlAssets++;
            }
        }

        var summary = new
        {
            siteRoot,
            processedFiles = processed,
            changedFiles = changed,
            discoveredMediaUrls = discoveredUrlSet.Count,
            rewrittenUrls = rewrittenUrlCount,
            downloadedFiles = downloaded,
            failedDownloads = downloadFailed,
            imageHintsAdded,
            iframeHintsAdded,
            sanitizedHtmlAssets,
            noDownload,
            whatIf
        };

        if (!whatIf)
        {
            var summaryDirectory = Path.GetDirectoryName(summaryPath);
            if (!string.IsNullOrWhiteSpace(summaryDirectory))
                Directory.CreateDirectory(summaryDirectory);
            File.WriteAllText(summaryPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        }

        stepResult.Success = true;
        stepResult.Message = $"wordpress-media-sync ok: processed={processed}; changed={changed}; rewritten={rewrittenUrlCount}; downloaded={downloaded}";
    }

    private static string[] GetPreferredWordPressHosts(string? frontMatter)
    {
        var candidates = new List<string>();
        var wpLink = GetFrontMatterValue(frontMatter, "meta.wp_link");
        if (!string.IsNullOrWhiteSpace(wpLink) &&
            Uri.TryCreate(wpLink, UriKind.Absolute, out var uri) &&
            !string.IsNullOrWhiteSpace(uri.Host))
        {
            var host = uri.Host.ToLowerInvariant();
            candidates.Add(host);
            if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                candidates.Add(host.Substring(4));
            else
                candidates.Add("www." + host);
        }

        candidates.Add("evotec.xyz");
        candidates.Add("www.evotec.xyz");
        candidates.Add("evotec.pl");
        candidates.Add("www.evotec.pl");

        return candidates
            .Where(static host => !string.IsNullOrWhiteSpace(host))
            .Select(static host => host.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static WordPressMediaReference? TryGetWordPressMediaLocalReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var candidate = value.Trim();
        if (candidate.StartsWith("//", StringComparison.Ordinal))
            candidate = "https:" + candidate;

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var absoluteUri))
        {
            if (!absoluteUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
                !absoluteUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                return null;

            var host = absoluteUri.Host.ToLowerInvariant();
            if (host is not ("evotec.xyz" or "www.evotec.xyz" or "evotec.pl" or "www.evotec.pl"))
                return null;

            var path = absoluteUri.AbsolutePath;
            if (string.IsNullOrWhiteSpace(path) ||
                !path.StartsWith("/wp-content/uploads/", StringComparison.OrdinalIgnoreCase))
                return null;

            var normalizedPath = Uri.UnescapeDataString(path).Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(normalizedPath))
                return null;

            var withQuery = string.IsNullOrWhiteSpace(absoluteUri.Query)
                ? normalizedPath
                : normalizedPath + absoluteUri.Query;
            return new WordPressMediaReference(normalizedPath, withQuery);
        }

        if (!candidate.StartsWith("/wp-content/uploads/", StringComparison.OrdinalIgnoreCase))
            return null;

        var withoutHash = candidate;
        var hashIndex = withoutHash.IndexOf('#');
        if (hashIndex >= 0)
            withoutHash = withoutHash.Substring(0, hashIndex);

        string query = string.Empty;
        var routeOnly = withoutHash;
        var queryIndex = withoutHash.IndexOf('?');
        if (queryIndex >= 0)
        {
            routeOnly = withoutHash.Substring(0, queryIndex);
            query = withoutHash.Substring(queryIndex);
        }

        var localRoute = Uri.UnescapeDataString(routeOnly).Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(localRoute) ||
            !localRoute.StartsWith("/wp-content/uploads/", StringComparison.OrdinalIgnoreCase))
            return null;

        var withQueryRoute = string.IsNullOrWhiteSpace(query) ? localRoute : localRoute + query;
        return new WordPressMediaReference(localRoute, withQueryRoute);
    }

    private static string ConvertToWordPressLocalMediaPath(string siteRoot, string localRoute)
    {
        var trimmed = localRoute.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(siteRoot, "static", trimmed);
    }

    private static string SetStyleAspectRatioHint(string? style)
    {
        if (string.IsNullOrWhiteSpace(style))
            return "aspect-ratio: auto;";

        if (style.IndexOf("aspect-ratio", StringComparison.OrdinalIgnoreCase) >= 0)
            return style;

        var normalized = style.Trim();
        if (!normalized.EndsWith(';'))
            normalized += ';';
        return normalized + " aspect-ratio: auto;";
    }

    private static string ConvertToYouTubeNoCookieUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url ?? string.Empty;

        var candidate = url.Trim();
        var protocolRelative = candidate.StartsWith("//", StringComparison.Ordinal);
        if (protocolRelative)
            candidate = "https:" + candidate;

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            return url;

        var host = uri.Host.ToLowerInvariant();
        if (host is not ("youtube.com" or "www.youtube.com" or "m.youtube.com" or "youtube-nocookie.com" or "www.youtube-nocookie.com"))
            return url;

        var builder = new UriBuilder(uri)
        {
            Scheme = "https",
            Host = "www.youtube-nocookie.com"
        };
        if (!builder.Path.StartsWith("/embed/", StringComparison.Ordinal))
            return url;

        if (protocolRelative)
        {
            var query = builder.Query;
            return "//" + builder.Host + builder.Path + query;
        }

        return builder.Uri.AbsoluteUri;
    }

    private static bool TryDownloadFile(string url, string destinationPath, int timeoutSeconds)
    {
        var destinationDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDir))
            Directory.CreateDirectory(destinationDir);

        var attempts = new List<string> { url };
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Query))
            attempts.Add(uri.GetLeftPart(UriPartial.Path));

        foreach (var attempt in attempts.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var http = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(timeoutSeconds)
                };
                var response = http.GetAsync(attempt).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                    continue;

                var data = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                if (data.Length == 0)
                    continue;

                File.WriteAllBytes(destinationPath, data);
                return true;
            }
            catch
            {
                // try next attempt
            }
        }

        return false;
    }

    private static bool NormalizeDownloadedHtmlAsset(string path)
    {
        if (!File.Exists(path))
            return false;

        var extension = Path.GetExtension(path);
        if (!extension.Equals(".html", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".htm", StringComparison.OrdinalIgnoreCase))
            return false;

        var original = SafeReadAllText(path);
        if (string.IsNullOrWhiteSpace(original))
            return false;

        var normalized = Regex.Replace(
            original,
            "(?is)<script\\b[^>]*?(?:/cdn-cgi/scripts/[^\"'>]*rocket-loader\\.min\\.js|rocket-loader\\.min\\.js)[^>]*>\\s*</script>",
            string.Empty);

        var fallbackTitle = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(fallbackTitle))
            fallbackTitle = "Document";
        fallbackTitle = WebUtility.HtmlEncode(fallbackTitle);

        if (Regex.IsMatch(normalized, "(?is)<title>\\s*</title>"))
        {
            normalized = Regex.Replace(normalized, "(?is)<title>\\s*</title>", $"<title>{fallbackTitle}</title>");
        }
        else if (!Regex.IsMatch(normalized, "(?is)<title\\b[^>]*>.*?</title>"))
        {
            normalized = Regex.Replace(normalized, "(?is)(<head\\b[^>]*>)", "$1\r\n<title>" + fallbackTitle + "</title>");
        }

        if (string.Equals(normalized, original, StringComparison.Ordinal))
            return false;

        File.WriteAllText(path, normalized, new UTF8Encoding(false));
        return true;
    }

    private static string ConvertToHtmlAttributeString(Dictionary<string, string> attributes)
    {
        if (attributes.Count == 0)
            return string.Empty;

        var keys = attributes.Keys
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase);

        var parts = new List<string>();
        foreach (var key in keys)
        {
            var value = attributes.TryGetValue(key, out var raw) ? raw : string.Empty;
            var encoded = value.Replace("\"", "&quot;", StringComparison.Ordinal);
            parts.Add($"{key}=\"{encoded}\"");
        }

        return string.Join(" ", parts);
    }

    private sealed record WordPressMediaReference(string LocalRoute, string LocalRouteWithQuery);

    private sealed record WordPressMediaReferenceResult(WordPressMediaReference? Reference, bool Success)
    {
        public static WordPressMediaReferenceResult Failed { get; } = new(null, false);
    }
}
