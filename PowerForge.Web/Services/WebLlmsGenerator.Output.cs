using System.Text;
using System.Text.Json;

namespace PowerForge.Web;

public static partial class WebLlmsGenerator
{
    private const string ApiSlugRule = "lower-case; remove generic arity markers (one or two backticks followed by digits); replace remaining non-alphanumerics with dashes; collapse and trim dashes";

    private static void WriteLlmsTxt(
        string path,
        string name,
        string? packageId,
        string? version,
        string? legacyInstallCommand,
        IReadOnlyList<PackageInfo> packages,
        int? typeCount,
        IReadOnlyList<ApiCatalogInfo> apiCatalogs,
        string overview,
        QuickstartInfo? quickstart,
        string? discoveryContentPath,
        bool includePackageContent)
    {
        var lines = new List<string>
        {
            $"# {name}",
            string.Empty,
            $"> {overview}",
            string.Empty
        };
        if (includePackageContent)
        {
            lines.Add("## Metadata");
            if (packages.Count == 0)
            {
                lines.Add($"- Version: {version}");
                lines.Add($"- Package: {packageId}");
            }
            else
            {
                lines.Add($"- Packages: {packages.Count}");
                lines.Add($"- Suite version: {version}");
            }
            if (typeCount.HasValue) lines.Add($"- API types: {typeCount.Value}");
            if (apiCatalogs.Count > 1) lines.Add($"- API catalogs: {apiCatalogs.Count}");
            lines.Add(string.Empty);
            if (!string.IsNullOrWhiteSpace(legacyInstallCommand) || packages.Any(HasInstallCommand))
            {
                lines.Add("## Install");
                if (packages.Count == 0)
                    lines.Add($"- {FormatInlineCode(legacyInstallCommand!)}");
                else
                    AppendPackageInstallMarkdown(lines, packages);
                lines.Add(string.Empty);
            }
        }
        if (quickstart is not null)
        {
            lines.Add("## Quickstart");
            lines.Add($"```{quickstart.Language}");
            lines.AddRange(quickstart.Lines);
            lines.Add("```");
            lines.Add(string.Empty);
        }
        if (apiCatalogs.Count > 0)
        {
            lines.Add("## Machine-friendly API data");
            AppendApiResourceLinks(lines, apiCatalogs);
        }
        AppendOptionalMarkdown(lines, discoveryContentPath);
        if (apiCatalogs.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"Slug rule: {ApiSlugRule}.");
        }

