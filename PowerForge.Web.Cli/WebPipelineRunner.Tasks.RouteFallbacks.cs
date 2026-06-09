using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecuteRouteFallbacks(
        JsonElement step,
        string baseDir,
        WebPipelineStepResult stepResult)
    {
        var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
        var templatePath = ResolvePath(baseDir,
            GetString(step, "template") ??
            GetString(step, "templatePath") ??
            GetString(step, "template-path"));
        var itemsPath = ResolvePath(baseDir,
            GetString(step, "items") ??
            GetString(step, "itemsPath") ??
            GetString(step, "items-path") ??
            GetString(step, "manifest") ??
            GetString(step, "data"));
        var itemsProperty = GetString(step, "itemsProperty") ??
                            GetString(step, "items-property") ??
                            GetString(step, "itemsKey") ??
                            GetString(step, "items-key");
        var destinationTemplate = GetString(step, "destinationTemplate") ??
                                  GetString(step, "destination-template") ??
                                  GetString(step, "pathTemplate") ??
                                  GetString(step, "path-template");
        var rootOutput = GetString(step, "rootOutput") ??
                         GetString(step, "root-output") ??
                         GetString(step, "indexOutput") ??
                         GetString(step, "index-output");
        var reportPath = ResolvePath(baseDir, GetString(step, "reportPath") ?? GetString(step, "report-path"));
        var htmlEncode = GetBool(step, "htmlEncode") ?? GetBool(step, "html-encode") ?? true;
        var routeReplacements = ReadRouteFallbackStringMap(
            step,
            "replacements",
            "routeReplacements",
            "route-replacements",
            "itemValues",
            "item-values");
        var rootReplacements = ReadRouteFallbackStringMap(
            step,
            "rootValues",
            "root-values",
            "rootReplacements",
            "root-replacements");

        if (string.IsNullOrWhiteSpace(siteRoot))
            throw new InvalidOperationException("route-fallbacks requires siteRoot.");
        if (!Directory.Exists(siteRoot))
            throw new InvalidOperationException($"route-fallbacks siteRoot not found: {siteRoot}");
        if (string.IsNullOrWhiteSpace(templatePath))
            throw new InvalidOperationException("route-fallbacks requires template.");
        if (!File.Exists(templatePath))
            throw new InvalidOperationException($"route-fallbacks template not found: {templatePath}");
        if (string.IsNullOrWhiteSpace(itemsPath))
            throw new InvalidOperationException("route-fallbacks requires items.");
        if (!File.Exists(itemsPath))
            throw new InvalidOperationException($"route-fallbacks items file not found: {itemsPath}");
        if (string.IsNullOrWhiteSpace(destinationTemplate))
            throw new InvalidOperationException("route-fallbacks requires destinationTemplate.");
        if (routeReplacements.Count == 0)
            throw new InvalidOperationException("route-fallbacks requires at least one replacement token.");

        var normalizedSiteRoot = NormalizeRootPath(siteRoot);
        var templateContent = File.ReadAllText(templatePath);
        ValidateRouteFallbackTokens(templateContent, templatePath, routeReplacements, rootReplacements);

        using var document = JsonDocument.Parse(File.ReadAllText(itemsPath));
        var items = ResolveRouteFallbackItems(document.RootElement, itemsProperty);
        if (items.Count == 0)
            throw new InvalidOperationException($"route-fallbacks found no items in '{itemsPath}'.");

        var report = new RouteFallbackReport
        {
            SiteRoot = Path.GetFullPath(siteRoot),
            Template = Path.GetFullPath(templatePath),
            ItemsPath = Path.GetFullPath(itemsPath)
        };

        var written = 0;
        var changed = 0;

        if (!string.IsNullOrWhiteSpace(rootOutput))
        {
            var rootRelativePath = NormalizeRouteFallbackRelativePath(rootOutput);
            var rootContent = ApplyRouteFallbackReplacements(templateContent, rootReplacements, null, htmlEncode);
            var rootChanged = WriteRouteFallbackFile(siteRoot, normalizedSiteRoot, rootRelativePath, rootContent);
            if (rootChanged)
                changed++;
            written++;
            report.Files.Add(new RouteFallbackReportEntry
            {
                Path = rootRelativePath,
                Changed = rootChanged
            });
        }

        foreach (var item in items)
        {
            var values = BuildRouteFallbackValueMap(item);
            var relativePath = NormalizeRouteFallbackRelativePath(
                ExpandRouteFallbackTemplate(destinationTemplate, values, "destinationTemplate"));
            var content = ApplyRouteFallbackReplacements(templateContent, routeReplacements, values, htmlEncode);
            var fileChanged = WriteRouteFallbackFile(siteRoot, normalizedSiteRoot, relativePath, content);
            if (fileChanged)
                changed++;
            written++;
            report.Files.Add(new RouteFallbackReportEntry
            {
                Path = relativePath,
                Changed = fileChanged
            });
        }

        report.ItemCount = items.Count;
        report.WrittenCount = written;
        report.ChangedCount = changed;

        if (!string.IsNullOrWhiteSpace(reportPath))
            WriteRouteFallbackReport(reportPath, report);

        stepResult.Success = true;
        stepResult.Message = $"route-fallbacks ok: wrote {written} file(s), changed {changed}.";
    }

    private static List<JsonElement> ResolveRouteFallbackItems(JsonElement root, string? itemsProperty)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return new List<JsonElement>(root.EnumerateArray());

        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("route-fallbacks items must be a JSON array or an object containing an array.");

        var candidateNames = new List<string>();
        if (!string.IsNullOrWhiteSpace(itemsProperty))
            candidateNames.Add(itemsProperty);
        candidateNames.Add("items");
        candidateNames.Add("routes");
        candidateNames.Add("entries");

        foreach (var name in candidateNames)
        {
            if (!TryGetRouteFallbackProperty(root, name, out var value) || value.ValueKind != JsonValueKind.Array)
                continue;

            return new List<JsonElement>(value.EnumerateArray());
        }

        throw new InvalidOperationException("route-fallbacks items file did not contain a readable array.");
    }

    private static Dictionary<string, string> BuildRouteFallbackValueMap(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("route-fallbacks item entries must be JSON objects.");

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in item.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Null ||
                property.Value.ValueKind == JsonValueKind.Undefined)
                continue;

            values[property.Name] = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString() ?? string.Empty
                : property.Value.ToString();
        }

        return values;
    }

    private static Dictionary<string, string> ReadRouteFallbackStringMap(JsonElement step, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
                continue;

            if (!TryGetRouteFallbackProperty(step, propertyName, out var element) || element.ValueKind != JsonValueKind.Object)
                continue;

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Null ||
                    property.Value.ValueKind == JsonValueKind.Undefined)
                    continue;

                values[property.Name] = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : property.Value.ToString();
            }

            return values;
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryGetRouteFallbackProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                continue;

            value = property.Value;
            return true;
        }

        value = default;
        return false;
    }

    private static void ValidateRouteFallbackTokens(
        string templateContent,
        string templatePath,
        IReadOnlyDictionary<string, string> routeReplacements,
        IReadOnlyDictionary<string, string> rootReplacements)
    {
        var requiredTokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in routeReplacements.Keys)
            requiredTokens.Add(key);
        foreach (var key in rootReplacements.Keys)
            requiredTokens.Add(key);

        foreach (var token in requiredTokens)
        {
            if (templateContent.IndexOf(token, StringComparison.Ordinal) >= 0)
                continue;

            throw new InvalidOperationException($"route-fallbacks template '{templatePath}' does not contain required token '{token}'.");
        }
    }

    private static string ApplyRouteFallbackReplacements(
        string templateContent,
        IReadOnlyDictionary<string, string> replacements,
        IReadOnlyDictionary<string, string>? values,
        bool htmlEncode)
    {
        var content = templateContent;
        foreach (var replacement in replacements)
        {
            var resolvedValue = ExpandRouteFallbackTemplate(replacement.Value, values, $"replacement '{replacement.Key}'");
            if (htmlEncode)
                resolvedValue = WebUtility.HtmlEncode(resolvedValue);
            content = content.Replace(replacement.Key, resolvedValue, StringComparison.Ordinal);
        }

        return content;
    }

    private static string ExpandRouteFallbackTemplate(
        string template,
        IReadOnlyDictionary<string, string>? values,
        string context)
    {
        if (string.IsNullOrEmpty(template))
            return string.Empty;

        return Regex.Replace(template, "\\{(?<name>[^{}]+)\\}", match =>
        {
            var name = match.Groups["name"].Value;
            if (values == null || !values.TryGetValue(name, out var value))
                throw new InvalidOperationException($"route-fallbacks {context} references missing value '{name}'.");
            return value ?? string.Empty;
        }, RegexOptions.CultureInvariant);
    }

    private static string NormalizeRouteFallbackRelativePath(string path)
    {
        var normalized = path.Replace('\\', '/').Trim();
        while (normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = normalized.Substring(1);

        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("route-fallbacks produced an empty output path.");

        return normalized;
    }

    private static bool WriteRouteFallbackFile(
        string siteRoot,
        string normalizedSiteRoot,
        string relativePath,
        string content)
    {
        var fullPath = Path.GetFullPath(Path.Combine(siteRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsPathWithinRoot(normalizedSiteRoot, fullPath))
            throw new InvalidOperationException($"route-fallbacks output path must stay under siteRoot: {relativePath}");

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var existing = File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
        if (string.Equals(existing, content, StringComparison.Ordinal))
            return false;

        File.WriteAllText(fullPath, content);
        return true;
    }

    private static void WriteRouteFallbackReport(string reportPath, RouteFallbackReport report)
    {
        var directory = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(reportPath, json);
    }

    private sealed class RouteFallbackReport
    {
        public string SiteRoot { get; set; } = string.Empty;
        public string Template { get; set; } = string.Empty;
        public string ItemsPath { get; set; } = string.Empty;
        public int ItemCount { get; set; }
        public int WrittenCount { get; set; }
        public int ChangedCount { get; set; }
        public List<RouteFallbackReportEntry> Files { get; } = new();
    }

    private sealed class RouteFallbackReportEntry
    {
        public string Path { get; set; } = string.Empty;
        public bool Changed { get; set; }
    }
}
