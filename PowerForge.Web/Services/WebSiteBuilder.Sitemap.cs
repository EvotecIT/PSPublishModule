using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

/// <summary>Exports trusted builder sitemap metadata under _powerforge/sitemap-entries.json.</summary>
public static partial class WebSiteBuilder
{
    private static readonly TimeSpan SitemapRenderedHtmlRegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex SitemapMetaTagRegex = new("<meta\\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, SitemapRenderedHtmlRegexTimeout);
    private static readonly Regex SitemapHtmlAttributeRegex = new("(?<name>[a-zA-Z_:][\\w:.-]*)\\s*=\\s*(?<q>['\"])(?<value>.*?)\\k<q>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant, SitemapRenderedHtmlRegexTimeout);
    private static readonly string[] SitemapNoIndexMetaNames = { "robots", "googlebot", "bingbot", "slurp" };

    private static void WriteSitemapEntriesReport(
        SiteSpec spec,
        IReadOnlyList<ContentItem> items,
        string metaDir,
        string outputRoot)
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
                NoIndex = ItemDeclaresNoIndex(spec, item, outputRoot)
            })
            .ToArray();

        var payload = new
        {
            schemaVersion = 1,
            site = spec.Name,
            noIndexTrusted = true,
            htmlRoutesTrusted = true,
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

    private static bool ItemDeclaresNoIndex(SiteSpec spec, ContentItem item, string outputRoot)
    {
        var forceFallbackNoIndex = IsLocalizedFallbackCopy(item) && !HasExplicitRobotsOverride(item);
        var resolved = ResolveCrawlPolicy(spec, item);
        var robots = forceFallbackNoIndex && string.IsNullOrWhiteSpace(resolved.Robots)
            ? "noindex,follow"
            : resolved.Robots;

        return ContainsNoIndexDirective(robots) ||
               resolved.Bots.Values.Any(ContainsNoIndexDirective) ||
               RenderedHtmlDeclaresNoIndex(spec, item, outputRoot);
    }

    private static bool ContainsNoIndexDirective(string? directives)
        => !string.IsNullOrWhiteSpace(directives) &&
           directives.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
               .Any(static directive => directive.Equals("noindex", StringComparison.OrdinalIgnoreCase));

    private static bool RenderedHtmlDeclaresNoIndex(SiteSpec spec, ContentItem item, string outputRoot)
    {
        if (string.IsNullOrWhiteSpace(outputRoot))
            return false;

        foreach (var format in ResolveOutputFormats(spec, item))
        {
            if (format is null ||
                (!string.Equals(format.Name, "html", StringComparison.OrdinalIgnoreCase) &&
                 !string.IsNullOrWhiteSpace(format.Suffix) &&
                 !string.Equals(format.Suffix, "html", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(format.Suffix, "htm", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var route = ResolveOutputRoute(item.OutputPath, format);
            var path = ResolveRenderedHtmlPath(outputRoot, route, format);
            if (!string.IsNullOrWhiteSpace(path) &&
                File.Exists(path) &&
                HasNoIndexRobotsMeta(File.ReadAllText(path)))
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveRenderedHtmlPath(string outputRoot, string route, OutputFormatSpec format)
    {
        var normalizedRoute = NormalizeRouteForMatch(route).Trim('/');
        var fileName = ResolveOutputFileName(format);
        var relative = string.IsNullOrWhiteSpace(normalizedRoute)
            ? fileName
            : normalizedRoute.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
              normalizedRoute.EndsWith(".htm", StringComparison.OrdinalIgnoreCase)
                ? normalizedRoute
                : Path.Combine(normalizedRoute.Replace('/', Path.DirectorySeparatorChar), fileName);

        return Path.GetFullPath(Path.Combine(outputRoot, relative));
    }

    private static bool HasNoIndexRobotsMeta(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return false;

        foreach (Match tagMatch in SitemapMetaTagRegex.Matches(html))
        {
            var tag = tagMatch.Value;
            var name = ReadSitemapHtmlAttribute(tag, "name");
            if (string.IsNullOrWhiteSpace(name) ||
                !SitemapNoIndexMetaNames.Any(known => known.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var content = ReadSitemapHtmlAttribute(tag, "content");
            if (ContainsNoIndexDirective(content))
                return true;
        }

        return false;
    }

    private static string? ReadSitemapHtmlAttribute(string tag, string attrName)
    {
        if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(attrName))
            return null;

        foreach (Match attrMatch in SitemapHtmlAttributeRegex.Matches(tag))
        {
            var name = attrMatch.Groups["name"].Value;
            if (!name.Equals(attrName, StringComparison.OrdinalIgnoreCase))
                continue;
            return System.Net.WebUtility.HtmlDecode(attrMatch.Groups["value"].Value).Trim();
        }

        return null;
    }
}
