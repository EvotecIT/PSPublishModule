using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using HtmlTinkerX;

namespace PowerForge.Web;

/// <summary>Audits generated HTML output using static checks.</summary>
public static partial class WebSiteAuditor
{
    private sealed class RenderedContrastFinding
    {
        public string Selector { get; init; } = string.Empty;
        public string Text { get; init; } = string.Empty;
        public double Ratio { get; init; }
        public double Required { get; init; }
    }

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
        var normalizedPath = string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/').Trim().ToLowerInvariant();
        return string.Join("|", new[]
        {
            NormalizeIssueToken(severity),
            NormalizeIssueToken(category),
            NormalizeIssueToken(normalizedPath),
            NormalizeIssueToken(hint)
        });
    }

    private static string NormalizeIssueToken(string? value)
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

    private static string BuildIssueCategoryCode(string category)
    {
        var normalizedCategory = NormalizeIssueToken(category);
        if (string.IsNullOrWhiteSpace(normalizedCategory))
            normalizedCategory = "general";
        return "PFAUDIT." + normalizedCategory.ToUpperInvariant();
    }

    private static string BuildIssueRuleCode(string category, string? hint)
    {
        var categoryCode = BuildIssueCategoryCode(category);
        var normalizedHint = NormalizeIssueToken(hint);
        if (string.IsNullOrWhiteSpace(normalizedHint))
            return categoryCode;
        return categoryCode + "." + normalizedHint.ToUpperInvariant();
    }

    private static bool MatchesFailIssuePatterns(WebAuditIssue issue, string[] patterns)
    {
        if (issue is null || patterns.Length == 0)
            return false;

        var categoryCode = BuildIssueCategoryCode(issue.Category);
        var ruleCode = string.IsNullOrWhiteSpace(issue.Code)
            ? BuildIssueRuleCode(issue.Category, issue.Hint)
            : issue.Code.Trim();
        var hint = NormalizeIssueToken(issue.Hint);
        var key = issue.Key ?? string.Empty;
        var path = issue.Path ?? string.Empty;
        var message = issue.Message ?? string.Empty;
        var descriptor = $"[{categoryCode}] [{ruleCode}] hint:{hint} key:{key} path:{path} message:{message}";

        return WebSuppressionMatcher.IsSuppressed(ruleCode, ruleCode, patterns) ||
               WebSuppressionMatcher.IsSuppressed(categoryCode, categoryCode, patterns) ||
               WebSuppressionMatcher.IsSuppressed(hint, ruleCode, patterns) ||
               WebSuppressionMatcher.IsSuppressed(key, ruleCode, patterns) ||
               WebSuppressionMatcher.IsSuppressed(descriptor, ruleCode, patterns);
    }

    private static HashSet<string> LoadBaselineIssueKeys(
        string baselinePath,
        Action<string, string, string?, string, string?> addIssue,
        out bool keysAreHashed)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        keysAreHashed = false;
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

            if (TryGetPropertyIgnoreCase(root, "issueKeyHashes", out var issueKeyHashes) && issueKeyHashes.ValueKind == JsonValueKind.Array)
            {
                keysAreHashed = true;
                foreach (var item in issueKeyHashes.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        keys.Add(value);
                }

                if (keys.Count == 0)
                    addIssue("warning", "baseline", null, $"Baseline file does not contain issue keys: {baselinePath}.", "baseline-empty");

                return keys;
            }

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

    private static RenderedContrastFinding[] FindRenderedContrastIssues(
        string url,
        HtmlBrowserEngine engine,
        bool headless,
        int timeoutMs,
        double minRatio,
        int maxFindings)
    {
        if (string.IsNullOrWhiteSpace(url))
            return Array.Empty<RenderedContrastFinding>();

        var safeMaxFindings = Math.Clamp(maxFindings, 1, 200);
        var safeMinRatio = double.IsFinite(minRatio) && minRatio > 0 ? minRatio : 4.5d;
        var script = BuildRenderedContrastScript(safeMinRatio, safeMaxFindings);

        HtmlBrowserSession? session = null;
        try
        {
            session = HtmlBrowser.OpenSessionAsync(
                    url,
                    browser: engine,
                    headless: headless,
                    timeout: timeoutMs)
                .GetAwaiter()
                .GetResult();

            var json = HtmlBrowser.EvaluateAsync<string>(session, script)
                .GetAwaiter()
                .GetResult();
            return ParseRenderedContrastFindings(json, safeMaxFindings);
        }
        finally
        {
            if (session is not null)
            {
                try
                {
                    HtmlBrowser.CloseSessionAsync(session).GetAwaiter().GetResult();
                }
                catch
                {
                    // best effort cleanup
                }
            }
        }
    }

    private static string BuildRenderedContrastSummary(RenderedContrastFinding[] findings, int max)
    {
        if (findings.Length == 0 || max <= 0)
            return string.Empty;

        var items = findings
            .Take(max)
            .Select(finding =>
            {
                var selector = string.IsNullOrWhiteSpace(finding.Selector) ? "<unknown>" : finding.Selector;
                var text = string.IsNullOrWhiteSpace(finding.Text) ? "<text>" : finding.Text;
                text = Regex.Replace(text, "\\s+", " ").Trim();
                if (text.Length > 80)
                    text = text.Substring(0, 80) + "...";
                return $"{selector} \"{text}\" {finding.Ratio:0.00}<{finding.Required:0.00}";
            })
            .ToArray();
        return items.Length == 0 ? string.Empty : string.Join(" | ", items);
    }

    private static RenderedContrastFinding[] ParseRenderedContrastFindings(string? json, int maxFindings)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<RenderedContrastFinding>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return Array.Empty<RenderedContrastFinding>();

            var list = new List<RenderedContrastFinding>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var selector = TryGetString(item, "selector") ?? string.Empty;
                var text = TryGetString(item, "text") ?? string.Empty;
                var ratio = TryGetDouble(item, "ratio");
                var required = TryGetDouble(item, "required");
                if (!ratio.HasValue || !required.HasValue)
                    continue;

                list.Add(new RenderedContrastFinding
                {
                    Selector = selector,
                    Text = text,
                    Ratio = ratio.Value,
                    Required = required.Value
                });

                if (list.Count >= maxFindings)
                    break;
            }

            return list.ToArray();
        }
        catch
        {
            return Array.Empty<RenderedContrastFinding>();
        }
    }

    private static string BuildRenderedContrastScript(double minRatio, int maxFindings)
    {
        var minRatioLiteral = minRatio.ToString("0.###", CultureInfo.InvariantCulture);
        var maxFindingsLiteral = maxFindings.ToString(CultureInfo.InvariantCulture);
        return $$"""
(() => {
  const minRatio = {{minRatioLiteral}};
  const maxFindings = {{maxFindingsLiteral}};
  const white = { r: 255, g: 255, b: 255, a: 1 };

  function parseRgba(value) {
    if (!value) return null;
    const match = value.trim().match(/^rgba?\(([^)]+)\)$/i);
    if (!match) return null;
    const parts = match[1].split(',').map(p => p.trim());
    if (parts.length < 3) return null;
    const r = Number.parseFloat(parts[0]);
    const g = Number.parseFloat(parts[1]);
    const b = Number.parseFloat(parts[2]);
    const a = parts.length >= 4 ? Number.parseFloat(parts[3]) : 1;
    if ([r, g, b, a].some(Number.isNaN)) return null;
    return { r, g, b, a: Math.max(0, Math.min(1, a)) };
  }

  function composite(fg, bg) {
    const a = fg.a + bg.a * (1 - fg.a);
    if (a <= 0) return { r: 0, g: 0, b: 0, a: 0 };
    return {
      r: ((fg.r * fg.a) + (bg.r * bg.a * (1 - fg.a))) / a,
      g: ((fg.g * fg.a) + (bg.g * bg.a * (1 - fg.a))) / a,
      b: ((fg.b * fg.a) + (bg.b * bg.a * (1 - fg.a))) / a,
      a
    };
  }

  function luminance(color) {
    function channel(v) {
      const n = v / 255;
      return n <= 0.03928 ? n / 12.92 : Math.pow((n + 0.055) / 1.055, 2.4);
    }
    return 0.2126 * channel(color.r) + 0.7152 * channel(color.g) + 0.0722 * channel(color.b);
  }

  function contrastRatio(fg, bg) {
    const l1 = luminance(fg);
    const l2 = luminance(bg);
    const lighter = Math.max(l1, l2);
    const darker = Math.min(l1, l2);
    return (lighter + 0.05) / (darker + 0.05);
  }

  function isVisible(el, style) {
    if (!el || !style) return false;
    if (style.display === 'none' || style.visibility === 'hidden') return false;
    if (Number.parseFloat(style.opacity || '1') <= 0) return false;
    const rect = el.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0;
  }

  function effectiveBackground(el) {
    const layers = [];
    let current = el;
    while (current) {
      const style = getComputedStyle(current);
      const bg = parseRgba(style.backgroundColor);
      if (bg && bg.a > 0) layers.push(bg);
      current = current.parentElement;
    }

    let merged = white;
    for (let i = layers.length - 1; i >= 0; i--) {
      merged = composite(layers[i], merged);
      if (merged.a >= 0.999) break;
    }

    return merged;
  }

  function buildSelector(el) {
    if (!el) return '';
    if (el.id) return `${el.tagName.toLowerCase()}#${el.id}`;
    const parts = [];
    let current = el;
    let depth = 0;
    while (current && depth < 4) {
      let part = current.tagName.toLowerCase();
      const classes = (current.className || '').toString().trim().split(/\s+/).filter(Boolean);
      if (classes.length > 0) part += '.' + classes[0];
      parts.unshift(part);
      current = current.parentElement;
      depth++;
    }
    return parts.join(' > ');
  }

  const results = [];
  const seen = new Set();
  const walker = document.createTreeWalker(document.body || document.documentElement, NodeFilter.SHOW_TEXT);
  let textNode = null;
  while ((textNode = walker.nextNode()) && results.length < maxFindings) {
    const text = (textNode.textContent || '').replace(/\s+/g, ' ').trim();
    if (!text) continue;
    const el = textNode.parentElement;
    if (!el) continue;
    const tagName = (el.tagName || '').toLowerCase();
    if (tagName === 'script' || tagName === 'style' || tagName === 'noscript') continue;
    if (el.closest('[aria-hidden=\"true\"]')) continue;

    const style = getComputedStyle(el);
    if (!isVisible(el, style)) continue;
    const fgBase = parseRgba(style.color);
    if (!fgBase) continue;

    const bg = effectiveBackground(el);
    const fg = fgBase.a < 0.999 ? composite(fgBase, bg) : fgBase;
    const ratio = contrastRatio(fg, bg);
    const fontSize = Number.parseFloat(style.fontSize || '16');
    const fontWeight = Number.parseInt(style.fontWeight || '400', 10) || 400;
    const isLarge = fontSize >= 24 || (fontSize >= 18.66 && fontWeight >= 700);
    const required = isLarge ? 3 : minRatio;

    if (ratio + 0.0001 >= required) continue;

    const selector = buildSelector(el);
    const key = `${selector}|${text}`;
    if (seen.has(key)) continue;
    seen.add(key);

    results.push({
      selector,
      text: text.slice(0, 120),
      ratio: Number(ratio.toFixed(2)),
      required: Number(required.toFixed(2))
    });
  }

  return JSON.stringify(results);
})()
""";
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        return value.GetString();
    }

    private static double? TryGetDouble(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var direct))
            return direct;
        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
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
