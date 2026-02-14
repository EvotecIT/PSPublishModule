using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Scriban;
using Scriban.Runtime;

namespace PowerForge.Web;

public static partial class WebSitemapGenerator
{
    private const long MaxEntriesJsonBytes = 8 * 1024 * 1024;

    private static WebSitemapEntry BuildEntryFromHtmlFile(string filePath, string route)
    {
        var section = ResolveSectionFromRoute(route);
        var title = TryReadHtmlTitle(filePath);
        if (string.IsNullOrWhiteSpace(title))
            title = BuildTitleFromRoute(route);
        return new WebSitemapEntry
        {
            Path = route,
            Title = title,
            Section = section
        };
    }

    private static string? TryReadHtmlTitle(string filePath)
    {
        try
        {
            var text = File.ReadAllText(filePath);
            var startTag = text.IndexOf("<title", StringComparison.OrdinalIgnoreCase);
            if (startTag < 0) return null;
            var startValue = text.IndexOf('>', startTag);
            if (startValue < 0) return null;
            var endTag = text.IndexOf("</title>", startValue + 1, StringComparison.OrdinalIgnoreCase);
            if (endTag <= startValue) return null;
            var raw = text.Substring(startValue + 1, endTag - startValue - 1).Trim();
            return string.IsNullOrWhiteSpace(raw)
                ? null
                : System.Net.WebUtility.HtmlDecode(raw);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Failed to read sitemap HTML title from '{filePath}': {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static string BuildTitleFromRoute(string route)
    {
        var normalized = NormalizeRoute(route).Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
            return "Home";
        var segment = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? normalized;
        if (segment.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            segment = segment[..^".html".Length];
        segment = segment.Replace('-', ' ').Replace('_', ' ');
        return string.IsNullOrWhiteSpace(segment)
            ? "Page"
            : char.ToUpperInvariant(segment[0]) + segment.Substring(1);
    }

    private static string ResolveSectionFromRoute(string route)
    {
        var normalized = NormalizeRoute(route).Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
            return "Home";
        var segment = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? normalized;
        segment = segment.Replace('-', ' ').Replace('_', ' ');
        if (string.IsNullOrWhiteSpace(segment))
            return "Pages";
        return char.ToUpperInvariant(segment[0]) + segment.Substring(1);
    }

    private static IEnumerable<WebSitemapEntry> LoadEntriesFromJsonPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("EntriesJsonPath cannot be empty.", nameof(path));

        var fullPath = Path.GetFullPath(path.Trim().Trim('"'));
        if (!fullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Sitemap entries file must be JSON: {fullPath}");
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Sitemap entries JSON not found: {fullPath}");
        var info = new FileInfo(fullPath);
        if (info.Length > MaxEntriesJsonBytes)
            throw new InvalidDataException($"Sitemap entries JSON exceeds max size ({MaxEntriesJsonBytes} bytes): {fullPath}");

        var json = File.ReadAllText(fullPath);
        using var doc = JsonDocument.Parse(json);
        var list = new List<WebSitemapEntry>();
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
            ParseSitemapEntriesArray(root, list);
        else if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("entries", out var entriesElement) && entriesElement.ValueKind == JsonValueKind.Array)
                ParseSitemapEntriesArray(entriesElement, list);
            else
                throw new InvalidDataException($"Sitemap entries JSON object must contain an 'entries' array: {fullPath}");
        }
        else
        {
            throw new InvalidDataException($"Sitemap entries JSON must be an array or object with an 'entries' array: {fullPath}");
        }
        return list;
    }

    private static void ParseSitemapEntriesArray(JsonElement arrayElement, List<WebSitemapEntry> destination)
    {
        foreach (var item in arrayElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var path = GetString(item, "path") ??
                       GetString(item, "route") ??
                       GetString(item, "url");
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var alternates = new List<WebSitemapAlternate>();
            if (item.TryGetProperty("alternates", out var alternatesElement) && alternatesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var alt in alternatesElement.EnumerateArray())
                {
                    if (alt.ValueKind != JsonValueKind.Object)
                        continue;
                    var hrefLang = GetString(alt, "hrefLang") ?? GetString(alt, "hreflang");
                    var altPath = GetString(alt, "path") ?? GetString(alt, "route") ?? GetString(alt, "url") ?? GetString(alt, "href");
                    if (string.IsNullOrWhiteSpace(hrefLang) || string.IsNullOrWhiteSpace(altPath))
                        continue;
                    alternates.Add(new WebSitemapAlternate
                    {
                        HrefLang = hrefLang,
                        Path = altPath
                    });
                }
            }

            destination.Add(new WebSitemapEntry
            {
                Path = path,
                Title = GetString(item, "title"),
                Description = GetString(item, "description"),
                Section = GetString(item, "section"),
                Priority = GetString(item, "priority"),
                ChangeFrequency = GetString(item, "changefreq") ?? GetString(item, "changeFrequency"),
                LastModified = GetString(item, "lastmod") ?? GetString(item, "lastModified"),
                Alternates = alternates.ToArray()
            });
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        if (!element.TryGetProperty(propertyName, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.String)
            return value.GetString();
        if (value.ValueKind == JsonValueKind.Number ||
            value.ValueKind == JsonValueKind.True ||
            value.ValueKind == JsonValueKind.False)
            return value.ToString();
        return null;
    }

    private static void WriteSitemapJson(string baseUrl, IReadOnlyList<WebSitemapEntry> entries, string outputPath)
    {
        var payload = new Dictionary<string, object?>
        {
            ["baseUrl"] = baseUrl,
            ["urlCount"] = entries.Count,
            ["entries"] = entries.Select(entry => new Dictionary<string, object?>
            {
                ["path"] = entry.Path,
                ["url"] = baseUrl + (entry.Path.StartsWith("/", StringComparison.Ordinal) ? entry.Path : "/" + entry.Path),
                ["title"] = entry.Title,
                ["description"] = entry.Description,
                ["section"] = entry.Section,
                ["lastModified"] = entry.LastModified,
                ["changeFrequency"] = entry.ChangeFrequency,
                ["priority"] = entry.Priority,
                ["alternates"] = entry.Alternates.Select(static alt => new Dictionary<string, object?>
                {
                    ["hrefLang"] = alt.HrefLang,
                    ["path"] = alt.Path
                }).ToArray()
            }).ToArray()
        };

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        var serialized = JsonSerializer.Serialize(payload, WebJson.Options);
        if (File.Exists(outputPath))
        {
            var previous = File.ReadAllText(outputPath);
            if (string.Equals(previous, serialized, StringComparison.Ordinal))
                return;
        }
        File.WriteAllText(outputPath, serialized, Encoding.UTF8);
    }

    private static string? TryResolveSitemapCssHref(string siteRoot)
    {
        var specPath = Path.Combine(siteRoot, "_powerforge", "site-spec.json");
        if (File.Exists(specPath))
        {
            try
            {
                var spec = JsonSerializer.Deserialize<SiteSpec>(File.ReadAllText(specPath), WebJson.Options);
                if (!string.IsNullOrWhiteSpace(spec?.DefaultTheme))
                {
                    var themesFolder = string.IsNullOrWhiteSpace(spec.ThemesRoot) || Path.IsPathRooted(spec.ThemesRoot)
                        ? "themes"
                        : spec.ThemesRoot.Trim().TrimStart('/', '\\');
                    var themeAssetsRoot = Path.Combine(siteRoot, themesFolder, spec.DefaultTheme, "assets");
                    var preferred = new[]
                    {
                        Path.Combine(themeAssetsRoot, "app.css"),
                        Path.Combine(themeAssetsRoot, "site.css")
                    };
                    foreach (var path in preferred)
                    {
                        if (!File.Exists(path))
                            continue;
                        var relative = Path.GetRelativePath(siteRoot, path).Replace('\\', '/');
                        return "/" + relative.TrimStart('/');
                    }

                    if (Directory.Exists(themeAssetsRoot))
                    {
                        var firstCss = Directory
                            .EnumerateFiles(themeAssetsRoot, "*.css", SearchOption.AllDirectories)
                            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                            .FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(firstCss))
                        {
                            var relative = Path.GetRelativePath(siteRoot, firstCss).Replace('\\', '/');
                            return "/" + relative.TrimStart('/');
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Failed to resolve sitemap CSS from site spec '{specPath}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        var fallback = new[]
        {
            Path.Combine(siteRoot, "css", "app.css"),
            Path.Combine(siteRoot, "assets", "app.css")
        };
        foreach (var path in fallback)
        {
            if (!File.Exists(path))
                continue;
            var relative = Path.GetRelativePath(siteRoot, path).Replace('\\', '/');
            return "/" + relative.TrimStart('/');
        }

        return null;
    }

    private static string RenderHtmlSitemap(string baseUrl, WebSitemapOptions options, IReadOnlyList<WebSitemapEntry> entries)
    {
        var templatePath = options.HtmlTemplatePath;
        string templateText;
        if (!string.IsNullOrWhiteSpace(templatePath))
        {
            if (!File.Exists(templatePath))
                throw new FileNotFoundException($"Sitemap HTML template not found: {templatePath}");
            templateText = File.ReadAllText(templatePath);
        }
        else
        {
            templateText = DefaultHtmlTemplate;
        }
        var title = string.IsNullOrWhiteSpace(options.HtmlTitle) ? "Sitemap" : options.HtmlTitle;
        var cssHref = string.IsNullOrWhiteSpace(options.HtmlCssHref)
            ? (TryResolveSitemapCssHref(options.SiteRoot) ?? string.Empty)
            : options.HtmlCssHref;

        var items = entries.Select(e => new Dictionary<string, object?>
            {
                ["path"] = e.Path,
                ["url"] = baseUrl + (e.Path.StartsWith("/") ? e.Path : "/" + e.Path),
                ["title"] = string.IsNullOrWhiteSpace(e.Title) ? BuildTitleFromRoute(e.Path) : e.Title,
                ["section"] = string.IsNullOrWhiteSpace(e.Section) ? ResolveSectionFromRoute(e.Path) : e.Section,
                ["description"] = e.Description,
                ["lastmod"] = e.LastModified,
                ["changefreq"] = e.ChangeFrequency,
                ["priority"] = e.Priority
            })
            .ToList();
        DisambiguateDuplicateTitles(items);
        var groups = items
            .GroupBy(
                static item => item.TryGetValue("section", out var section) ? section?.ToString() ?? "Pages" : "Pages",
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new Dictionary<string, object?>
            {
                ["name"] = group.Key,
                ["items"] = group.OrderBy(
                        static item => item.TryGetValue("title", out var titleValue) ? titleValue?.ToString() ?? string.Empty : string.Empty,
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            })
            .ToArray();

        var parsed = Template.Parse(templateText);
        if (parsed.HasErrors)
        {
            var messages = string.Join(Environment.NewLine, parsed.Messages.Select(m => m.Message));
            throw new InvalidOperationException($"Sitemap HTML template errors:{Environment.NewLine}{messages}");
        }

        var globals = new ScriptObject();
        globals.Add("title", title);
        globals.Add("base_url", baseUrl);
        globals.Add("css_href", cssHref);
        globals.Add("generated", DateTime.UtcNow.ToString("O"));
        globals.Add("entries", items);
        globals.Add("groups", groups);

        var context = new TemplateContext
        {
            LoopLimit = 0
        };
        context.PushGlobal(globals);

        return parsed.Render(context);
    }

    private static void DisambiguateDuplicateTitles(List<Dictionary<string, object?>> items)
    {
        if (items is null || items.Count == 0)
            return;

        var duplicateGroups = items
            .Where(static item => item.TryGetValue("title", out var title) && !string.IsNullOrWhiteSpace(title?.ToString()))
            .GroupBy(
                static item => item.TryGetValue("title", out var title) ? title?.ToString() ?? string.Empty : string.Empty,
                StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .ToArray();

        foreach (var group in duplicateGroups)
        {
            foreach (var item in group)
            {
                if (!item.TryGetValue("path", out var pathValue))
                    continue;
                var path = pathValue?.ToString();
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                item["title"] = $"{group.Key} ({path})";
            }
        }
    }

    private const string DefaultHtmlTemplate = @"<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
  <meta name=""robots"" content=""noindex"" />
  <title>{{ title }}</title>
  {{ if css_href != """" }}<link rel=""stylesheet"" href=""{{ css_href }}"" />{{ end }}
  <style>
    body { margin: 0; font-family: Segoe UI, Arial, sans-serif; background: #f8fafc; color: #0f172a; }
    .pf-sitemap-wrap { max-width: 1080px; margin: 0 auto; padding: 38px 18px 44px; }
    .pf-sitemap-meta { margin-top: 8px; color: #475569; font-size: 0.92rem; }
    .pf-sitemap-group { margin-top: 28px; }
    .pf-sitemap-group h2 { margin: 0 0 12px; font-size: 1.2rem; }
    .pf-sitemap-list { list-style: none; margin: 0; padding: 0; display: grid; gap: 10px; }
    .pf-sitemap-item { border: 1px solid #cbd5e1; border-radius: 12px; padding: 10px 12px; background: #ffffff; }
    .pf-sitemap-item a { text-decoration: none; font-weight: 600; }
    .pf-sitemap-path { margin-top: 4px; font-size: 0.83rem; color: #64748b; }
    .pf-sitemap-desc { margin-top: 6px; font-size: 0.9rem; color: #334155; }
  </style>
</head>
<body>
  <main class=""pf-sitemap-wrap"">
    <h1>{{ title }}</h1>
    <div class=""pf-sitemap-meta"">Generated {{ generated }} ({{ entries.size }} URLs)</div>
    {{ for group in groups }}
      <section class=""pf-sitemap-group"">
        <h2>{{ group.name }}</h2>
        <ul class=""pf-sitemap-list"">
          {{ for item in group.items }}
            <li class=""pf-sitemap-item"">
              <a href=""{{ item.url }}"">{{ item.title }}</a>
              <div class=""pf-sitemap-path"">{{ item.path }}</div>
              {{ if item.description != """" }}<div class=""pf-sitemap-desc"">{{ item.description }}</div>{{ end }}
            </li>
          {{ end }}
        </ul>
      </section>
    {{ end }}
  </main>
</body>
</html>";
}
