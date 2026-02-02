using System.Text.RegularExpressions;
using HtmlTinkerX;

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
    /// <summary>Enable asset hashing (fingerprinting).</summary>
    public bool HashAssets { get; set; }
    /// <summary>File extensions to hash.</summary>
    public string[] HashExtensions { get; set; } = new[] { ".css", ".js" };
    /// <summary>Glob-style exclude patterns for hashing.</summary>
    public string[] HashExclude { get; set; } = Array.Empty<string>();
    /// <summary>Optional manifest path for hashed assets.</summary>
    public string? HashManifestPath { get; set; }
    /// <summary>Optional asset policy for rewrites and headers.</summary>
    public AssetPolicySpec? AssetPolicy { get; set; }
}

/// <summary>Optimizes generated site assets (critical CSS, minification).</summary>
public static class WebAssetOptimizer
{
    /// <summary>Runs asset optimization and returns the count of updated HTML files.</summary>
    /// <param name="options">Optimization options.</param>
    /// <returns>Number of HTML files updated with critical CSS.</returns>
    public static int Optimize(WebAssetOptimizerOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        var siteRoot = Path.GetFullPath(options.SiteRoot);
        if (!Directory.Exists(siteRoot))
            throw new DirectoryNotFoundException($"Site root not found: {siteRoot}");

        var htmlFiles = Directory.EnumerateFiles(siteRoot, "*.html", SearchOption.AllDirectories).ToArray();
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
                    File.WriteAllText(htmlFile, updated);
            }
        }
        var criticalCss = LoadCriticalCss(options.CriticalCssPath);
        var cssPattern = new Regex(options.CssLinkPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

        var processed = 0;
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
                    processed++;
                }
            }
        }

        var hashSpec = ResolveHashSpec(options, policy);
        Dictionary<string, string>? hashMap = null;
        if (hashSpec.Enabled)
        {
            hashMap = HashAssets(siteRoot, hashSpec);
            if (hashMap.Count > 0)
            {
                RewriteHashedReferences(siteRoot, htmlFiles, hashMap);
                WriteHashManifest(siteRoot, hashSpec, hashMap);
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
                catch
                {
                    minified = null;
                }
                if (!string.IsNullOrWhiteSpace(minified) && !string.Equals(html, minified, StringComparison.Ordinal))
                    File.WriteAllText(htmlFile, minified);
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
                catch
                {
                    minified = null;
                }
                if (!string.IsNullOrWhiteSpace(minified) && !string.Equals(css, minified, StringComparison.Ordinal))
                    File.WriteAllText(cssFile, minified);
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
                catch
                {
                    minified = null;
                }
                if (!string.IsNullOrWhiteSpace(minified) && !string.Equals(js, minified, StringComparison.Ordinal))
                    File.WriteAllText(jsFile, minified);
            }
        }

        if (policy?.CacheHeaders?.Enabled == true)
            WriteCacheHeaders(siteRoot, policy.CacheHeaders, hashMap);

        return processed;
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
            var dest = Path.Combine(siteRoot, destRelative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(source, dest, overwrite: true);
        }
    }

    private static string RewriteHtmlAssets(string html, AssetRewriteSpec[] rewrites)
    {
        if (rewrites.Length == 0) return html;
        var attrRegex = new Regex("(?<attr>href|src)=\"(?<url>[^\"]+)\"", RegexOptions.IgnoreCase);
        return attrRegex.Replace(html, match =>
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
                        var regex = new Regex(rewrite.Match, RegexOptions.IgnoreCase);
                        if (regex.IsMatch(url))
                            return regex.Replace(url, rewrite.Replace);
                    }
                    catch
                    {
                        // ignore regex errors
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

    private static Dictionary<string, string> HashAssets(string siteRoot, AssetHashSpec spec)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
            var without = relative.Substring(0, relative.Length - ext.Length);
            var hashedName = $"{without}.{hash}{ext}";
            var target = Path.Combine(siteRoot, hashedName.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(file, target, overwrite: true);

            map[$"/{relative}"] = $"/{hashedName}";
            map[relative] = hashedName;
        }

        return map;
    }

    private static void RewriteHashedReferences(string siteRoot, string[] htmlFiles, Dictionary<string, string> map)
    {
        foreach (var htmlFile in htmlFiles)
        {
            var html = File.ReadAllText(htmlFile);
            if (string.IsNullOrWhiteSpace(html)) continue;
            var updated = RewriteReferences(html, map);
            if (!string.Equals(updated, html, StringComparison.Ordinal))
                File.WriteAllText(htmlFile, updated);
        }

        foreach (var cssFile in Directory.EnumerateFiles(siteRoot, "*.css", SearchOption.AllDirectories))
        {
            var css = File.ReadAllText(cssFile);
            if (string.IsNullOrWhiteSpace(css)) continue;
            var updated = RewriteCssUrls(css, map);
            if (!string.Equals(updated, css, StringComparison.Ordinal))
                File.WriteAllText(cssFile, updated);
        }
    }

    private static string RewriteReferences(string html, Dictionary<string, string> map)
    {
        var attrRegex = new Regex("(?<attr>href|src)=\"(?<url>[^\"]+)\"", RegexOptions.IgnoreCase);
        return attrRegex.Replace(html, match =>
        {
            var url = match.Groups["url"].Value;
            var rewritten = RewriteUrlWithMap(url, map);
            return rewritten == url ? match.Value : $"{match.Groups["attr"].Value}=\"{rewritten}\"";
        });
    }

    private static string RewriteCssUrls(string css, Dictionary<string, string> map)
    {
        var urlRegex = new Regex("url\\((?<quote>['\"]?)(?<url>[^'\")]+)\\k<quote>\\)", RegexOptions.IgnoreCase);
        return urlRegex.Replace(css, match =>
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

    private static void WriteHashManifest(string siteRoot, AssetHashSpec spec, Dictionary<string, string> map)
    {
        if (map.Count == 0) return;
        var path = string.IsNullOrWhiteSpace(spec.ManifestPath)
            ? Path.Combine(siteRoot, "asset-manifest.json")
            : Path.Combine(siteRoot, spec.ManifestPath.TrimStart('/', '\\'));
        var json = System.Text.Json.JsonSerializer.Serialize(map, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(path, json);
    }

    private static void WriteCacheHeaders(string siteRoot, CacheHeadersSpec headers, Dictionary<string, string>? map)
    {
        var output = string.IsNullOrWhiteSpace(headers.OutputPath) ? "_headers" : headers.OutputPath;
        var outputPath = Path.Combine(siteRoot, output.TrimStart('/', '\\'));
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
    }

    private static bool GlobMatch(string pattern, string value)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        if (pattern == "*") return true;
        var regex = "^" + Regex.Escape(pattern)
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*")
            .Replace("\\?", ".") + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase);
    }

    private static string InlineCriticalCss(string content, string criticalCss, Regex cssPattern)
    {
        var linkRegex = new Regex("<link\\s+rel=\"stylesheet\"\\s+href=\"([^\"]+)\"\\s*/?>", RegexOptions.IgnoreCase);
        var match = linkRegex.Match(content);
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
        catch
        {
            return css;
        }
    }

}
