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
    public bool IncludeHtmlFiles { get; set; } = true;
    public bool IncludeTextFiles { get; set; } = true;
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

        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (options.IncludeHtmlFiles)
        {
            foreach (var file in Directory.EnumerateFiles(siteRoot, "*.html", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(siteRoot, file).Replace('\\', '/');
                if (relative.EndsWith("404.html", StringComparison.OrdinalIgnoreCase)) continue;
                var route = NormalizeRoute(relative);
                if (string.IsNullOrWhiteSpace(route)) continue;
                urls.Add(route);
            }
        }

        if (options.IncludeTextFiles)
        {
            var extras = new[] { "llms.txt", "llms.json", "llms-full.txt", "robots.txt" };
            foreach (var name in extras)
            {
                var candidate = Path.Combine(siteRoot, name);
                if (File.Exists(candidate))
                    urls.Add("/" + name);
            }
        }

        if (options.ExtraPaths is not null)
        {
            foreach (var path in options.ExtraPaths.Where(p => !string.IsNullOrWhiteSpace(p)))
                urls.Add(NormalizeRoute(path));
        }

        if (!string.IsNullOrWhiteSpace(options.ApiSitemapPath))
            MergeApiSitemap(options.ApiSitemapPath, baseUrl, urls);

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var entries = urls.OrderBy(u => u, StringComparer.OrdinalIgnoreCase)
            .Select(u => BuildEntry(baseUrl, u, today))
            .ToArray();

        var ns = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(ns + "urlset", entries.Select(e => e.WithNamespace(ns))));

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? siteRoot);
        using var stream = File.Create(outputPath);
        doc.Save(stream);

        return new WebSitemapResult
        {
            OutputPath = outputPath,
            UrlCount = entries.Length
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

    private static XElement BuildEntry(string baseUrl, string path, string lastmod)
    {
        var loc = baseUrl + (path.StartsWith("/") ? path : "/" + path);
        return new XElement("url",
            new XElement("loc", loc),
            new XElement("lastmod", lastmod),
            new XElement("changefreq", "monthly"),
            new XElement("priority", path == "/" ? "1.0" : "0.5"));
    }

    private static void MergeApiSitemap(string apiSitemapPath, string baseUrl, HashSet<string> urls)
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
                    urls.Add(string.IsNullOrWhiteSpace(path) ? "/" : path);
                }
                else
                {
                    urls.Add(normalized);
                }
            }
        }
        catch
        {
            return;
        }
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
