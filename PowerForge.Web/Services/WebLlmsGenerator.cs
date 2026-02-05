using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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

        var apiIndexPath = options.ApiIndexPath;
        if (string.IsNullOrWhiteSpace(apiIndexPath))
            apiIndexPath = Path.Combine(siteRoot, "api", "index.json");
        var typeCount = ReadApiTypeCount(apiIndexPath);

        var llmsTxtPath = Path.Combine(siteRoot, "llms.txt");
        var llmsJsonPath = Path.Combine(siteRoot, "llms.json");
        var llmsFullPath = Path.Combine(siteRoot, "llms-full.txt");

        var quickstart = ResolveQuickstart(options.QuickstartPath, name);
        var overview = options.Overview ?? $"{name} is a zero-dependency library for generating and decoding QR codes, barcodes, and 2D matrix codes.";
        var apiBase = string.IsNullOrWhiteSpace(options.ApiBase) ? "/api" : options.ApiBase;

        WriteLlmsTxt(llmsTxtPath, name, packageId, version, typeCount, apiBase, quickstart);
        WriteLlmsJson(llmsJsonPath, name, packageId, version, apiBase, quickstart);
        WriteLlmsFull(llmsFullPath, name, packageId, version, typeCount, apiBase, overview, quickstart, options);

        return new WebLlmsResult
        {
            LlmsTxtPath = llmsTxtPath,
            LlmsJsonPath = llmsJsonPath,
            LlmsFullPath = llmsFullPath,
            Name = name,
            PackageId = packageId,
            Version = version,
            ApiTypeCount = typeCount
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
            Version = NormalizeEmpty(MatchValue(content, "Version")) ?? NormalizeEmpty(MatchValue(content, "VersionPrefix"))
        };
    }

    private static int? ReadApiTypeCount(string? apiIndexPath)
    {
        if (string.IsNullOrWhiteSpace(apiIndexPath)) return null;
        var full = Path.GetFullPath(apiIndexPath);
        if (!File.Exists(full)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(full));
            if (doc.RootElement.TryGetProperty("typeCount", out var count) && count.TryGetInt32(out var value))
                return value;
        }
        catch
        {
            return null;
        }

        return null;
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
        string apiBase,
        string[] quickstart)
    {
        var lines = new List<string>
        {
            $"# {name}",
            $"Version: {version}",
            $"Package: {packageId}"
        };
        if (typeCount.HasValue) lines.Add($"API types: {typeCount.Value}");
        lines.Add(string.Empty);
        lines.Add("Install:");
        lines.Add($"- dotnet add package {packageId}");
        lines.Add(string.Empty);
        lines.Add("Quickstart:");
        lines.Add("```csharp");
        lines.AddRange(quickstart);
        lines.Add("```");
        lines.Add(string.Empty);
        lines.Add("Machine-friendly API data:");
        lines.Add($"- {apiBase.TrimEnd('/')}/index.json");
        lines.Add($"- {apiBase.TrimEnd('/')}/search.json");
        lines.Add($"- {apiBase.TrimEnd('/')}/types/{{slug}}.json");
        lines.Add(string.Empty);
        lines.Add("Slug rule: lower-case, dots/symbols -> dashes.");

        File.WriteAllText(path, string.Join(Environment.NewLine, lines), Encoding.UTF8);
    }

    private static void WriteLlmsJson(
        string path,
        string name,
        string packageId,
        string version,
        string apiBase,
        string[] quickstart)
    {
        var payload = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["version"] = version,
            ["package"] = packageId,
            ["install"] = new[] { $"dotnet add package {packageId}" },
            ["quickstart"] = quickstart.Where(l => l != null).ToArray(),
            ["api"] = new Dictionary<string, object?>
            {
                ["index"] = $"{apiBase.TrimEnd('/')}/index.json",
                ["search"] = $"{apiBase.TrimEnd('/')}/search.json",
                ["type"] = $"{apiBase.TrimEnd('/')}/types/{{slug}}.json",
                ["slugRule"] = "lower-case, dots/symbols -> dashes"
            }
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    private static void WriteLlmsFull(
        string path,
        string name,
        string packageId,
        string version,
        int? typeCount,
        string apiBase,
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
        lines.Add($"- {apiBase.TrimEnd('/')}/index.json");
        lines.Add($"- {apiBase.TrimEnd('/')}/search.json");
        lines.Add($"- {apiBase.TrimEnd('/')}/types/{{slug}}.json");

        AppendApiDetails(lines, options, apiBase);

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

    private static void AppendApiDetails(List<string> lines, WebLlmsOptions options, string apiBase)
    {
        if (options.ApiDetailLevel == WebApiDetailLevel.None)
            return;

        var indexPath = options.ApiIndexPath;
        if (string.IsNullOrWhiteSpace(indexPath))
            indexPath = Path.Combine(options.SiteRoot, "api", "index.json");
        var fullIndexPath = Path.GetFullPath(indexPath);
        if (!File.Exists(fullIndexPath))
            return;

        var entries = ReadApiIndex(fullIndexPath);
        if (entries.Count == 0)
            return;

        var maxTypes = options.ApiMaxTypes <= 0 ? entries.Count : Math.Min(options.ApiMaxTypes, entries.Count);
        var maxMembers = options.ApiMaxMembers <= 0 ? int.MaxValue : options.ApiMaxMembers;
        var typesDir = Path.Combine(Path.GetDirectoryName(fullIndexPath) ?? ".", "types");

        lines.Add(string.Empty);
        lines.Add("## API Summary");

        for (var i = 0; i < maxTypes; i++)
        {
            var entry = entries[i];
            var summary = string.IsNullOrWhiteSpace(entry.Summary) ? string.Empty : $" — {entry.Summary}";
            lines.Add($"- {entry.FullName}{summary}");
        }

        if (options.ApiDetailLevel != WebApiDetailLevel.Full)
            return;

        lines.Add(string.Empty);
        lines.Add("## API Members");
        foreach (var entry in entries.Take(maxTypes))
        {
            if (maxMembers <= 0) break;
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
    }
}
