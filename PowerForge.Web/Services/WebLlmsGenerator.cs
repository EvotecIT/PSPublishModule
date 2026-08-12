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
    /// <summary>Optional project or module manifests used to describe every installable package in a suite.</summary>
    public string[] PackageFiles { get; set; } = Array.Empty<string>();
    /// <summary>Content shape to generate. Site content omits package and installation metadata.</summary>
    public WebLlmsContentKind ContentKind { get; set; } = WebLlmsContentKind.Package;
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
    /// <summary>Optional curated Markdown appended to both llms.txt and llms-full.txt.</summary>
    public string? DiscoveryContentPath { get; set; }
    /// <summary>Optional API detail level for llms-full (none, summary, full).</summary>
    public WebApiDetailLevel ApiDetailLevel { get; set; } = WebApiDetailLevel.None;
    /// <summary>Maximum number of API types to include.</summary>
    public int ApiMaxTypes { get; set; } = 200;
    /// <summary>Maximum number of API members to include when ApiDetailLevel is full.</summary>
    public int ApiMaxMembers { get; set; } = 2000;
}

/// <summary>Generates llms.txt files for documentation consumers.</summary>
public static partial class WebLlmsGenerator
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
        var includePackageContent = options.ContentKind == WebLlmsContentKind.Package;
        var packages = includePackageContent
            ? ResolvePackages(options.PackageFiles)
            : new List<PackageInfo>();
        var name = options.Name ?? projectInfo.Name ?? options.PackageId ?? projectInfo.PackageId ??
                   packages.FirstOrDefault()?.Id ?? Path.GetFileName(siteRoot);
        var packageId = includePackageContent
            ? options.PackageId ?? projectInfo.PackageId ?? name
            : null;
        var version = includePackageContent
            ? options.Version ?? ResolveSuiteVersion(packages) ?? projectInfo.Version ?? "unknown"
            : null;

        var apiCatalogs = ResolveApiCatalogs(options, siteRoot);
        int? typeCount = apiCatalogs.Any(catalog => catalog.TypeCount.HasValue)
            ? apiCatalogs.Sum(catalog => catalog.TypeCount ?? 0)
            : null;

        var llmsTxtPath = Path.Combine(siteRoot, "llms.txt");
        var llmsJsonPath = Path.Combine(siteRoot, "llms.json");
        var llmsFullPath = Path.Combine(siteRoot, "llms-full.txt");

        var primaryPackage = packages.FirstOrDefault();
        var quickstart = ResolveQuickstart(
            options.QuickstartPath,
            primaryPackage?.Id ?? name,
            primaryPackage?.IsPowerShellModule ?? projectInfo.IsPowerShellModule,
            primaryPackage?.IsDotNetTool ?? projectInfo.IsDotNetTool,
            primaryPackage?.ToolCommandName ?? projectInfo.ToolCommandName,
            includePackageContent);
        var legacyInstallCommand = includePackageContent
            ? CreateInstallCommand(packageId!, projectInfo.IsPowerShellModule, projectInfo.IsDotNetTool)
            : null;
        var overview = ResolveOverview(options, projectInfo, siteRoot, name);
        WriteLlmsTxt(llmsTxtPath, name, packageId, version, legacyInstallCommand, packages, typeCount, apiCatalogs, overview, quickstart, options.DiscoveryContentPath, includePackageContent);
        WriteLlmsJson(llmsJsonPath, name, packageId, version, legacyInstallCommand, packages, typeCount, apiCatalogs, quickstart, includePackageContent);
        WriteLlmsFull(llmsFullPath, name, packageId, version, legacyInstallCommand, packages, typeCount, apiCatalogs, overview, quickstart, options, includePackageContent);

        return new WebLlmsResult
        {
            LlmsTxtPath = llmsTxtPath,
            LlmsJsonPath = llmsJsonPath,
            LlmsFullPath = llmsFullPath,
            Name = name,
            PackageId = packageId ?? string.Empty,
            Version = version ?? string.Empty,
            PackageCount = includePackageContent ? packages.Count == 0 ? 1 : packages.Count : 0,
            ApiTypeCount = typeCount,
            ApiCatalogCount = apiCatalogs.Count
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

    private static QuickstartInfo? ResolveQuickstart(
        string? quickstartPath,
        string name,
        bool isPowerShellModule,
        bool isDotNetTool,
        string? toolCommandName,
        bool allowGenerated)
    {
        if (!string.IsNullOrWhiteSpace(quickstartPath))
        {
            var full = Path.GetFullPath(quickstartPath);
            if (!File.Exists(full))
                throw new FileNotFoundException($"LLMS quickstart file not found: {full}", full);

            var text = File.ReadAllText(full).TrimEnd();
            return new QuickstartInfo
            {
                Language = Path.GetExtension(full).Equals(".ps1", StringComparison.OrdinalIgnoreCase)
                    ? "powershell"
                    : InferQuickstartLanguage(full, text),
                Lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            };
        }

        if (allowGenerated && isPowerShellModule)
        {
            return new QuickstartInfo
            {
                Language = "powershell",
                Lines =
                [
                    $"Import-Module {name}",
                    $"Get-Command -Module {name}"
                ]
            };
        }

        if (allowGenerated && isDotNetTool)
        {
            var commandName = string.IsNullOrWhiteSpace(toolCommandName)
                ? name
                : toolCommandName;
            return new QuickstartInfo
            {
                Language = "shell",
                Lines =
                [
                    $"{commandName} --help"
                ]
            };
        }

        return null;
    }

    private static string InferQuickstartLanguage(string path, string content)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)) return "csharp";
        if (extension.Equals(".sh", StringComparison.OrdinalIgnoreCase)) return "shell";
        if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) &&
            (content.Contains("Import-Module", StringComparison.OrdinalIgnoreCase) ||
             content.Contains("Get-Command", StringComparison.OrdinalIgnoreCase)))
            return "powershell";
        return "shell";
    }

    private static string CreateInstallCommand(string packageId, bool isPowerShellModule, bool isDotNetTool)
        => isPowerShellModule
            ? $"Install-Module {packageId}"
            : isDotNetTool
                ? $"dotnet tool install --global {packageId}"
                : $"dotnet add package {packageId}";

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

    private sealed class QuickstartInfo
    {
        public string Language { get; set; } = "csharp";
        public string[] Lines { get; set; } = Array.Empty<string>();
    }
}
