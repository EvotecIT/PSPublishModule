using System.Text;
using System.Xml.Linq;
using Scriban;
using Scriban.Runtime;

namespace PowerForge.Web;

/// <summary>Options for sitemap generation.</summary>
public sealed class WebSitemapOptions
{
    /// <summary>Root directory of the generated site.</summary>
    public string SiteRoot { get; set; } = ".";
    /// <summary>Base URL for sitemap entries.</summary>
    public string BaseUrl { get; set; } = string.Empty;
    /// <summary>Optional output path override.</summary>
    public string? OutputPath { get; set; }
    /// <summary>Optional existing API sitemap path to merge.</summary>
    public string? ApiSitemapPath { get; set; }
    /// <summary>Additional paths to include.</summary>
    public string[]? ExtraPaths { get; set; }
    /// <summary>Explicit sitemap entries.</summary>
    public WebSitemapEntry[]? Entries { get; set; }
    /// <summary>When true, include HTML files.</summary>
    public bool IncludeHtmlFiles { get; set; } = true;
    /// <summary>When true, include text files (robots/llms).</summary>
    public bool IncludeTextFiles { get; set; } = true;
    /// <summary>When true, generate an HTML sitemap.</summary>
    public bool GenerateHtml { get; set; }
    /// <summary>Optional HTML sitemap output path.</summary>
    public string? HtmlOutputPath { get; set; }
    /// <summary>Optional HTML sitemap template path.</summary>
    public string? HtmlTemplatePath { get; set; }
    /// <summary>Optional HTML title override.</summary>
    public string? HtmlTitle { get; set; }
    /// <summary>Optional CSS href to include in the HTML sitemap.</summary>
    public string? HtmlCssHref { get; set; }
}

/// <summary>Explicit sitemap entry metadata.</summary>
public sealed class WebSitemapEntry
{
    /// <summary>Route path (relative to base URL).</summary>
    public string Path { get; set; } = "/";
    /// <summary>Optional change frequency value.</summary>
    public string? ChangeFrequency { get; set; }
    /// <summary>Optional priority value.</summary>
    public string? Priority { get; set; }
    /// <summary>Optional last-modified date.</summary>
    public string? LastModified { get; set; }
}

/// <summary>Generates sitemap.xml for the site output.</summary>
public static class WebSitemapGenerator
{
    /// <summary>Generates a sitemap from site output.</summary>
    /// <param name="options">Generation options.</param>
    /// <returns>Result payload describing the sitemap output.</returns>
    public static WebSitemapResult Generate(WebSitemapOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            throw new ArgumentException("BaseUrl is required.", nameof(options));

        var siteRoot = Path.GetFullPath(options.SiteRoot);
        if (!Directory.Exists(siteRoot))
            throw new DirectoryNotFoundException($"Site root not found: {siteRoot}");

        var baseUrl = options.BaseUrl.TrimEnd('/');
        var outputPath = options.OutputPath;
        if (string.IsNullOrWhiteSpace(outputPath))
            outputPath = Path.Combine(siteRoot, "sitemap.xml");
        outputPath = Path.GetFullPath(outputPath);

        var entries = new Dictionary<string, WebSitemapEntry>(StringComparer.OrdinalIgnoreCase);
        string? htmlOutputPath = null;

        if (options.IncludeHtmlFiles)
        {
            foreach (var file in Directory.EnumerateFiles(siteRoot, "*.html", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(siteRoot, file).Replace('\\', '/');
                if (relative.EndsWith("404.html", StringComparison.OrdinalIgnoreCase)) continue;
                var route = NormalizeRoute(relative);
                if (string.IsNullOrWhiteSpace(route)) continue;
                AddOrUpdate(entries, route, null);
            }
        }

        if (options.IncludeTextFiles)
        {
            var extras = new[] { "llms.txt", "llms.json", "llms-full.txt", "robots.txt" };
            foreach (var name in extras)
            {
                var candidate = Path.Combine(siteRoot, name);
                if (File.Exists(candidate))
                    AddOrUpdate(entries, "/" + name, null);
            }
        }

        if (options.ExtraPaths is not null)
        {
            foreach (var path in options.ExtraPaths.Where(p => !string.IsNullOrWhiteSpace(p)))
                AddOrUpdate(entries, NormalizeRoute(path), null);
        }

        if (options.Entries is not null)
        {
            foreach (var entry in options.Entries)
            {
                if (entry is null || string.IsNullOrWhiteSpace(entry.Path)) continue;
                AddOrUpdate(entries, NormalizeRoute(entry.Path), entry);
            }
        }

        if (!string.IsNullOrWhiteSpace(options.ApiSitemapPath))
            MergeApiSitemap(options.ApiSitemapPath, baseUrl, entries);

        if (options.GenerateHtml)
        {
            htmlOutputPath = string.IsNullOrWhiteSpace(options.HtmlOutputPath)
                ? Path.Combine(siteRoot, "sitemap", "index.html")
                : Path.GetFullPath(options.HtmlOutputPath);

            if (IsUnderRoot(siteRoot, htmlOutputPath))
            {
                var relative = Path.GetRelativePath(siteRoot, htmlOutputPath).Replace('\\', '/');
                AddOrUpdate(entries, NormalizeRoute(relative), null);
            }
        }

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var entriesXml = entries.Values
            .OrderBy(u => u.Path, StringComparer.OrdinalIgnoreCase)
            .Select(u => BuildEntry(baseUrl, u, today))
            .ToArray();

        var ns = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(ns + "urlset", entriesXml.Select(e => e.WithNamespace(ns))));

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? siteRoot);
        using (var stream = File.Create(outputPath))
        {
            doc.Save(stream);
        }

