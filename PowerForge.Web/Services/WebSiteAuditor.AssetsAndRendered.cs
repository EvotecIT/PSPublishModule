using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlTinkerX;

namespace PowerForge.Web;

/// <summary>Audits generated HTML output using static checks.</summary>
public static partial class WebSiteAuditor
{
    private static IEnumerable<string> GetAssetHrefs(AngleSharp.Dom.IDocument doc)
    {
        foreach (var link in doc.QuerySelectorAll("link[href]"))
        {
            var rel = (link.GetAttribute("rel") ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(rel))
                continue;

            if (rel.Contains("stylesheet") || rel.Contains("icon") || rel.Contains("manifest") || rel.Contains("preload"))
            {
                var href = link.GetAttribute("href");
                if (!string.IsNullOrWhiteSpace(href))
                    yield return href;
            }
        }

        foreach (var script in doc.QuerySelectorAll("script[src]"))
        {
            var src = script.GetAttribute("src");
            if (!string.IsNullOrWhiteSpace(src))
                yield return src;
        }

        foreach (var img in doc.QuerySelectorAll("img[src]"))
        {
            var src = img.GetAttribute("src");
            if (!string.IsNullOrWhiteSpace(src))
                yield return src;
        }

        foreach (var source in doc.QuerySelectorAll("source[src]"))
        {
            var src = source.GetAttribute("src");
            if (!string.IsNullOrWhiteSpace(src))
                yield return src;
        }
    }

    private static IEnumerable<string> GetAssetSrcSets(AngleSharp.Dom.IDocument doc)
    {
        foreach (var img in doc.QuerySelectorAll("img[srcset]"))
        {
            var srcset = img.GetAttribute("srcset");
            if (!string.IsNullOrWhiteSpace(srcset))
                yield return srcset;
        }

        foreach (var source in doc.QuerySelectorAll("source[srcset]"))
        {
            var srcset = source.GetAttribute("srcset");
            if (!string.IsNullOrWhiteSpace(srcset))
                yield return srcset;
        }
    }