        File.WriteAllText(path, string.Join(Environment.NewLine, lines), Encoding.UTF8);
    }

    private static void WriteLlmsJson(
        string path,
        string name,
        string? packageId,
        string? version,
        string? legacyInstallCommand,
        IReadOnlyList<PackageInfo> packages,
        int? typeCount,
        IReadOnlyList<ApiCatalogInfo> apiCatalogs,
        QuickstartInfo? quickstart,
        bool includePackageContent)
    {
        var payload = new Dictionary<string, object?> { ["name"] = name };
        if (apiCatalogs.Count > 0)
            payload["apiTypeCount"] = typeCount;
        if (quickstart is not null)
        {
            payload["quickstart"] = quickstart.Lines.Where(l => l != null).ToArray();
            payload["quickstartLanguage"] = quickstart.Language;
        }
        if (includePackageContent)
        {
            payload["version"] = version;
            if (packages.Count == 0)
            {
                payload["package"] = packageId;
                if (!string.IsNullOrWhiteSpace(legacyInstallCommand))
                    payload["install"] = new[] { legacyInstallCommand };
            }
            else
            {
                payload["packages"] = packages.Select(CreatePackagePayload).ToArray();
                var installCommands = packages
                    .Select(static package => package.InstallCommand)
                    .Where(static command => !string.IsNullOrWhiteSpace(command))
                    .ToArray();
                if (installCommands.Length > 0)
                    payload["install"] = installCommands;
            }
        }
        if (apiCatalogs.Count == 1)
            payload["api"] = CreateApiResourcePayload(apiCatalogs[0]);
        else if (apiCatalogs.Count > 1)
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
        string? packageId,
        string? version,
        string? legacyInstallCommand,
        IReadOnlyList<PackageInfo> packages,
        int? typeCount,
        IReadOnlyList<ApiCatalogInfo> apiCatalogs,
        string overview,
        QuickstartInfo? quickstart,
        WebLlmsOptions options,
        bool includePackageContent)
    {
        var lines = new List<string>
        {
            $"# {name} - Complete AI Context",
            string.Empty,
            "## Overview",
            overview
        };
        if (includePackageContent && packages.Count == 0)
        {
            lines.Add($"- Package: {packageId}");
            lines.Add($"- Version: {version}");
        }
        else if (includePackageContent)
        {
            lines.Add($"- Packages: {packages.Count}");
            lines.Add($"- Suite version: {version}");
        }
        if (typeCount.HasValue) lines.Add($"- API types: {typeCount.Value}");
        if (apiCatalogs.Count > 1) lines.Add($"- API catalogs: {apiCatalogs.Count}");
        if (!string.IsNullOrWhiteSpace(options.License)) lines.Add($"- License: {options.License}");
        if (!string.IsNullOrWhiteSpace(options.Targets)) lines.Add($"- Targets: {options.Targets}");

        if (includePackageContent &&
            (!string.IsNullOrWhiteSpace(legacyInstallCommand) || packages.Any(HasInstallCommand)))
        {
            lines.Add(string.Empty);
            lines.Add("## Installation");
            if (packages.Count == 0)
            {
                lines.Add("```");
                lines.Add(legacyInstallCommand!);
                lines.Add("```");
            }
            else
            {
                AppendPackageInstallMarkdown(lines, packages);
            }
        }
        if (quickstart is not null)
        {
            lines.Add(string.Empty);
            lines.Add("## Quickstart");
            lines.Add($"```{quickstart.Language}");
            lines.AddRange(quickstart.Lines);
            lines.Add("```");
        }
        if (apiCatalogs.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("## API Resources");
            AppendApiResourceLinks(lines, apiCatalogs);
            lines.Add($"- Slug rule: {ApiSlugRule}.");
        }

        AppendApiDetails(lines, options, apiCatalogs);
        AppendOptionalMarkdown(lines, options.DiscoveryContentPath);

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

    private static void AppendOptionalMarkdown(List<string> lines, string? contentPath)
    {
        if (string.IsNullOrWhiteSpace(contentPath))
            return;

        var fullPath = Path.GetFullPath(contentPath);
        if (!File.Exists(fullPath))
            return;

        lines.Add(string.Empty);
        lines.AddRange(File.ReadAllLines(fullPath));
    }

    private static void AppendPackageInstallMarkdown(List<string> lines, IReadOnlyList<PackageInfo> packages)
    {
        foreach (var package in packages)
        {
            if (!HasInstallCommand(package))
                continue;
            var version = string.IsNullOrWhiteSpace(package.Version)
                ? string.Empty
                : $" — source version `{package.Version}`";
            lines.Add($"- {FormatInlineCode(package.InstallCommand!)}{version}");
        }
    }

    private static bool HasInstallCommand(PackageInfo package)
        => !string.IsNullOrWhiteSpace(package.InstallCommand);

    private static Dictionary<string, object?> CreatePackagePayload(PackageInfo package)
    {
        var payload = new Dictionary<string, object?>
        {
            ["id"] = package.Id,
            ["version"] = package.Version
        };
        if (HasInstallCommand(package))
            payload["install"] = package.InstallCommand;
        return payload;
    }

    private static string FormatInlineCode(string value)
        => $"`{value.Replace("`", "\\`", StringComparison.Ordinal)}`";

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
            ["slugRule"] = ApiSlugRule
        };
    }
}