        if (options.GenerateHtml && !string.IsNullOrWhiteSpace(htmlOutputPath))
        {
            var entryList = entries.Values
                .OrderBy(u => u.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var html = RenderHtmlSitemap(baseUrl, options, entryList);
            Directory.CreateDirectory(Path.GetDirectoryName(htmlOutputPath) ?? siteRoot);
            File.WriteAllText(htmlOutputPath, html, Encoding.UTF8);
        }

        return new WebSitemapResult
        {
            OutputPath = outputPath,
            HtmlOutputPath = htmlOutputPath,
            UrlCount = entriesXml.Length
        };
    }

    private static string NormalizeRoute(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "/";
        var trimmed = path.Replace('\\', '/').Trim();
        if (trimmed.StartsWith("/")) trimmed = trimmed.TrimStart('/');

        if (trimmed.Equals("index.html", StringComparison.OrdinalIgnoreCase))
            return "/";

        if (trimmed.EndsWith("/index.html", StringComparison.OrdinalIgnoreCase))
        {
            var folder = trimmed[..^"index.html".Length].TrimEnd('/');
            return "/" + folder + "/";
        }

        if (!trimmed.StartsWith("/"))
            trimmed = "/" + trimmed;

        return trimmed;
    }

    private static XElement BuildEntry(string baseUrl, WebSitemapEntry entry, string defaultLastmod)
    {
        var path = string.IsNullOrWhiteSpace(entry.Path) ? "/" : entry.Path;
        var loc = baseUrl + (path.StartsWith("/") ? path : "/" + path);
        var lastmod = string.IsNullOrWhiteSpace(entry.LastModified) ? defaultLastmod : entry.LastModified;
        var changefreq = string.IsNullOrWhiteSpace(entry.ChangeFrequency) ? "monthly" : entry.ChangeFrequency;
        var priority = string.IsNullOrWhiteSpace(entry.Priority)
            ? (path == "/" ? "1.0" : "0.5")
            : entry.Priority;
        return new XElement("url",
            new XElement("loc", loc),
            new XElement("lastmod", lastmod),
            new XElement("changefreq", changefreq),
            new XElement("priority", priority));
    }