    private static IEnumerable<string> ParseSrcSet(string srcset)
    {
        if (string.IsNullOrWhiteSpace(srcset))
            yield break;

        var parts = srcset.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;
            var spaceIdx = trimmed.IndexOf(' ');
            if (spaceIdx > 0)
                trimmed = trimmed.Substring(0, spaceIdx);
            if (!string.IsNullOrWhiteSpace(trimmed))
                yield return trimmed;
        }
    }

    private static string ToRelative(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            return fullPath;
        return Path.GetRelativePath(fullRoot, fullPath).Replace('\\', '/');
    }

    private static (string? BaseUrl, CancellationTokenSource? Cts, Task? ServerTask) EnsureRenderedBaseUrl(
        string siteRoot,
        WebAuditOptions options,
        IList<string> warnings)
    {
        if (!string.IsNullOrWhiteSpace(options.RenderedBaseUrl))
            return (options.RenderedBaseUrl.TrimEnd('/'), null, null);

        if (!options.RenderedServe)
            return (null, null, null);

        var host = string.IsNullOrWhiteSpace(options.RenderedServeHost) ? "localhost" : options.RenderedServeHost;
        var port = options.RenderedServePort;
        if (port <= 0)
        {
            port = GetFreePort();
        }

        var cts = new CancellationTokenSource();
        var task = Task.Run(() => WebStaticServer.Serve(siteRoot, host, port, cts.Token), cts.Token);
        var baseUrl = $"http://{host}:{port}";
        return (baseUrl, cts, task);
    }

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string CombineUrl(string baseUrl, string path)
    {
        var trimmedBase = baseUrl.TrimEnd('/');
        var trimmedPath = path.StartsWith("/") ? path : "/" + path;
        return trimmedBase + trimmedPath;
    }

    private static string ToRoutePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return "/";

        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (normalized.EndsWith("index.html", StringComparison.OrdinalIgnoreCase))
        {
            var withoutIndex = normalized.Substring(0, normalized.Length - "index.html".Length);
            if (string.IsNullOrWhiteSpace(withoutIndex))
                return "/";
            return "/" + withoutIndex.TrimEnd('/') + "/";
        }

        return "/" + normalized;
    }

    private static List<string> FilterRenderedFiles(string siteRoot, List<string> htmlFiles, string[] includePatterns, string[] excludePatterns)
    {
        var includes = NormalizePatterns(includePatterns);
        var excludes = NormalizePatterns(excludePatterns);
        if (includes.Length == 0 && excludes.Length == 0)
            return htmlFiles;

        var list = new List<string>();
        foreach (var file in htmlFiles)
        {
            var relative = Path.GetRelativePath(siteRoot, file).Replace('\\', '/');
            if (excludes.Length > 0 && MatchesAny(excludes, relative))
                continue;
            if (includes.Length > 0 && !MatchesAny(includes, relative))
                continue;
            list.Add(file);
        }
        return list;
    }

    private static string ResolveSummaryPath(string siteRoot, string summaryPath)
    {
        var normalizedRoot = NormalizeRootPath(siteRoot);
        var trimmed = summaryPath.Trim();
        var full = Path.IsPathRooted(trimmed)
            ? trimmed
            : Path.Combine(siteRoot, trimmed);
        var resolved = Path.GetFullPath(full);
        if (!IsPathWithinRoot(normalizedRoot, resolved))
            throw new InvalidOperationException($"Path must resolve under site root: {summaryPath}");
        return resolved;
    }

    private static string ResolveBaselinePath(string siteRoot, string? baselineRoot, string baselinePath)
    {
        var trimmed = baselinePath.Trim();
        if (Path.IsPathRooted(trimmed))
            return Path.GetFullPath(trimmed);

        var root = string.IsNullOrWhiteSpace(baselineRoot) ? siteRoot : baselineRoot.Trim();
        var normalizedRoot = NormalizeRootPath(root);
        var resolved = Path.GetFullPath(Path.Combine(normalizedRoot, trimmed));
        if (!IsPathWithinRoot(normalizedRoot, resolved))
            throw new InvalidOperationException($"Baseline path must resolve under baseline root: {baselinePath}");
        return resolved;
    }

    private static string NormalizeRootPath(string siteRoot)
    {
        var full = Path.GetFullPath(siteRoot);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    private static bool IsPathWithinRoot(string normalizedRoot, string candidatePath)
    {
        var full = Path.GetFullPath(candidatePath);
        return full.StartsWith(normalizedRoot, FileSystemPathComparison);
    }

    private static string BuildIssueKey(string severity, string category, string? path, string hint)
    {
        static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            var trimmed = value.Trim().ToLowerInvariant();
            var chars = trimmed.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
            var normalized = new string(chars);
            while (normalized.Contains("--", StringComparison.Ordinal))
                normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
            return normalized.Trim('-');
        }

        var normalizedPath = string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Join("|", new[]
        {
            Normalize(severity),
            Normalize(category),
            Normalize(normalizedPath),
            Normalize(hint)
        });
    }

    private static HashSet<string> LoadBaselineIssueKeys(
        string baselinePath,
        Action<string, string, string?, string, string?> addIssue)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(baselinePath))
        {
            addIssue("warning", "baseline", null, $"Baseline file not found: {baselinePath}.", "baseline-missing");
            return keys;
        }

        var info = new FileInfo(baselinePath);
        if (info.Length > MaxAuditDataFileSizeBytes)
        {
            addIssue("warning", "baseline", null, $"Baseline file is too large ({info.Length} bytes).", "baseline-too-large");
            return keys;
        }

        try
        {
            using var stream = File.OpenRead(baselinePath);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            if (TryGetPropertyIgnoreCase(root, "issueKeys", out var issueKeys) && issueKeys.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in issueKeys.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        keys.Add(value);
                }
            }

            if (TryGetPropertyIgnoreCase(root, "issues", out var issues) && issues.ValueKind == JsonValueKind.Array)
            {
                foreach (var issue in issues.EnumerateArray())
                {
                    if (issue.ValueKind != JsonValueKind.Object) continue;
                    if (!TryGetPropertyIgnoreCase(issue, "key", out var keyElement) || keyElement.ValueKind != JsonValueKind.String) continue;
                    var value = keyElement.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        keys.Add(value);
                }
            }

            if (keys.Count == 0)
                addIssue("warning", "baseline", null, $"Baseline file does not contain issue keys: {baselinePath}.", "baseline-empty");
        }
        catch (Exception ex)
        {
            addIssue("warning", "baseline", null, $"Baseline file parse failed ({ex.Message}).", "baseline-parse");
        }

        return keys;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        if (element.TryGetProperty(propertyName, out value))
            return true;

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string ReadFileAsUtf8(
        string filePath,
        string relativePath,
        Action<string, string, string?, string, string?> addIssue)
    {
        var bytes = File.ReadAllBytes(filePath);
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            var offset = ex.Index >= 0 ? $" at byte offset {ex.Index}" : string.Empty;
            addIssue("error", "utf8", relativePath, $"invalid UTF-8 byte sequence{offset} ({ex.Message}).", "utf8-invalid");
            return Encoding.UTF8.GetString(bytes);
        }
    }

    private static bool HasUtf8Meta(AngleSharp.Dom.IDocument doc)
    {
        foreach (var meta in doc.QuerySelectorAll("meta"))
        {
            var charset = meta.GetAttribute("charset");
            if (!string.IsNullOrWhiteSpace(charset) &&
                charset.Trim().Equals("utf-8", StringComparison.OrdinalIgnoreCase))
                return true;

            var httpEquiv = meta.GetAttribute("http-equiv");
            var content = meta.GetAttribute("content");
            if (!string.IsNullOrWhiteSpace(httpEquiv) &&
                httpEquiv.Equals("content-type", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(content) &&
                content.IndexOf("charset=utf-8", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string[] TakeIssues(string[] issues, int max)
    {
        if (issues.Length == 0) return Array.Empty<string>();
        if (max <= 0) return Array.Empty<string>();
        return issues.Take(max).ToArray();
    }

    private static WebAuditIssue[] TakeIssues(WebAuditIssue[] issues, int max)
    {
        if (issues.Length == 0) return Array.Empty<WebAuditIssue>();
        if (max <= 0) return Array.Empty<WebAuditIssue>();
        return issues.Take(max).ToArray();
    }

    private static void WriteSummary(string summaryPath, WebAuditSummary summary, IList<string> warnings)
    {
        try
        {
            var dir = Path.GetDirectoryName(summaryPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
            var json = System.Text.Json.JsonSerializer.Serialize(summary, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(summaryPath, json);
        }
        catch (Exception ex)
        {
            warnings.Add($"Audit summary write failed: {ex.Message}");
        }
    }

    private static void WriteSarif(string sarifPath, IReadOnlyList<WebAuditIssue> issues, IList<string> warnings)
    {
        try
        {
            var dir = Path.GetDirectoryName(sarifPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var rules = issues
                .Where(issue => !string.IsNullOrWhiteSpace(issue.Category))
                .Select(issue => issue.Category.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(category => new
                {
                    id = "powerforge.web/" + category,
                    shortDescription = new { text = "PowerForge.Web audit category: " + category }
                })
                .ToArray();

            var results = issues.Select(issue =>
            {
                var ruleId = "powerforge.web/" +
                             (string.IsNullOrWhiteSpace(issue.Category) ? "general" : issue.Category.Trim().ToLowerInvariant());
                var location = string.IsNullOrWhiteSpace(issue.Path)
                    ? null
                    : new[]
                    {
                        new
                        {
                            physicalLocation = new
                            {
                                artifactLocation = new { uri = issue.Path.Replace('\\', '/') }
                            }
                        }
                    };

                return new
                {
                    ruleId,
                    level = MapSarifLevel(issue.Severity),
                    message = new { text = issue.Message },
                    locations = location,
                    properties = new
                    {
                        key = issue.Key,
                        category = issue.Category,
                        severity = issue.Severity,
                        isNew = issue.IsNew
                    }
                };
            }).ToArray();

            var sarif = new Dictionary<string, object?>
            {
                ["$schema"] = "https://json.schemastore.org/sarif-2.1.0.json",
                ["version"] = "2.1.0",
                ["runs"] = new[]
                {
                    new
                    {
                        tool = new
                        {
                            driver = new
                            {
                                name = "PowerForge.Web",
                                fullName = "PowerForge.Web Static Site Audit",
                                version = typeof(WebSiteAuditor).Assembly.GetName().Version?.ToString() ?? "unknown",
                                rules
                            }
                        },
                        results
                    }
                }
            };

            var json = JsonSerializer.Serialize(sarif, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(sarifPath, json);
        }
        catch (Exception ex)
        {
            warnings.Add($"Audit SARIF write failed: {ex.Message}");
        }
    }

    private static string MapSarifLevel(string? severity)
    {
        if (string.IsNullOrWhiteSpace(severity))
            return "warning";

        if (severity.Equals("error", StringComparison.OrdinalIgnoreCase))
            return "error";
        if (severity.Equals("info", StringComparison.OrdinalIgnoreCase))
            return "note";

        return "warning";
    }

    private static HtmlBrowserEngine ResolveEngine(string? value, IList<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(value))
            return HtmlBrowserEngine.Chromium;

        if (Enum.TryParse<HtmlBrowserEngine>(value, true, out var engine))
            return engine;

        warnings.Add($"Rendered engine '{value}' not recognized; using Chromium.");
        return HtmlBrowserEngine.Chromium;
    }

    private static string BuildConsoleSummary(IEnumerable<object>? entries, int max)
    {
        return BuildRenderedSummary(entries, max, entry =>
        {
            var text = GetEntryString(entry, "Text") ?? entry?.ToString();
            if (string.IsNullOrWhiteSpace(text))
                return null;
            var location = GetEntryString(entry, "FullLocation") ?? GetEntryString(entry, "Location");
            return string.IsNullOrWhiteSpace(location) ? text : $"{text} ({location})";
        });
    }

    private static string BuildFailedRequestSummary(IEnumerable<object>? entries, int max)
    {
        return BuildRenderedSummary(entries, max, entry =>
        {
            var url = GetEntryString(entry, "Url");
            if (string.IsNullOrWhiteSpace(url))
                url = entry?.ToString();

            var method = GetEntryString(entry, "Method");
            var status = GetEntryString(entry, "Status");
            var error = GetEntryString(entry, "ErrorMessage") ?? GetEntryString(entry, "ErrorType");

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(method))
                parts.Add(method);
            if (!string.IsNullOrWhiteSpace(url))
                parts.Add(url);
            if (!string.IsNullOrWhiteSpace(status))
                parts.Add($"status {status}");
            if (!string.IsNullOrWhiteSpace(error))
                parts.Add(error);

            return parts.Count == 0 ? null : string.Join(" ", parts);
        });
    }

    private static string BuildRenderedSummary(IEnumerable<object>? entries, int max, Func<object?, string?> formatter)
    {
        if (entries is null || max <= 0)
            return string.Empty;

        var list = new List<string>();
        foreach (var entry in entries)
        {
            if (entry is null) continue;
            var formatted = formatter(entry);
            if (string.IsNullOrWhiteSpace(formatted))
                continue;
            list.Add(formatted.Trim());
            if (list.Count >= max)
                break;
        }

        return list.Count == 0 ? string.Empty : string.Join(" | ", list);
    }

    private static string? GetEntryString(object? entry, string property)
    {
        if (entry is null) return null;
        var prop = entry.GetType().GetProperty(property);
        if (prop is null) return null;
        var value = prop.GetValue(entry);
        return value?.ToString();
    }


    private static bool IsPlaywrightMissing(IEnumerable<object>? entries, out string? message)
    {
        message = null;
        if (entries is null) return false;

        foreach (var entry in entries)
        {
            var text = GetEntryString(entry, "Text");
            if (string.IsNullOrWhiteSpace(text))
                continue;
            if (text.IndexOf("Executable doesn't exist", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (text.IndexOf("playwright", StringComparison.OrdinalIgnoreCase) >= 0 &&
                 text.IndexOf("install", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                message = text.Trim();
                return true;
            }
        }

        return false;
    }
}
