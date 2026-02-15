using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    /// <summary>
    /// Exports a deterministic navigation payload (site-nav.json shape) from site.json + discovered content,
    /// without building HTML output. Intended for API docs and external tooling that consumes nav surfaces/profiles.
    /// </summary>
    /// <param name="spec">Site configuration.</param>
    /// <param name="plan">Resolved site plan.</param>
    /// <param name="outputPath">Target JSON file path.</param>
    /// <param name="overwrite">
    /// When false (default), existing files are overwritten only when they were previously generated
    /// by PowerForge (root property <c>generated: true</c>). When true, always overwrite.
    /// </param>
    /// <param name="options">Optional JSON serializer options.</param>
    public static WebNavExportResult ExportSiteNavJson(
        SiteSpec spec,
        WebSitePlan plan,
        string outputPath,
        bool overwrite = false,
        JsonSerializerOptions? options = null)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("Output path is required.", nameof(outputPath));

        var fullOut = Path.GetFullPath(outputPath.Trim().Trim('"'));
        var normalizedRoot = NormalizeRootPathForSink(plan.RootPath);
        if (!IsPathWithinRoot(normalizedRoot, fullOut))
        {
            return new WebNavExportResult
            {
                Success = false,
                OutputPath = fullOut,
                Changed = false,
                Message = "Refusing to write nav export outside site root."
            };
        }

        var directory = Path.GetDirectoryName(fullOut);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        if (!overwrite && File.Exists(fullOut) && !IsGeneratedNavExport(fullOut))
        {
            return new WebNavExportResult
            {
                Success = false,
                OutputPath = fullOut,
                Changed = false,
                Message = "Refusing to overwrite existing nav export (file is not marked generated:true). Use --overwrite/overwrite:true to force."
            };
        }

        var json = BuildSiteNavJsonForExport(spec, plan, options ?? WebJson.Options);
        var changed = WriteAllTextIfChanged(fullOut, json);

        return new WebNavExportResult
        {
            Success = true,
            OutputPath = fullOut,
            Changed = changed,
            Message = changed ? "Nav export updated." : "Nav export up-to-date."
        };
    }

    private static bool IsGeneratedNavExport(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            if (doc.RootElement.TryGetProperty("generated", out var generatedProp) &&
                generatedProp.ValueKind == JsonValueKind.True)
            {
                return true;
            }
        }
        catch
        {
            // ignore invalid JSON and treat as user-managed file
        }

        return false;
    }

    private static string BuildSiteNavJsonForExport(SiteSpec spec, WebSitePlan plan, JsonSerializerOptions options)
    {
        var redirects = new List<RedirectSpec>();
        if (spec.RouteOverrides is { Length: > 0 }) redirects.AddRange(spec.RouteOverrides);
        if (spec.Redirects is { Length: > 0 }) redirects.AddRange(spec.Redirects);

        var projectSpecs = LoadProjectSpecs(plan.ProjectsRoot, options).ToList();
        foreach (var project in projectSpecs)
        {
            if (project.Redirects is { Length: > 0 })
                redirects.AddRange(project.Redirects);
        }

        AddVersioningAliasRedirects(spec, redirects);

        var data = LoadData(spec, plan, projectSpecs);
        var projectMap = projectSpecs
            .Where(p => !string.IsNullOrWhiteSpace(p.Slug))
            .ToDictionary(p => p.Slug, StringComparer.OrdinalIgnoreCase);
        var projectContentMap = projectSpecs
            .Where(p => p.Content is not null && !string.IsNullOrWhiteSpace(p.Slug))
            .ToDictionary(p => p.Slug, p => p.Content!, StringComparer.OrdinalIgnoreCase);
        var cacheRoot = ResolveCacheRoot(spec, plan.RootPath);

        var items = BuildContentItems(spec, plan, redirects, data, projectMap, projectContentMap, cacheRoot);
        items.AddRange(BuildTaxonomyItems(spec, items));
        items = BuildPaginatedItems(spec, items);

        var menuSpecs = BuildMenuSpecs(spec, items, plan.RootPath);

        // Reuse the same nav export shape as the build output (site-nav.json under outputRoot/<dataRoot>/).
        return BuildSiteNavJson(spec, menuSpecs);
    }
}
