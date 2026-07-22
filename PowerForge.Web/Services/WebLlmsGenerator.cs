using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net;

namespace PowerForge.Web;

/// <summary>Options for llms.txt generation.</summary>
public sealed class WebLlmsOptions
{
    /// <summary>Root directory of the site.</summary>
    public string SiteRoot { get; set; } = ".";
    /// <summary>Optional project file for metadata lookup.</summary>
    public string? ProjectFile { get; set; }
    /// <summary>Optional API index path.</summary>
    public string? ApiIndexPath { get; set; }
    /// <summary>Optional API index paths for sites that publish more than one API catalog.</summary>
    public string[] ApiIndexPaths { get; set; } = Array.Empty<string>();
    /// <summary>Base URL for API docs.</summary>
    public string ApiBase { get; set; } = "/api";
    /// <summary>Optional project name override.</summary>
    public string? Name { get; set; }
    /// <summary>Optional package identifier override.</summary>
    public string? PackageId { get; set; }
    /// <summary>Optional version override.</summary>
    public string? Version { get; set; }
    /// <summary>Optional quickstart snippet path.</summary>
    public string? QuickstartPath { get; set; }
    /// <summary>Optional overview text.</summary>
    public string? Overview { get; set; }
    /// <summary>Optional license text.</summary>
    public string? License { get; set; }
    /// <summary>Optional target framework list.</summary>
    public string? Targets { get; set; }
    /// <summary>Optional path to extra content for llms-full.</summary>
    public string? ExtraContentPath { get; set; }
    /// <summary>Optional API detail level for llms-full (none, summary, full).</summary>
    public WebApiDetailLevel ApiDetailLevel { get; set; } = WebApiDetailLevel.None;
    /// <summary>Maximum number of API types to include.</summary>
    public int ApiMaxTypes { get; set; } = 200;
    /// <summary>Maximum number of API members to include when ApiDetailLevel is full.</summary>
    public int ApiMaxMembers { get; set; } = 2000;
}

