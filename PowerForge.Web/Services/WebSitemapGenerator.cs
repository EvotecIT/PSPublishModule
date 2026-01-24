using System.Text;
using System.Xml.Linq;

namespace PowerForge.Web;

public sealed class WebSitemapOptions
{
    public string SiteRoot { get; set; } = ".";
    public string BaseUrl { get; set; } = string.Empty;
    public string? OutputPath { get; set; }
    public string? ApiSitemapPath { get; set; }
    public string[]? ExtraPaths { get; set; }
    public WebSitemapEntry[]? Entries { get; set; }
    public bool IncludeHtmlFiles { get; set; } = true;
    public bool IncludeTextFiles { get; set; } = true;
}

public sealed class WebSitemapEntry
{
    public string Path { get; set; } = "/";
    public string? ChangeFrequency { get; set; }
    public string? Priority { get; set; }
    public string? LastModified { get; set; }
}

public static class WebSitemapGenerator
{
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
        using var stream = File.Create(outputPath);
        doc.Save(stream);

        return new WebSitemapResult
        {
            OutputPath = outputPath,
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
}
