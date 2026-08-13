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
    /// <summary>Controls whether package installation commands are emitted.</summary>
    public WebLlmsInstallCommandPolicy InstallCommandPolicy { get; set; } = WebLlmsInstallCommandPolicy.Declared;
    /// <summary>Optional owner-scoped ecosystem stats catalog used by VerifiedCatalog installation policy.</summary>
    public string? PublicationCatalogPath { get; set; }
    /// <summary>Expected NuGet owner for verified .NET package and tool commands.</summary>
    public string? NuGetOwner { get; set; }
    /// <summary>Expected PowerShell Gallery owner for verified module commands.</summary>
    public string? PowerShellGalleryOwner { get; set; }
    /// <summary>Maximum accepted publication catalog age in hours; zero disables the age check.</summary>
    public int PublicationCatalogMaxAgeHours { get; set; }
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
        if (!Enum.IsDefined(options.ContentKind))
            throw new ArgumentOutOfRangeException(
                nameof(options.ContentKind),
                options.ContentKind,
                "Unsupported LLMS content kind. Expected Package or Site.");
        if (!Enum.IsDefined(options.InstallCommandPolicy))
            throw new ArgumentOutOfRangeException(
                nameof(options.InstallCommandPolicy),
                options.InstallCommandPolicy,
                "Unsupported LLMS install command policy. Expected Declared, VerifiedCatalog, or None.");
        if (options.PublicationCatalogMaxAgeHours < 0)
            throw new ArgumentOutOfRangeException(
                nameof(options.PublicationCatalogMaxAgeHours),
                options.PublicationCatalogMaxAgeHours,
                "Publication catalog maximum age cannot be negative.");
        if (!Enum.IsDefined(options.ApiDetailLevel))
            throw new ArgumentOutOfRangeException(
                nameof(options.ApiDetailLevel),
                options.ApiDetailLevel,
                "Unsupported LLMS API detail level. Expected None, Summary, or Full.");

        var siteRoot = Path.GetFullPath(options.SiteRoot);
        if (!Directory.Exists(siteRoot))
            throw new DirectoryNotFoundException($"Site root not found: {siteRoot}");

        var includePackageContent = options.ContentKind == WebLlmsContentKind.Package;
        var configuredName = string.IsNullOrWhiteSpace(options.Name) ? null : options.Name.Trim();
        var configuredPackageId = string.IsNullOrWhiteSpace(options.PackageId) ? null : options.PackageId.Trim();
        var configuredVersion = string.IsNullOrWhiteSpace(options.Version) ? null : options.Version.Trim();
        var requireInstallMetadata = options.InstallCommandPolicy != WebLlmsInstallCommandPolicy.None;
        var packages = includePackageContent
            ? ResolvePackages(options.PackageFiles, requireInstallMetadata)
            : new List<PackageInfo>();
        var primaryPackage = packages.FirstOrDefault();
        var useProjectPackageMetadata = includePackageContent && primaryPackage is null;
        var projectInfo = ReadProjectInfo(
            options.ProjectFile,
            useProjectPackageMetadata,
            requirePackageId: useProjectPackageMetadata && configuredPackageId is null,
            requireVersion: useProjectPackageMetadata && configuredVersion is null,
            requireInstallMetadata: requireInstallMetadata);
        var name = includePackageContent
            ? configuredName ?? configuredPackageId ?? primaryPackage?.Id ?? projectInfo.PackageId ?? projectInfo.Name
            : configuredName ?? TryReadNameFromHomepage(siteRoot) ?? projectInfo.Name;
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("LLMS content name could not be resolved. Configure name or provide usable project/homepage metadata.");
        var packageId = includePackageContent
            ? configuredPackageId ?? primaryPackage?.Id ?? projectInfo.PackageId ?? name
            : null;
        var version = includePackageContent
            ? configuredVersion ?? ResolveSuiteVersion(packages) ?? projectInfo.Version ?? "unknown"
            : null;

        var apiCatalogs = ResolveApiCatalogs(options, siteRoot);
        int? typeCount = apiCatalogs.Any(catalog => catalog.TypeCount.HasValue)
            ? apiCatalogs.Sum(catalog => catalog.TypeCount ?? 0)
            : null;

        var llmsTxtPath = Path.Combine(siteRoot, "llms.txt");
        var llmsJsonPath = Path.Combine(siteRoot, "llms.json");
        var llmsFullPath = Path.Combine(siteRoot, "llms-full.txt");

        var quickstart = ResolveQuickstart(
            options.QuickstartPath,
            primaryPackage?.Id ?? name,
            primaryPackage?.IsPowerShellModule ?? projectInfo.IsPowerShellModule,
            primaryPackage?.IsDotNetTool ?? projectInfo.IsDotNetTool,
            primaryPackage?.ToolCommandName ?? projectInfo.ToolCommandName,
            includePackageContent);
        var legacyInstallCommand = includePackageContent
            ? CreateInstallCommand(
                packageId!,
                primaryPackage?.IsPowerShellModule ?? projectInfo.IsPowerShellModule,
                primaryPackage?.IsDotNetTool ?? projectInfo.IsDotNetTool)
            : null;
        var installCommandCount = ApplyInstallCommandPolicy(
            options,
            packages,
            packageId,
            version,
            projectInfo.IsPowerShellModule,
            ref legacyInstallCommand);
        var overview = ResolveOverview(options, projectInfo, siteRoot, name, apiCatalogs.Count > 0);
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
            InstallCommandCount = installCommandCount,
            ApiTypeCount = typeCount,
            ApiCatalogCount = apiCatalogs.Count
        };
    }

    private static string ResolveOverview(
        WebLlmsOptions options,
        ProjectInfo projectInfo,
        string siteRoot,
        string name,
        bool hasApiCatalogs)
    {
        if (!string.IsNullOrWhiteSpace(options.Overview))
            return options.Overview.Trim();

        if (!string.IsNullOrWhiteSpace(projectInfo.Description))
            return projectInfo.Description!;

        if (TryReadOverviewFromHomepage(siteRoot, out var homepageOverview))
            return homepageOverview;

        if (!hasApiCatalogs)
            return options.ContentKind == WebLlmsContentKind.Site
                ? $"{name} website."
                : $"{name} documentation.";

        return $"{name} documentation site and API reference.";
    }

    private static List<ApiCatalogInfo> ResolveApiCatalogs(WebLlmsOptions options, string siteRoot)
    {
        var configuredPaths = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.ApiIndexPath))
            configuredPaths.Add(options.ApiIndexPath);
        configuredPaths.AddRange(options.ApiIndexPaths.Where(path => !string.IsNullOrWhiteSpace(path)));

        if (configuredPaths.Count == 0)
        {
            var defaultIndexPath = Path.Combine(siteRoot, "api", "index.json");
            if (File.Exists(defaultIndexPath))
                configuredPaths.Add(defaultIndexPath);
        }

        var fullPaths = configuredPaths
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var multipleCatalogs = fullPaths.Length > 1;
        var catalogs = new List<ApiCatalogInfo>(fullPaths.Length);
        foreach (var fullPath in fullPaths)
        {
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"Configured LLMS API index not found: {fullPath}", fullPath);

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
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Configured LLMS API index is not valid JSON: {fullPath}", ex);
        }

        return catalog;
    }

    private static string InferApiBase(string siteRoot, string apiIndexPath)
    {
        var directory = Path.GetDirectoryName(apiIndexPath) ?? siteRoot;
        var relative = Path.GetRelativePath(siteRoot, directory).Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(relative))
            return "/api";
        if (relative.StartsWith("..", StringComparison.Ordinal))
            throw new InvalidOperationException($"Cannot infer a published API route for external catalog '{apiIndexPath}'. Place multiple API indexes under the site root.");
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

    private static string? TryReadNameFromHomepage(string siteRoot)
    {
        var indexPath = Path.Combine(siteRoot, "index.html");
        if (!File.Exists(indexPath))
            return null;

        string html;
        try
        {
            html = File.ReadAllText(indexPath);
        }
        catch
        {
            return null;
        }

        foreach (var pattern in new[]
                 {
                     @"<title\b[^>]*>(?<content>.*?)</title>",
                     @"<h1\b[^>]*>(?<content>.*?)</h1>"
                 })
        {
            var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!match.Success) continue;
            var name = NormalizeHtmlSnippet(match.Groups["content"].Value);
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }

        return null;
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
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidDataException($"LLMS quickstart file is empty: {full}");
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
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".cs" or ".csx") return "csharp";
        if (extension is ".ps1" or ".psm1" or ".psd1") return "powershell";
        if (extension is ".sh" or ".bash" or ".zsh") return "shell";

        if (content.Contains("Import-Module", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("Get-Command", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(content, @"(?m)^\s*[A-Z][A-Za-z0-9]*-[A-Z][A-Za-z0-9]*\b"))
            return "powershell";

        if (Regex.IsMatch(content, @"(?m)^\s*(using\s+[A-Za-z_][A-Za-z0-9_.]*\s*;|namespace\s+[A-Za-z_]|(?:var|await|new)\s+.+;)"))
            return "csharp";

        if (content.StartsWith("#!", StringComparison.Ordinal) ||
            Regex.IsMatch(content, @"(?m)^\s*\S+\s+--?[A-Za-z0-9]") ||
            Regex.IsMatch(content, @"(?m)^\s*(dotnet|npm|npx|pnpm|yarn|node|python3?|pip3?|bash|sh|pwsh|powershell|curl|wget|git|docker|kubectl|helm)\s+\S+", RegexOptions.IgnoreCase))
            return "shell";

        // Preserve the historical non-PowerShell fallback for ambiguous curated snippets.
        return "csharp";
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
