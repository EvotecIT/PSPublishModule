using System.Globalization;
using System.Text.Json;

namespace PowerForge.Web;

/// <summary>Sitemap metadata export helpers.</summary>
public static partial class WebSiteBuilder
{
    private static void WriteSitemapEntriesReport(
        SiteSpec spec,
        IReadOnlyList<ContentItem> items,
        string metaDir)
    {
        if (spec is null || items is null || string.IsNullOrWhiteSpace(metaDir))
            return;

        var entries = items
            .Where(static item => item is not null && !item.Draft && !string.IsNullOrWhiteSpace(item.OutputPath))
            .OrderBy(static item => NormalizeRouteForMatch(item.OutputPath), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.SourcePath, StringComparer.OrdinalIgnoreCase)
            .Select(static item => new WebSitemapEntry
            {
                Path = item.OutputPath,
                Title = item.Title,
                Description = item.Description,
                Section = item.Collection,
                LastModified = FormatSitemapLastModified(item.LastModifiedUtc)
            })
            .ToArray();

        var payload = new
        {
            site = spec.Name,
            entries
        };

        var sitemapEntriesPath = Path.Combine(metaDir, "sitemap-entries.json");
        WriteAllTextIfChanged(sitemapEntriesPath, JsonSerializer.Serialize(payload, WebJson.Options));
    }

    private static string? FormatSitemapLastModified(DateTimeOffset? value)
    {
        if (!value.HasValue)
            return null;

        return value.Value
            .ToUniversalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }
}
