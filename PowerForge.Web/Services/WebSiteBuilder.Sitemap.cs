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
        var entries = items
            .Where(static item => item is not null && !item.Draft && !string.IsNullOrWhiteSpace(item.OutputPath))
            .Where(item => ItemRendersHtml(spec, item))
            .OrderBy(static item => NormalizeRouteForMatch(item.OutputPath), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.SourcePath, StringComparer.OrdinalIgnoreCase)
            .Select(item => new WebSitemapEntry
            {
                Path = item.OutputPath,
                Title = item.Title,
                Description = item.Description,
                Section = item.Collection,
                LastModified = FormatSitemapLastModified(item.LastModifiedUtc),
                NoIndex = ItemDeclaresNoIndex(spec, item)
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

    private static bool ItemRendersHtml(SiteSpec spec, ContentItem item)
    {
        var formats = ResolveOutputFormats(spec, item);
        return formats.Any(static format =>
            format is not null &&
            (string.Equals(format.Name, "html", StringComparison.OrdinalIgnoreCase) ||
             string.IsNullOrWhiteSpace(format.Suffix) ||
             string.Equals(format.Suffix, "html", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool ItemDeclaresNoIndex(SiteSpec spec, ContentItem item)
    {
        var forceFallbackNoIndex = IsLocalizedFallbackCopy(item) && !HasExplicitRobotsOverride(item);
        var resolved = ResolveCrawlPolicy(spec, item);
        var robots = forceFallbackNoIndex && string.IsNullOrWhiteSpace(resolved.Robots)
            ? "noindex,follow"
            : resolved.Robots;

        return ContainsNoIndexDirective(robots) ||
               resolved.Bots.Values.Any(ContainsNoIndexDirective);
    }

    private static bool ContainsNoIndexDirective(string? directives)
        => !string.IsNullOrWhiteSpace(directives) &&
           directives.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
               .Any(static directive => directive.Equals("noindex", StringComparison.OrdinalIgnoreCase));
}