    private static void MergeApiSitemap(string apiSitemapPath, string baseUrl, Dictionary<string, WebSitemapEntry> entries)
    {
        var full = Path.GetFullPath(apiSitemapPath);
        if (!File.Exists(full)) return;

        try
        {
            var doc = XDocument.Load(full);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            foreach (var url in doc.Descendants(ns + "url"))
            {
                var loc = url.Element(ns + "loc")?.Value;
                if (string.IsNullOrWhiteSpace(loc)) continue;
                var normalized = ReplaceHost(loc, baseUrl);
                if (normalized.StartsWith(baseUrl, StringComparison.OrdinalIgnoreCase))
                {
                    var path = normalized.Substring(baseUrl.Length);
                    AddOrUpdate(entries, string.IsNullOrWhiteSpace(path) ? "/" : path, null);
                }
                else
                {
                    if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        continue;
                    AddOrUpdate(entries, normalized, null);
                }
            }
        }
        catch
        {
            return;
        }
    }

    private static void AddOrUpdate(Dictionary<string, WebSitemapEntry> entries, string path, WebSitemapEntry? update)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var normalized = NormalizeRoute(path);
        if (entries.TryGetValue(normalized, out var existing))
        {
            if (update is null) return;
            if (!string.IsNullOrWhiteSpace(update.ChangeFrequency))
                existing.ChangeFrequency = update.ChangeFrequency;
            if (!string.IsNullOrWhiteSpace(update.Priority))
                existing.Priority = update.Priority;
            if (!string.IsNullOrWhiteSpace(update.LastModified))
                existing.LastModified = update.LastModified;
            return;
        }

        if (update is null)
        {
            entries[normalized] = new WebSitemapEntry { Path = normalized };
            return;
        }

        entries[normalized] = new WebSitemapEntry
        {
            Path = normalized,
            ChangeFrequency = update.ChangeFrequency,
            Priority = update.Priority,
            LastModified = update.LastModified
        };
    }

    private static string ReplaceHost(string input, string baseUrl)
    {
        var trimmed = baseUrl.TrimEnd('/');
        if (input.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("https://localhost", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(input);
            return trimmed + uri.AbsolutePath + uri.Query;
        }

        return input;
    }

    private static XElement WithNamespace(this XElement element, XNamespace ns)
    {
        var namespaced = new XElement(ns + element.Name.LocalName);
        foreach (var attr in element.Attributes())
            namespaced.Add(attr);
        foreach (var node in element.Nodes())
        {
            if (node is XElement child)
                namespaced.Add(child.WithNamespace(ns));
            else
                namespaced.Add(node);
        }
        return namespaced;
    }

    private static bool IsUnderRoot(string root, string fullPath)
    {
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var targetFull = Path.GetFullPath(fullPath);
        return targetFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase);
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
        var cssHref = options.HtmlCssHref ?? string.Empty;

        var items = entries.Select(e => new Dictionary<string, object?>
            {
                ["path"] = e.Path,
                ["url"] = baseUrl + (e.Path.StartsWith("/") ? e.Path : "/" + e.Path),
                ["lastmod"] = e.LastModified,
                ["changefreq"] = e.ChangeFrequency,
                ["priority"] = e.Priority
            })
            .ToList();

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

        var context = new TemplateContext
        {
            LoopLimit = 0
        };
        context.PushGlobal(globals);

        return parsed.Render(context);
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
    body { font-family: system-ui, -apple-system, Segoe UI, sans-serif; margin: 0; background: #0b0f14; color: #e2e8f0; }
    .pf-wrap { max-width: 960px; margin: 0 auto; padding: 48px 24px; }
    .pf-list { list-style: none; margin: 24px 0 0; padding: 0; }
    .pf-item { padding: 12px 0; border-bottom: 1px solid rgba(148,163,184,0.2); }
    .pf-item a { color: inherit; text-decoration: none; }
    .pf-item a:hover { color: #22d3ee; }
    .pf-meta { font-size: 0.85rem; color: #94a3b8; }
  </style>
</head>
<body>
  <main class=""pf-wrap"">
    <h1>{{ title }}</h1>
    <div class=""pf-meta"">Generated {{ generated }}</div>
    <ul class=""pf-list"">
      {{ for item in entries }}
        <li class=""pf-item"">
          <a href=""{{ item.url }}"">{{ item.path }}</a>
        </li>
      {{ end }}
    </ul>
  </main>
</body>
</html>";
}