/// <summary>Generates llms.txt files for documentation consumers.</summary>
public static class WebLlmsGenerator
{
    /// <summary>Generates llms.txt, llms.json, and llms-full.txt.</summary>
    /// <param name="options">Generation options.</param>
    /// <returns>Result payload describing generated outputs.</returns>
    public static WebLlmsResult Generate(WebLlmsOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));

        var siteRoot = Path.GetFullPath(options.SiteRoot);
        if (!Directory.Exists(siteRoot))
            throw new DirectoryNotFoundException($"Site root not found: {siteRoot}");

        var projectInfo = ReadProjectInfo(options.ProjectFile);
        var name = options.Name ?? projectInfo.Name ?? options.PackageId ?? projectInfo.PackageId ?? Path.GetFileName(siteRoot);
        var packageId = options.PackageId ?? projectInfo.PackageId ?? name;
        var version = options.Version ?? projectInfo.Version ?? "unknown";

        var apiCatalogs = ResolveApiCatalogs(options, siteRoot);
        int? typeCount = apiCatalogs.Any(catalog => catalog.TypeCount.HasValue)
            ? apiCatalogs.Sum(catalog => catalog.TypeCount ?? 0)
            : null;

        var llmsTxtPath = Path.Combine(siteRoot, "llms.txt");
        var llmsJsonPath = Path.Combine(siteRoot, "llms.json");
        var llmsFullPath = Path.Combine(siteRoot, "llms-full.txt");

        var quickstart = ResolveQuickstart(options.QuickstartPath, name);
        var overview = ResolveOverview(options, projectInfo, siteRoot, name);
        WriteLlmsTxt(llmsTxtPath, name, packageId, version, typeCount, apiCatalogs, overview, quickstart);
        WriteLlmsJson(llmsJsonPath, name, packageId, version, typeCount, apiCatalogs, quickstart);
        WriteLlmsFull(llmsFullPath, name, packageId, version, typeCount, apiCatalogs, overview, quickstart, options);

        return new WebLlmsResult
        {
            LlmsTxtPath = llmsTxtPath,
            LlmsJsonPath = llmsJsonPath,
            LlmsFullPath = llmsFullPath,
            Name = name,
            PackageId = packageId,
            Version = version,
            ApiTypeCount = typeCount,
            ApiCatalogCount = apiCatalogs.Count
        };
    }

    private static ProjectInfo ReadProjectInfo(string? projectFile)
    {
        if (string.IsNullOrWhiteSpace(projectFile))
            return new ProjectInfo();

        var full = Path.GetFullPath(projectFile);
        if (!File.Exists(full))
            return new ProjectInfo();

        var content = File.ReadAllText(full);
        return new ProjectInfo
        {
            Name = NormalizeEmpty(MatchValue(content, "AssemblyName")) ?? NormalizeEmpty(MatchValue(content, "RootNamespace")),
            PackageId = NormalizeEmpty(MatchValue(content, "PackageId")),
            Version = NormalizeEmpty(MatchValue(content, "Version")) ?? NormalizeEmpty(MatchValue(content, "VersionPrefix")),
            Description = NormalizeEmpty(MatchValue(content, "Description"))
        };
    }

    private static string ResolveOverview(WebLlmsOptions options, ProjectInfo projectInfo, string siteRoot, string name)
    {
        if (!string.IsNullOrWhiteSpace(options.Overview))
            return options.Overview.Trim();

        if (!string.IsNullOrWhiteSpace(projectInfo.Description))
            return projectInfo.Description!;

        if (TryReadOverviewFromHomepage(siteRoot, out var homepageOverview))
            return homepageOverview;

        return $"{name} documentation site and API reference.";
    }

    private static List<ApiCatalogInfo> ResolveApiCatalogs(WebLlmsOptions options, string siteRoot)
    {
        var configuredPaths = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.ApiIndexPath))
            configuredPaths.Add(options.ApiIndexPath);
        configuredPaths.AddRange(options.ApiIndexPaths.Where(path => !string.IsNullOrWhiteSpace(path)));

        if (configuredPaths.Count == 0)
            configuredPaths.Add(Path.Combine(siteRoot, "api", "index.json"));

        var fullPaths = configuredPaths
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var multipleCatalogs = fullPaths.Length > 1;
        var catalogs = new List<ApiCatalogInfo>(fullPaths.Length);
        foreach (var fullPath in fullPaths)
        {
            var apiBase = multipleCatalogs
                ? InferApiBase(siteRoot, fullPath)
                : NormalizeApiBase(options.ApiBase);
            catalogs.Add(ReadApiCatalog(fullPath, apiBase));
        }

        return catalogs;
    }

    private static ApiCatalogInfo ReadApiCatalog(string fullPath, string apiBase)
    {
        var catalog = new ApiCatalogInfo
        {
            IndexPath = fullPath,
            ApiBase = apiBase,
            Name = Path.GetFileName(Path.GetDirectoryName(fullPath)) ?? "API"
        };
        if (!File.Exists(fullPath)) return catalog;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(fullPath));
            if (doc.RootElement.TryGetProperty("typeCount", out var count) && count.TryGetInt32(out var value))
                catalog.TypeCount = value;
            if (doc.RootElement.TryGetProperty("assembly", out var assembly) &&
                assembly.ValueKind == JsonValueKind.Object)
            {
                var assemblyName = ReadString(assembly, "assemblyName");
                if (!string.IsNullOrWhiteSpace(assemblyName))
                    catalog.Name = assemblyName;
            }
            else
            {
                var title = ReadString(doc.RootElement, "title");
                if (!string.IsNullOrWhiteSpace(title))
                    catalog.Name = Regex.Replace(title, @"\s+(API|Cmdlet)\s+Reference$", string.Empty, RegexOptions.IgnoreCase);
            }
        }
        catch
        {
            // Keep the catalog link even when optional metadata cannot be read.
        }

        return catalog;
    }

    private static string InferApiBase(string siteRoot, string apiIndexPath)
    {
        var directory = Path.GetDirectoryName(apiIndexPath) ?? siteRoot;
        var relative = Path.GetRelativePath(siteRoot, directory).Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(relative) || relative.StartsWith("..", StringComparison.Ordinal))
            return "/api";
        return "/" + relative;
    }

    private static string NormalizeApiBase(string? apiBase)
    {
        var normalized = string.IsNullOrWhiteSpace(apiBase) ? "/api" : apiBase.Trim();
        return normalized.Length > 1 ? normalized.TrimEnd('/') : normalized;
    }

    private static string MatchValue(string content, string name)
    {
        var pattern = $@"<{name}>([^<]+)</{name}>";
        var match = Regex.Match(content, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    private static string? NormalizeEmpty(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool TryReadOverviewFromHomepage(string siteRoot, out string overview)
    {
        overview = string.Empty;

        var indexPath = Path.Combine(siteRoot, "index.html");
        if (!File.Exists(indexPath))
            return false;

        string html;
        try
        {
            html = File.ReadAllText(indexPath);
        }
        catch
        {
            return false;
        }

        var description =
            TryMatchMetaContent(html, "name", "description") ??
            TryMatchMetaContent(html, "property", "og:description") ??
            TryMatchMetaContent(html, "name", "twitter:description");
        if (!string.IsNullOrWhiteSpace(description))
        {
            overview = description;
            return true;
        }

        var heading = TryMatchTagContent(html, "h1");
        if (!string.IsNullOrWhiteSpace(heading))
        {
            overview = heading;
            return true;
        }

        var title = TryMatchTagContent(html, "title");
        if (!string.IsNullOrWhiteSpace(title))
        {
            overview = title;
            return true;
        }

        return false;
    }

    private static string? TryMatchMetaContent(string html, string attributeName, string attributeValue)
    {
        var pattern = $@"<meta\b[^>]*\b{attributeName}\s*=\s*[""']{Regex.Escape(attributeValue)}[""'][^>]*\bcontent\s*=\s*[""'](?<content>.*?)[""'][^>]*>";
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!match.Success)
            return null;

        return NormalizeHtmlSnippet(match.Groups["content"].Value);
    }

    private static string? TryMatchTagContent(string html, string tagName)
    {
        var pattern = $@"<{tagName}\b[^>]*>(?<content>.*?)</{tagName}>";
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!match.Success)
            return null;

        return NormalizeHtmlSnippet(match.Groups["content"].Value);
    }

    private static string NormalizeHtmlSnippet(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var withoutTags = Regex.Replace(value, "<.*?>", " ", RegexOptions.Singleline);
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }

    private static string[] ResolveQuickstart(string? quickstartPath, string name)
    {
        if (!string.IsNullOrWhiteSpace(quickstartPath))
        {
            var full = Path.GetFullPath(quickstartPath);
            if (File.Exists(full))
            {
                var text = File.ReadAllText(full).TrimEnd();
                return text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            }
        }

        return new[]
        {
            $"using {name};",
            string.Empty,
            "// TODO: add quickstart snippet"
        };
    }

    private static void WriteLlmsTxt(
        string path,
        string name,
        string packageId,
        string version,
        int? typeCount,
        IReadOnlyList<ApiCatalogInfo> apiCatalogs,
        string overview,
        string[] quickstart)
    {
        var lines = new List<string>
        {
            $"# {name}",
            string.Empty,
            $"> {overview}",
            string.Empty,
            "## Metadata",
            $"- Version: {version}",
            $"- Package: {packageId}"
        };
        if (typeCount.HasValue) lines.Add($"- API types: {typeCount.Value}");
        if (apiCatalogs.Count > 1) lines.Add($"- API catalogs: {apiCatalogs.Count}");
        lines.Add(string.Empty);
        lines.Add("## Install");
        lines.Add($"- dotnet add package {packageId}");
        lines.Add(string.Empty);
        lines.Add("## Quickstart");
        lines.Add("```csharp");
        lines.AddRange(quickstart);
        lines.Add("```");
        lines.Add(string.Empty);
        lines.Add("## Machine-friendly API data");
        AppendApiResourceLinks(lines, apiCatalogs);
        lines.Add(string.Empty);
        lines.Add("Slug rule: lower-case, dots/symbols -> dashes.");

        File.WriteAllText(path, string.Join(Environment.NewLine, lines), Encoding.UTF8);
    }

    private static void WriteLlmsJson(
        string path,
        string name,
        string packageId,
        string version,
        int? typeCount,
        IReadOnlyList<ApiCatalogInfo> apiCatalogs,
        string[] quickstart)
    {
        var payload = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["version"] = version,
            ["package"] = packageId,
            ["install"] = new[] { $"dotnet add package {packageId}" },
            ["quickstart"] = quickstart.Where(l => l != null).ToArray(),
            ["apiTypeCount"] = typeCount
        };
        if (apiCatalogs.Count == 1)
            payload["api"] = CreateApiResourcePayload(apiCatalogs[0]);
        else
            payload["apiCatalogs"] = apiCatalogs.Select(catalog => new Dictionary<string, object?>
            {
                ["name"] = catalog.Name,
                ["typeCount"] = catalog.TypeCount,
                ["resources"] = CreateApiResourcePayload(catalog)
            }).ToArray();

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    private static void WriteLlmsFull(
        string path,
        string name,
        string packageId,
        string version,
        int? typeCount,
        IReadOnlyList<ApiCatalogInfo> apiCatalogs,
        string overview,
        string[] quickstart,
        WebLlmsOptions options)
    {
        var lines = new List<string>
        {
            $"# {name} - Complete AI Context",
            string.Empty,
            "## Overview",
            overview,
            $"- Package: {packageId}",
            $"- Version: {version}"
        };
        if (typeCount.HasValue) lines.Add($"- API types: {typeCount.Value}");
        if (apiCatalogs.Count > 1) lines.Add($"- API catalogs: {apiCatalogs.Count}");
        if (!string.IsNullOrWhiteSpace(options.License)) lines.Add($"- License: {options.License}");
        if (!string.IsNullOrWhiteSpace(options.Targets)) lines.Add($"- Targets: {options.Targets}");

        lines.Add(string.Empty);
        lines.Add("## Installation");
        lines.Add("```");
        lines.Add($"dotnet add package {packageId}");
        lines.Add("```");
        lines.Add(string.Empty);
        lines.Add("## Quickstart");
        lines.Add("```csharp");
        lines.AddRange(quickstart);
        lines.Add("```");
        lines.Add(string.Empty);
        lines.Add("## API Resources");
        AppendApiResourceLinks(lines, apiCatalogs);

        AppendApiDetails(lines, options, apiCatalogs);

        if (!string.IsNullOrWhiteSpace(options.ExtraContentPath))
        {
            var extraPath = Path.GetFullPath(options.ExtraContentPath);
            if (File.Exists(extraPath))
            {
                lines.Add(string.Empty);
                lines.AddRange(File.ReadAllLines(extraPath));
            }
        }

        File.WriteAllText(path, string.Join(Environment.NewLine, lines), Encoding.UTF8);
    }

    private static void AppendApiResourceLinks(List<string> lines, IReadOnlyList<ApiCatalogInfo> apiCatalogs)
    {
        foreach (var catalog in apiCatalogs)
        {
            var label = apiCatalogs.Count > 1 ? $"{catalog.Name} " : string.Empty;
            lines.Add($"- [{label}API index]({catalog.ApiBase}/index.json): Type and package metadata.");
            lines.Add($"- [{label}API search]({catalog.ApiBase}/search.json): Searchable API data.");
            lines.Add($"- [{label}API type template]({catalog.ApiBase}/types/{{slug}}.json): Per-type API details.");
        }
    }

    private static Dictionary<string, object?> CreateApiResourcePayload(ApiCatalogInfo catalog)
    {
        return new Dictionary<string, object?>
        {
            ["index"] = $"{catalog.ApiBase}/index.json",
            ["search"] = $"{catalog.ApiBase}/search.json",
            ["type"] = $"{catalog.ApiBase}/types/{{slug}}.json",
            ["slugRule"] = "lower-case, dots/symbols -> dashes"
        };
    }

    private static void AppendApiDetails(List<string> lines, WebLlmsOptions options, IReadOnlyList<ApiCatalogInfo> apiCatalogs)
    {
        if (options.ApiDetailLevel == WebApiDetailLevel.None)
            return;

        var catalogEntries = apiCatalogs
            .Select(catalog => (Catalog: catalog, Entries: ReadApiIndex(catalog.IndexPath)))
            .Where(item => item.Entries.Count > 0)
            .ToArray();
        if (catalogEntries.Length == 0)
            return;

        var remainingTypes = options.ApiMaxTypes <= 0 ? int.MaxValue : options.ApiMaxTypes;
        var maxMembers = options.ApiMaxMembers <= 0 ? int.MaxValue : options.ApiMaxMembers;
        var selectedEntries = new List<(ApiCatalogInfo Catalog, ApiIndexEntry Entry)>();

        lines.Add(string.Empty);
        lines.Add("## API Summary");
        foreach (var (catalog, entries) in catalogEntries)
        {
            if (remainingTypes <= 0) break;
            if (catalogEntries.Length > 1)
                lines.Add($"### {catalog.Name}");
            foreach (var entry in entries.Take(remainingTypes))
            {
                var summary = string.IsNullOrWhiteSpace(entry.Summary) ? string.Empty : $" — {entry.Summary}";
                lines.Add($"- {entry.FullName}{summary}");
                selectedEntries.Add((catalog, entry));
                remainingTypes--;
                if (remainingTypes <= 0) break;
            }
        }

        if (options.ApiDetailLevel != WebApiDetailLevel.Full)
            return;

        lines.Add(string.Empty);
        lines.Add("## API Members");
        foreach (var (catalog, entry) in selectedEntries)
        {
            if (maxMembers <= 0) break;
            var typesDir = Path.Combine(Path.GetDirectoryName(catalog.IndexPath) ?? ".", "types");
            var typePath = Path.Combine(typesDir, $"{entry.Slug}.json");
            if (!File.Exists(typePath)) continue;

            var detail = ReadApiTypeDetail(typePath);
            lines.Add(string.Empty);
            lines.Add($"### {entry.FullName}");
            if (!string.IsNullOrWhiteSpace(entry.Summary))
                lines.Add(entry.Summary);

            maxMembers = AppendMemberLines(lines, "Methods", detail.Methods, maxMembers);
            maxMembers = AppendMemberLines(lines, "Properties", detail.Properties, maxMembers);
            maxMembers = AppendMemberLines(lines, "Fields", detail.Fields, maxMembers);
            maxMembers = AppendMemberLines(lines, "Events", detail.Events, maxMembers);
        }
    }

    private static int AppendMemberLines(List<string> lines, string title, List<ApiMemberEntry> members, int remaining)
    {
        if (remaining <= 0 || members.Count == 0)
            return remaining;

        lines.Add(string.Empty);
        lines.Add($"#### {title}");
        foreach (var member in members)
        {
            if (remaining <= 0) break;
            var summary = string.IsNullOrWhiteSpace(member.Summary) ? string.Empty : $" — {member.Summary}";
            var signature = string.IsNullOrWhiteSpace(member.Signature) ? member.Name : member.Signature;
            lines.Add($"- {signature}{summary}");
            remaining--;
        }
        return remaining;
    }

    private static List<ApiIndexEntry> ReadApiIndex(string indexPath)
    {
        var results = new List<ApiIndexEntry>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(indexPath));
            if (!doc.RootElement.TryGetProperty("types", out var types) || types.ValueKind != JsonValueKind.Array)
                return results;
            foreach (var item in types.EnumerateArray())
            {
                var entry = new ApiIndexEntry
                {
                    Name = ReadString(item, "name"),
                    FullName = ReadString(item, "fullName"),
                    Summary = ReadString(item, "summary"),
                    Kind = ReadString(item, "kind"),
                    Slug = ReadString(item, "slug")
                };
                if (!string.IsNullOrWhiteSpace(entry.FullName))
                    results.Add(entry);
            }
        }
        catch
        {
            return results;
        }
        return results;
    }

    private static ApiTypeDetail ReadApiTypeDetail(string typePath)
    {
        var detail = new ApiTypeDetail();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(typePath));
            detail.Methods = ReadMemberArray(doc.RootElement, "methods");
            detail.Properties = ReadMemberArray(doc.RootElement, "properties");
            detail.Fields = ReadMemberArray(doc.RootElement, "fields");
            detail.Events = ReadMemberArray(doc.RootElement, "events");
        }
        catch
        {
            return detail;
        }
        return detail;
    }

    private static List<ApiMemberEntry> ReadMemberArray(JsonElement root, string name)
    {
        var list = new List<ApiMemberEntry>();
        if (!root.TryGetProperty(name, out var members) || members.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var item in members.EnumerateArray())
        {
            var entry = new ApiMemberEntry
            {
                Name = ReadString(item, "name"),
                Summary = ReadString(item, "summary"),
                Signature = ReadString(item, "signature")
            };
            if (!string.IsNullOrWhiteSpace(entry.Name))
                list.Add(entry);
        }
        return list;
    }

    private static string ReadString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private sealed class ApiIndexEntry
    {
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
    }

    private sealed class ApiCatalogInfo
    {
        public string IndexPath { get; set; } = string.Empty;
        public string ApiBase { get; set; } = "/api";
        public string Name { get; set; } = "API";
        public int? TypeCount { get; set; }
    }

    private sealed class ApiTypeDetail
    {
        public List<ApiMemberEntry> Methods { get; set; } = new();
        public List<ApiMemberEntry> Properties { get; set; } = new();
        public List<ApiMemberEntry> Fields { get; set; } = new();
        public List<ApiMemberEntry> Events { get; set; } = new();
    }

    private sealed class ApiMemberEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
    }

    private sealed class ProjectInfo
    {
        public string? Name { get; set; }
        public string? PackageId { get; set; }
        public string? Version { get; set; }
        public string? Description { get; set; }
    }
}
