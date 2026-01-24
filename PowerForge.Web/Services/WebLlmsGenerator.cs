using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

public sealed class WebLlmsOptions
{
    public string SiteRoot { get; set; } = ".";
    public string? ProjectFile { get; set; }
    public string? ApiIndexPath { get; set; }
    public string ApiBase { get; set; } = "/api";
    public string? Name { get; set; }
    public string? PackageId { get; set; }
    public string? Version { get; set; }
    public string? QuickstartPath { get; set; }
    public string? Overview { get; set; }
    public string? License { get; set; }
    public string? Targets { get; set; }
    public string? ExtraContentPath { get; set; }
}

public static class WebLlmsGenerator
{
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

    private sealed class ProjectInfo
    {
        public string? Name { get; set; }
        public string? PackageId { get; set; }
        public string? Version { get; set; }
    }
}
