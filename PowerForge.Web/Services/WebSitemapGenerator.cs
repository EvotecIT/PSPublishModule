using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

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
    /// <summary>Optional JSON file containing sitemap entries (array or object with entries[]).</summary>
    public string? EntriesJsonPath { get; set; }
    /// <summary>When true, merge sitemap metadata emitted by the PowerForge site build under _powerforge/sitemap-entries.json.</summary>
    public bool UseGeneratedSitemapMetadata { get; set; } = true;
    /// <summary>When true, include HTML files.</summary>
    public bool IncludeHtmlFiles { get; set; } = true;
    /// <summary>When true, include HTML files that declare robots noindex.</summary>
    public bool IncludeNoIndexHtml { get; set; }
    /// <summary>When true, apply default exclusion patterns for utility HTML files.</summary>
    public bool UseDefaultExcludePatterns { get; set; } = true;
    /// <summary>Additional exclusion patterns for HTML route discovery.</summary>
    public string[]? ExcludePatterns { get; set; }
    /// <summary>When true, include text files (robots/llms).</summary>
    public bool IncludeTextFiles { get; set; } = true;
    /// <summary>When true, include HTML pages that declare robots noindex metadata.</summary>
    public bool IncludeNoIndexPages { get; set; }
    /// <summary>When true, emit localized alternate URLs (hreflang/x-default) when localization is configured.</summary>
    public bool IncludeLanguageAlternates { get; set; } = true;
    /// <summary>When true, generate an HTML sitemap.</summary>
    public bool GenerateHtml { get; set; }
    /// <summary>When true, generate a machine-readable sitemap JSON file.</summary>
    public bool GenerateJson { get; set; }
    /// <summary>Optional sitemap JSON output path.</summary>
    public string? JsonOutputPath { get; set; }
    /// <summary>Optional HTML sitemap output path.</summary>
    public string? HtmlOutputPath { get; set; }
    /// <summary>Optional HTML sitemap template path.</summary>
    public string? HtmlTemplatePath { get; set; }
    /// <summary>Optional HTML title override.</summary>
    public string? HtmlTitle { get; set; }
    /// <summary>Optional CSS href to include in the HTML sitemap.</summary>
    public string? HtmlCssHref { get; set; }
    /// <summary>When true, include the generated HTML sitemap route in sitemap.xml.</summary>
    public bool IncludeGeneratedHtmlRouteInXml { get; set; }
    /// <summary>When true, write a lightweight browser stylesheet for generated XML sitemaps. The generated stylesheet file is overwritten on each run when the href resolves under the site root.</summary>
    public bool GenerateBrowserStylesheet { get; set; } = true;
    /// <summary>Optional href override for the generated XML sitemap browser stylesheet.</summary>
    public string? BrowserStylesheetHref { get; set; }
    /// <summary>Optional news sitemap generation options.</summary>
    public WebSitemapNewsOptions? NewsSitemap { get; set; }
    /// <summary>Optional image sitemap generation options.</summary>
    public WebSitemapImageOptions? ImageSitemap { get; set; }
    /// <summary>Optional video sitemap generation options.</summary>
    public WebSitemapVideoOptions? VideoSitemap { get; set; }
    /// <summary>Optional sitemap index output path.</summary>
    public string? SitemapIndexPath { get; set; }
}

/// <summary>Options for specialized news sitemap output.</summary>
public sealed class WebSitemapNewsOptions
{
    /// <summary>Optional output path override for the news sitemap XML.</summary>
    public string? OutputPath { get; set; }
    /// <summary>Optional path patterns used to select entries for news sitemap output.</summary>
    public string[]? PathPatterns { get; set; }
    /// <summary>Optional publication name for news metadata.</summary>
    public string? PublicationName { get; set; }
    /// <summary>Optional publication language code for news metadata (for example: en).</summary>
    public string? PublicationLanguage { get; set; }
    /// <summary>Optional news genres metadata.</summary>
    public string? Genres { get; set; }
    /// <summary>Optional news access metadata.</summary>
    public string? Access { get; set; }
    /// <summary>Optional news keywords metadata.</summary>
    public string? Keywords { get; set; }
}

/// <summary>Options for specialized image sitemap output.</summary>
public sealed class WebSitemapImageOptions
{
    /// <summary>Optional output path override for the image sitemap XML.</summary>
    public string? OutputPath { get; set; }
    /// <summary>Optional path patterns used to select entries for image sitemap output.</summary>
    public string[]? PathPatterns { get; set; }
}

/// <summary>Options for specialized video sitemap output.</summary>
public sealed class WebSitemapVideoOptions
{
    /// <summary>Optional output path override for the video sitemap XML.</summary>
    public string? OutputPath { get; set; }
    /// <summary>Optional path patterns used to select entries for video sitemap output.</summary>
    public string[]? PathPatterns { get; set; }
}

/// <summary>Explicit sitemap entry metadata.</summary>
public sealed class WebSitemapEntry
{
    /// <summary>Route path (relative to base URL).</summary>
    public string Path { get; set; } = "/";
    /// <summary>Optional display title used by HTML sitemap renderers.</summary>
    public string? Title { get; set; }
    /// <summary>Optional description used by HTML sitemap renderers.</summary>
    public string? Description { get; set; }
    /// <summary>Optional section/group label used by HTML sitemap renderers.</summary>
    public string? Section { get; set; }
    /// <summary>Optional change frequency value.</summary>
    public string? ChangeFrequency { get; set; }
    /// <summary>Optional priority value.</summary>
    public string? Priority { get; set; }
    /// <summary>Optional last-modified date.</summary>
    public string? LastModified { get; set; }
    /// <summary>Optional localized alternate URLs for this path.</summary>
    public WebSitemapAlternate[] Alternates { get; set; } = Array.Empty<WebSitemapAlternate>();
    /// <summary>Optional page-associated image URLs (absolute or site-relative).</summary>
    public string[] ImageUrls { get; set; } = Array.Empty<string>();
    /// <summary>Optional page-associated video URLs (absolute or site-relative).</summary>
    public string[] VideoUrls { get; set; } = Array.Empty<string>();
    /// <summary>When true, page declares robots noindex metadata.</summary>
    public bool NoIndex { get; set; }
}

/// <summary>Localized alternate URL mapping for sitemap entries.</summary>
public sealed class WebSitemapAlternate
{
    /// <summary>Language code (for example en, pl, x-default).</summary>
    public string HrefLang { get; set; } = string.Empty;
    /// <summary>Route path relative to site root.</summary>
    public string Path { get; set; } = "/";
    /// <summary>Optional absolute URL override for this alternate.</summary>
    public string? Url { get; set; }
}

/// <summary>Generates sitemap.xml for the site output.</summary>
public static partial class WebSitemapGenerator
{
    private const string DefaultSitemapStylesheetHref = "/sitemap.css";
    private const string SitemapNamespaceUri = "http://www.sitemaps.org/schemas/sitemap/0.9";
    private const string XhtmlNamespaceUri = "http://www.w3.org/1999/xhtml";
    private const string XsiNamespaceUri = "http://www.w3.org/2001/XMLSchema-instance";
    private const string SitemapSchemaLocation = SitemapNamespaceUri + " http://www.sitemaps.org/schemas/sitemap/0.9/sitemap.xsd";
    private const string SitemapIndexSchemaLocation = SitemapNamespaceUri + " http://www.sitemaps.org/schemas/sitemap/0.9/siteindex.xsd";
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly XNamespace SitemapNs = SitemapNamespaceUri;
    private static readonly XNamespace XhtmlNs = XhtmlNamespaceUri;
    private static readonly XNamespace XsiNs = XsiNamespaceUri;

    private static readonly string[] DefaultExcludedHtmlPatterns =
    {
        "*.scripts.html",
        "**/*.scripts.html",
        "*.head.html",
        "**/*.head.html",
        "api-fragments/**",
        "**/api-fragments/**"
    };

    private static readonly string[] RobotsNoIndexNames = { "robots", "googlebot", "bingbot", "slurp" };

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
        var htmlRoutes = new List<string>();
        var localization = options.IncludeLanguageAlternates ? TryLoadLocalizationConfig(siteRoot) : null;
        var generateJson = options.GenerateJson || options.GenerateHtml;
        string? htmlOutputPath = null;
        string? jsonOutputPath = null;
        string? newsOutputPath = null;
        string? imageOutputPath = null;
        string? videoOutputPath = null;
        string? indexOutputPath = null;
        string? generatedHtmlRelativePath = null;
        var generateNewsSitemap = options.NewsSitemap is not null;
        var generateImageSitemap = options.ImageSitemap is not null;
        var generateVideoSitemap = options.VideoSitemap is not null;
        var generateSitemapIndex = options.SitemapIndexPath is not null;

        if (generateNewsSitemap)
        {
            newsOutputPath = string.IsNullOrWhiteSpace(options.NewsSitemap!.OutputPath)
                ? Path.Combine(siteRoot, "sitemap-news.xml")
                : Path.GetFullPath(options.NewsSitemap.OutputPath!);
        }
        if (generateSitemapIndex)
        {
            indexOutputPath = string.IsNullOrWhiteSpace(options.SitemapIndexPath)
                ? Path.Combine(siteRoot, "sitemap-index.xml")
                : Path.GetFullPath(options.SitemapIndexPath!);
        }
        if (generateImageSitemap)
        {
            imageOutputPath = string.IsNullOrWhiteSpace(options.ImageSitemap!.OutputPath)
                ? Path.Combine(siteRoot, "sitemap-images.xml")
                : Path.GetFullPath(options.ImageSitemap.OutputPath!);
        }
        if (generateVideoSitemap)
        {
            videoOutputPath = string.IsNullOrWhiteSpace(options.VideoSitemap!.OutputPath)
                ? Path.Combine(siteRoot, "sitemap-videos.xml")
                : Path.GetFullPath(options.VideoSitemap.OutputPath!);
        }

        if (options.GenerateHtml)
        {
            htmlOutputPath = string.IsNullOrWhiteSpace(options.HtmlOutputPath)
                ? Path.Combine(siteRoot, "sitemap", "index.html")
                : Path.GetFullPath(options.HtmlOutputPath);

            if (IsUnderRoot(siteRoot, htmlOutputPath))
                generatedHtmlRelativePath = Path.GetRelativePath(siteRoot, htmlOutputPath).Replace('\\', '/');
        }

        if (options.IncludeHtmlFiles)
        {
            var excludePatterns = BuildExcludePatterns(options);
            foreach (var file in Directory.EnumerateFiles(siteRoot, "*.html", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(siteRoot, file).Replace('\\', '/');
                if (!options.IncludeGeneratedHtmlRouteInXml &&
                    !string.IsNullOrWhiteSpace(generatedHtmlRelativePath) &&
                    string.Equals(relative, generatedHtmlRelativePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (relative.EndsWith("404.html", StringComparison.OrdinalIgnoreCase)) continue;
                if (IsExcludedHtml(relative, excludePatterns)) continue;
                if (!options.IncludeNoIndexHtml && HtmlDeclaresNoIndex(file)) continue;
                var route = NormalizeRoute(relative);
                if (string.IsNullOrWhiteSpace(route)) continue;
                var entry = BuildEntryFromHtmlFile(file, route);
                if (entry.NoIndex && !options.IncludeNoIndexHtml && !options.IncludeNoIndexPages)
                    continue;
                AddOrUpdate(entries, route, entry);
                htmlRoutes.Add(route);
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

        if (options.UseGeneratedSitemapMetadata)
        {
            foreach (var entry in LoadDefaultGeneratedSitemapEntries(siteRoot))
            {
                if (entry is null || string.IsNullOrWhiteSpace(entry.Path)) continue;
                var route = NormalizeRoute(entry.Path);
                if (!ShouldIncludeGeneratedSitemapEntry(siteRoot, route, entry, options))
                    continue;
                AddOrUpdate(entries, route, entry);
                htmlRoutes.Add(route);
            }
        }

        if (options.Entries is not null)
        {
            foreach (var entry in options.Entries)
            {
                if (entry is null || string.IsNullOrWhiteSpace(entry.Path)) continue;
                AddOrUpdate(entries, NormalizeRoute(entry.Path), entry);
            }
        }
        if (!string.IsNullOrWhiteSpace(options.EntriesJsonPath))
        {
            foreach (var entry in LoadEntriesFromJsonPath(options.EntriesJsonPath))
            {
                if (entry is null || string.IsNullOrWhiteSpace(entry.Path)) continue;
                AddOrUpdate(entries, NormalizeRoute(entry.Path), entry);
            }
        }

        if (!string.IsNullOrWhiteSpace(options.ApiSitemapPath))
            MergeApiSitemap(options.ApiSitemapPath, baseUrl, entries);

        if (options.GenerateHtml)
        {
            if (options.IncludeGeneratedHtmlRouteInXml &&
                !string.IsNullOrWhiteSpace(generatedHtmlRelativePath))
            {
                AddOrUpdate(entries, NormalizeRoute(generatedHtmlRelativePath), null);
            }
        }
        if (generateJson)
        {
            jsonOutputPath = string.IsNullOrWhiteSpace(options.JsonOutputPath)
                ? Path.Combine(siteRoot, "sitemap", "index.json")
                : Path.GetFullPath(options.JsonOutputPath);
        }

        CollapseHtmlRouteAliases(entries);

        if (options.IncludeLanguageAlternates && localization is not null)
            ApplyLanguageAlternates(entries, htmlRoutes, localization, baseUrl);

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var ns = SitemapNs;
        var xhtmlNs = XhtmlNs;
        var orderedEntries = entries.Values
            .OrderBy(u => u.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var warnings = AnalyzeLastModifiedSignals(orderedEntries, today);
        var entriesXml = orderedEntries
            .Select(u => BuildEntry(baseUrl, u, ns, xhtmlNs))
            .ToArray();
        var browserStylesheetHref = ResolveSitemapBrowserStylesheetHref(options);
        var rootAttributes = new List<object>(CreateSchemaAttributes(SitemapSchemaLocation));
        if (entriesXml.Any(entry => entry.Descendants(xhtmlNs + "link").Any()))
            rootAttributes.Add(new XAttribute(XNamespace.Xmlns + "xhtml", xhtmlNs.NamespaceName));

        var doc = CreateSitemapDocument(
            browserStylesheetHref,
            new XElement(
                ns + "urlset",
                rootAttributes,
                entriesXml));

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? siteRoot);
        using (var stream = File.Create(outputPath))
        {
            doc.Save(stream);
        }
        WriteSitemapBrowserStylesheet(siteRoot, browserStylesheetHref);

        if (generateNewsSitemap && !string.IsNullOrWhiteSpace(newsOutputPath))
        {
            WriteNewsSitemap(
                siteRoot,
                baseUrl,
                options.NewsSitemap!,
                orderedEntries,
                browserStylesheetHref,
                today,
                newsOutputPath);
        }
        if (generateImageSitemap && !string.IsNullOrWhiteSpace(imageOutputPath))
        {
            WriteImageSitemap(
                siteRoot,
                baseUrl,
                options.ImageSitemap!,
                orderedEntries,
                browserStylesheetHref,
                imageOutputPath);
        }
        if (generateVideoSitemap && !string.IsNullOrWhiteSpace(videoOutputPath))
        {
            WriteVideoSitemap(
                siteRoot,
                baseUrl,
                options.VideoSitemap!,
                orderedEntries,
                browserStylesheetHref,
                videoOutputPath);
        }

        if (generateSitemapIndex && !string.IsNullOrWhiteSpace(indexOutputPath))
        {
            WriteSitemapIndex(
                siteRoot,
                baseUrl,
                indexOutputPath,
                browserStylesheetHref,
                today,
                outputPath,
                newsOutputPath,
                imageOutputPath,
                videoOutputPath);
        }

        if (generateJson && !string.IsNullOrWhiteSpace(jsonOutputPath))
            WriteSitemapJson(baseUrl, orderedEntries, jsonOutputPath);

        if (options.GenerateHtml && !string.IsNullOrWhiteSpace(htmlOutputPath))
        {
            var html = RenderHtmlSitemap(baseUrl, options, orderedEntries);
            Directory.CreateDirectory(Path.GetDirectoryName(htmlOutputPath) ?? siteRoot);
            File.WriteAllText(htmlOutputPath, html, Encoding.UTF8);
        }

        return new WebSitemapResult
        {
            OutputPath = outputPath,
            NewsOutputPath = newsOutputPath,
            ImageOutputPath = imageOutputPath,
            VideoOutputPath = videoOutputPath,
            IndexOutputPath = indexOutputPath,
            JsonOutputPath = jsonOutputPath,
            HtmlOutputPath = htmlOutputPath,
            UrlCount = entriesXml.Length,
            LastModifiedCount = orderedEntries.Count(static entry => !string.IsNullOrWhiteSpace(entry.LastModified)),
            Warnings = warnings
        };
    }

    private static string? ResolveSitemapBrowserStylesheetHref(WebSitemapOptions options)
    {
        if (!options.GenerateBrowserStylesheet)
            return null;

        var href = string.IsNullOrWhiteSpace(options.BrowserStylesheetHref)
            ? DefaultSitemapStylesheetHref
            : options.BrowserStylesheetHref.Trim();
        if (href.Contains('"') || href.Contains("?>", StringComparison.Ordinal))
            throw new ArgumentException("BrowserStylesheetHref cannot contain double quotes or processing instruction terminators.", nameof(options));

        if (href.StartsWith("/", StringComparison.Ordinal))
            return href;

        if (Uri.TryCreate(href, UriKind.Absolute, out var absoluteUri))
        {
            if (absoluteUri.Scheme != Uri.UriSchemeHttp && absoluteUri.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException("BrowserStylesheetHref absolute URLs must use http or https.", nameof(options));

            return href;
        }

        return href.StartsWith("/", StringComparison.Ordinal) ? href : "/" + href;
    }

    private static XDocument CreateSitemapDocument(string? browserStylesheetHref, XElement root)
    {
        return string.IsNullOrWhiteSpace(browserStylesheetHref)
            ? new XDocument(new XDeclaration("1.0", "UTF-8", null), root)
            : new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XProcessingInstruction(
                    "xml-stylesheet",
                    $"type=\"text/css\" href=\"{browserStylesheetHref}\""),
                root);
    }

    private static XAttribute[] CreateSchemaAttributes(string schemaLocation)
    {
        return new[]
        {
            new XAttribute(XNamespace.Xmlns + "xsi", XsiNs.NamespaceName),
            new XAttribute(XsiNs + "schemaLocation", schemaLocation)
        };
    }

    private static void WriteSitemapBrowserStylesheet(string siteRoot, string? browserStylesheetHref)
    {
        if (!TryResolveRootRelativeHref(siteRoot, browserStylesheetHref, out var outputPath))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? siteRoot);
        File.WriteAllText(
            outputPath,
            """
            urlset, sitemapindex {
              display: block;
              min-height: 100vh;
              box-sizing: border-box;
              padding: 28px;
              background: #f5f7fb;
              color: #1f2937;
              counter-reset: sitemap-entry;
              font-family: "Segoe UI", Roboto, Arial, sans-serif;
              line-height: 1.45;
            }
            urlset::before {
              content: "XML sitemap";
              display: block;
              max-width: 1120px;
              margin: 0 auto 6px;
              color: #0f172a;
              font-size: 30px;
              font-weight: 700;
            }
            sitemapindex::before {
              content: "XML sitemap index";
              display: block;
              max-width: 1120px;
              margin: 0 auto 6px;
              color: #0f172a;
              font-size: 30px;
              font-weight: 700;
            }
            urlset::after {
              content: "Generated for search engines. Styled only for human inspection.";
              display: block;
              max-width: 1120px;
              margin: 4px auto 22px;
              color: #64748b;
              font-size: 14px;
            }
            sitemapindex::after {
              content: "Sitemap index. Each entry points to a machine-readable sitemap.";
              display: block;
              max-width: 1120px;
              margin: 4px auto 22px;
              color: #64748b;
              font-size: 14px;
            }
            url, sitemap {
              counter-increment: sitemap-entry;
              display: block;
              position: relative;
              max-width: 1120px;
              margin: 0 auto 12px;
              padding: 16px 18px 16px 58px;
              border: 1px solid #d9e2ef;
              border-left: 4px solid #2563eb;
              border-radius: 10px;
              background: #ffffff;
              box-shadow: 0 1px 3px rgba(15, 23, 42, 0.06);
            }
            url::before, sitemap::before {
              content: counter(sitemap-entry);
              position: absolute;
              top: 16px;
              left: 16px;
              display: inline-flex;
              width: 26px;
              height: 26px;
              align-items: center;
              justify-content: center;
              border-radius: 999px;
              background: #eff6ff;
              color: #1d4ed8;
              font-size: 12px;
              font-weight: 700;
            }
            loc {
              display: block;
              margin: 0 0 10px;
              color: #0f5f8f;
              font-family: "Cascadia Mono", Consolas, monospace;
              font-size: 13px;
              font-weight: 600;
              word-break: break-all;
            }
            lastmod, changefreq, priority {
              display: inline-block;
              margin: 0 10px 8px 0;
              padding: 4px 9px;
              border-radius: 999px;
              background: #f1f5f9;
              color: #475569;
              font-size: 12px;
            }
            lastmod::before { content: "Last modified: "; font-weight: 600; color: #334155; }
            changefreq::before { content: "Change frequency: "; font-weight: 600; color: #334155; }
            priority::before { content: "Priority: "; font-weight: 600; color: #334155; }
            link {
              display: inline-block;
              margin: 0 8px 8px 0;
              padding: 3px 8px;
              border: 1px solid #dbe4ef;
              border-radius: 999px;
              color: #64748b;
              font-size: 12px;
            }
            link::before {
              content: attr(hreflang);
              font-weight: 600;
            }
            @media (max-width: 720px) {
              urlset, sitemapindex {
                padding: 18px;
              }
              url, sitemap {
                padding: 52px 14px 14px;
              }
              url::before, sitemap::before {
                top: 14px;
                left: 14px;
              }
            }
            """,
            Utf8NoBom);
    }

    private static bool TryResolveRootRelativeHref(string siteRoot, string? href, out string outputPath)
    {
        outputPath = string.Empty;
        if (string.IsNullOrWhiteSpace(href))
            return false;

        var trimmed = href.Trim();
        if (!trimmed.StartsWith("/", StringComparison.Ordinal) &&
            Uri.TryCreate(trimmed, UriKind.Absolute, out _))
            return false;

        var pathOnly = trimmed.Split('?', '#')[0].TrimStart('/');
        if (string.IsNullOrWhiteSpace(pathOnly))
            return false;

        var candidate = Path.GetFullPath(Path.Combine(siteRoot, pathOnly.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsUnderRoot(siteRoot, candidate))
            return false;

        outputPath = candidate;
        return true;
    }

    private static string NormalizeRoute(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "/";
        var trimmed = path.Replace('\\', '/').Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri) &&
            (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps))
        {
            trimmed = absoluteUri.AbsolutePath + absoluteUri.Query;
        }
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

    private static void ApplyLanguageAlternates(
        Dictionary<string, WebSitemapEntry> entries,
        IEnumerable<string> htmlRoutes,
        ResolvedLocalizationConfig localization,
        string fallbackBaseUrl)
    {
        if (entries is null || htmlRoutes is null || localization is null || !localization.Enabled)
            return;

        var routeInfos = htmlRoutes
            .Where(static route => !string.IsNullOrWhiteSpace(route))
            .Select(route => ResolveLocalizedRouteInfo(route, localization))
            .ToArray();
        if (routeInfos.Length == 0)
            return;

        var grouped = routeInfos
            .GroupBy(static info => info.BaseRoute, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var group in grouped)
        {
            var byLanguage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in group.OrderBy(static info => info.Route, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(info.Language) || string.IsNullOrWhiteSpace(info.Route))
                    continue;
                if (!byLanguage.ContainsKey(info.Language))
                    byLanguage[info.Language] = info.Route;
            }

            if (byLanguage.Count <= 1)
                continue;

            foreach (var info in group)
            {
                if (!entries.TryGetValue(info.Route, out var entry))
                    continue;

                var alternates = byLanguage
                    .Select(pair => new WebSitemapAlternate
                    {
                        HrefLang = pair.Key,
                        Path = pair.Value,
                        Url = ResolveLocalizedAlternateUrl(localization, pair.Key, pair.Value, fallbackBaseUrl)
                    })
                    .ToList();

                if (!string.IsNullOrWhiteSpace(localization.DefaultLanguage) &&
                    byLanguage.TryGetValue(localization.DefaultLanguage, out var defaultRoute))
                {
                    alternates.Add(new WebSitemapAlternate
                    {
                        HrefLang = "x-default",
                        Path = defaultRoute,
                        Url = ResolveLocalizedAlternateUrl(localization, localization.DefaultLanguage, defaultRoute, fallbackBaseUrl)
                    });
                }

                entry.Alternates = alternates
                    .GroupBy(static alt => $"{alt.HrefLang}|{alt.Path}", StringComparer.OrdinalIgnoreCase)
                    .Select(static groupAlt => groupAlt.First())
                    .OrderBy(static alt => alt.HrefLang, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    private static string ResolveLocalizedAlternateUrl(
        ResolvedLocalizationConfig localization,
        string languageCode,
        string route,
        string fallbackBaseUrl)
    {
        var baseUrl = fallbackBaseUrl;
        if (localization.LanguageBaseUrlsByCode.TryGetValue(languageCode, out var localizedBaseUrl) &&
            !string.IsNullOrWhiteSpace(localizedBaseUrl))
        {
            baseUrl = localizedBaseUrl;
        }

        if (localization.RenderAtRootByCode.TryGetValue(languageCode, out var renderAtRoot) && renderAtRoot)
            route = StripLocalizedRoutePrefix(route, localization, languageCode);

        return ResolveAbsoluteUrl(baseUrl, route);
    }

    private static string StripLocalizedRoutePrefix(string route, ResolvedLocalizationConfig localization, string languageCode)
    {
        var normalizedRoute = NormalizeRoute(route);
        var normalizedLanguage = NormalizeLanguageToken(languageCode);
        if (string.IsNullOrWhiteSpace(normalizedLanguage))
            return normalizedRoute;

        var prefix = localization.ByPrefix
            .FirstOrDefault(pair => string.Equals(pair.Value, normalizedLanguage, StringComparison.OrdinalIgnoreCase))
            .Key;
        if (string.IsNullOrWhiteSpace(prefix))
            return normalizedRoute;

        var prefixedRoot = "/" + prefix;
        if (normalizedRoute.Equals(prefixedRoot, StringComparison.OrdinalIgnoreCase) ||
            normalizedRoute.Equals(prefixedRoot + "/", StringComparison.OrdinalIgnoreCase))
            return "/";

        if (normalizedRoute.StartsWith(prefixedRoot + "/", StringComparison.OrdinalIgnoreCase))
            return NormalizeRoute(normalizedRoute.Substring(prefixedRoot.Length));

        return normalizedRoute;
    }

    private static LocalizedRouteInfo ResolveLocalizedRouteInfo(string route, ResolvedLocalizationConfig localization)
    {
        var normalizedRoute = NormalizeRoute(route);
        if (!localization.Enabled)
        {
            return new LocalizedRouteInfo
            {
                Route = normalizedRoute,
                BaseRoute = normalizedRoute,
                Language = localization.DefaultLanguage
            };
        }

        var trimmed = normalizedRoute.Trim('/');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return new LocalizedRouteInfo
            {
                Route = normalizedRoute,
                BaseRoute = normalizedRoute,
                Language = localization.DefaultLanguage
            };
        }

        var slash = trimmed.IndexOf('/', StringComparison.Ordinal);
        var firstSegment = slash < 0 ? trimmed : trimmed.Substring(0, slash);
        if (localization.ByPrefix.TryGetValue(firstSegment, out var languageCode))
        {
            var remainder = slash < 0 ? "/" : "/" + trimmed.Substring(slash + 1);
            if (normalizedRoute.EndsWith("/", StringComparison.Ordinal) &&
                !remainder.EndsWith("/", StringComparison.Ordinal))
            {
                remainder += "/";
            }
            var normalizedRemainder = NormalizeRoute(remainder);
            return new LocalizedRouteInfo
            {
                Route = normalizedRoute,
                BaseRoute = normalizedRemainder,
                Language = languageCode
            };
        }

        return new LocalizedRouteInfo
        {
            Route = normalizedRoute,
            BaseRoute = normalizedRoute,
            Language = localization.DefaultLanguage
        };
    }

    private static ResolvedLocalizationConfig? TryLoadLocalizationConfig(string siteRoot)
    {
        if (string.IsNullOrWhiteSpace(siteRoot))
            return null;

        try
        {
            var specPath = Path.Combine(siteRoot, "_powerforge", "site-spec.json");
            if (!File.Exists(specPath))
                return null;

            var spec = JsonSerializer.Deserialize<SiteSpec>(File.ReadAllText(specPath), WebJson.Options);
            if (spec is null)
                return null;

            return ResolveLocalizationConfig(spec);
        }
        catch
        {
            return null;
        }
    }

    private static ResolvedLocalizationConfig ResolveLocalizationConfig(SiteSpec spec)
    {
        var localizationSpec = spec.Localization;
        var defaultLanguage = NormalizeLanguageToken(localizationSpec?.DefaultLanguage);
        if (string.IsNullOrWhiteSpace(defaultLanguage))
            defaultLanguage = "en";

        var byPrefix = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var languageBaseUrlsByCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var renderAtRootByCode = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (localizationSpec?.Languages is { Length: > 0 })
        {
            foreach (var language in localizationSpec.Languages)
            {
                if (language is null || language.Disabled || string.IsNullOrWhiteSpace(language.Code))
                    continue;
                var code = NormalizeLanguageToken(language.Code);
                if (string.IsNullOrWhiteSpace(code))
                    continue;
                var prefix = NormalizeLanguageToken(string.IsNullOrWhiteSpace(language.Prefix) ? code : language.Prefix);
                if (string.IsNullOrWhiteSpace(prefix))
                    continue;
                if (!byPrefix.ContainsKey(prefix))
                    byPrefix[prefix] = code;

                var languageBaseUrl = NormalizeAbsoluteBaseUrl(language.BaseUrl);
                if (!string.IsNullOrWhiteSpace(languageBaseUrl))
                    languageBaseUrlsByCode[code] = languageBaseUrl;

                renderAtRootByCode[code] = language.RenderAtRoot ||
                                           (language.Default && localizationSpec.PrefixDefaultLanguage == false);
            }
        }

        return new ResolvedLocalizationConfig
        {
            Enabled = localizationSpec?.Enabled == true && byPrefix.Count > 0,
            DefaultLanguage = defaultLanguage,
            ByPrefix = byPrefix,
            LanguageBaseUrlsByCode = languageBaseUrlsByCode,
            RenderAtRootByCode = renderAtRootByCode
        };
    }

    private static string NormalizeLanguageToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return value.Trim().Replace('_', '-').Trim('/').ToLowerInvariant();
    }

    private static string? NormalizeAbsoluteBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return null;
        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return null;
        return $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath.TrimEnd('/')}";
    }

    private static string ResolveAbsoluteUrl(string baseUrl, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return baseUrl.TrimEnd('/');
        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             absolute.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            return path;

        var normalizedBase = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        var normalizedPath = path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;
        return normalizedBase + normalizedPath;
    }

    private static XElement BuildEntry(string baseUrl, WebSitemapEntry entry, XNamespace sitemapNs, XNamespace xhtmlNs)
    {
        var path = string.IsNullOrWhiteSpace(entry.Path) ? "/" : entry.Path;
        var loc = baseUrl + (path.StartsWith("/") ? path : "/" + path);
        var changefreq = string.IsNullOrWhiteSpace(entry.ChangeFrequency) ? "monthly" : entry.ChangeFrequency;
        var priority = string.IsNullOrWhiteSpace(entry.Priority)
            ? (path == "/" ? "1.0" : "0.5")
            : entry.Priority;

        var children = new List<object>
        {
            new XElement(sitemapNs + "loc", loc)
        };
        if (!string.IsNullOrWhiteSpace(entry.LastModified))
            children.Add(new XElement(sitemapNs + "lastmod", entry.LastModified));
        children.Add(new XElement(sitemapNs + "changefreq", changefreq));
        children.Add(new XElement(sitemapNs + "priority", priority));

        var urlElement = new XElement(sitemapNs + "url", children);

        if (entry.Alternates is { Length: > 0 })
        {
            foreach (var alternate in entry.Alternates
                         .Where(static value =>
                             !string.IsNullOrWhiteSpace(value.HrefLang) &&
                             (!string.IsNullOrWhiteSpace(value.Path) || !string.IsNullOrWhiteSpace(value.Url)))
                         .OrderBy(static value => value.HrefLang, StringComparer.OrdinalIgnoreCase))
            {
                var href = !string.IsNullOrWhiteSpace(alternate.Url)
                    ? alternate.Url!
                    : baseUrl + (alternate.Path.StartsWith("/") ? alternate.Path : "/" + alternate.Path);
                urlElement.Add(
                    new XElement(
                        xhtmlNs + "link",
                        new XAttribute("rel", "alternate"),
                        new XAttribute("hreflang", alternate.HrefLang),
                        new XAttribute("href", href)));
            }
        }

        return urlElement;
    }

    private static void MergeApiSitemap(string apiSitemapPath, string baseUrl, Dictionary<string, WebSitemapEntry> entries)
    {
        var full = Path.GetFullPath(apiSitemapPath);
        if (!File.Exists(full)) return;

        try
        {
            var doc = LoadXmlSafe(full);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            foreach (var url in doc.Descendants(ns + "url"))
            {
                var loc = url.Element(ns + "loc")?.Value;
                if (string.IsNullOrWhiteSpace(loc)) continue;
                var normalized = ReplaceHost(loc, baseUrl);
                var merged = new WebSitemapEntry
                {
                    LastModified = url.Element(ns + "lastmod")?.Value,
                    ChangeFrequency = url.Element(ns + "changefreq")?.Value,
                    Priority = url.Element(ns + "priority")?.Value
                };
                if (normalized.StartsWith(baseUrl, StringComparison.OrdinalIgnoreCase))
                {
                    var path = normalized.Substring(baseUrl.Length);
                    AddOrUpdate(entries, string.IsNullOrWhiteSpace(path) ? "/" : path, merged);
                }
                else
                {
                    if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        continue;
                    AddOrUpdate(entries, normalized, merged);
                }
            }
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Failed to merge API sitemap {full}: {ex.GetType().Name}: {ex.Message}");
            return;
        }
    }

    private static XDocument LoadXmlSafe(string path)
    {
        using var stream = File.OpenRead(path);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            CloseInput = false
        };
        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, LoadOptions.None);
    }

    private static IEnumerable<WebSitemapEntry> LoadDefaultGeneratedSitemapEntries(string siteRoot)
    {
        var path = Path.Combine(siteRoot, "_powerforge", "sitemap-entries.json");
        if (!File.Exists(path))
            return Array.Empty<WebSitemapEntry>();

        try
        {
            return LoadEntriesFromJsonPath(path).ToArray();
        }
        catch (Exception ex) when (ex is IOException or JsonException or FormatException)
        {
            Trace.TraceWarning($"Failed to load generated sitemap metadata {path}: {ex.GetType().Name}: {ex.Message}");
            return Array.Empty<WebSitemapEntry>();
        }
    }

    private static string[] AnalyzeLastModifiedSignals(IReadOnlyList<WebSitemapEntry> entries, string today)
    {
        if (entries.Count < 10)
            return Array.Empty<string>();

        var values = entries
            .Select(static entry => entry.LastModified)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => ExtractSitemapDate(value!))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        if (values.Length == 0)
            return Array.Empty<string>();

        var warnings = new List<string>();
        var groups = values
            .GroupBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .Select(static group => new { Date = group.Key, Count = group.Count() })
            .OrderByDescending(static group => group.Count)
            .ToArray();
        var dominant = groups[0];
        var ratio = (double)dominant.Count / entries.Count;
        if (dominant.Date.Equals(today, StringComparison.OrdinalIgnoreCase) && ratio >= 0.80)
        {
            warnings.Add(
                $"sitemap lastmod freshness looks suspicious: {dominant.Count}/{entries.Count} URLs are stamped with build date {today}. Use content metadata, source git dates, or omit lastmod for unknown pages.");
        }

        return warnings.ToArray();
    }

    private static string ExtractSitemapDate(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 10)
            return trimmed[..10];
        return trimmed;
    }

    private static void AddOrUpdate(Dictionary<string, WebSitemapEntry> entries, string path, WebSitemapEntry? update)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var normalized = NormalizeRoute(path);
        if (entries.TryGetValue(normalized, out var existing))
        {
            if (update is null) return;
            if (!string.IsNullOrWhiteSpace(update.Title))
                existing.Title = update.Title;
            if (!string.IsNullOrWhiteSpace(update.Description))
                existing.Description = update.Description;
            if (!string.IsNullOrWhiteSpace(update.Section))
                existing.Section = update.Section;
            if (!string.IsNullOrWhiteSpace(update.ChangeFrequency))
                existing.ChangeFrequency = update.ChangeFrequency;
            if (!string.IsNullOrWhiteSpace(update.Priority))
                existing.Priority = update.Priority;
            if (!string.IsNullOrWhiteSpace(update.LastModified))
                existing.LastModified = update.LastModified;
            if (update.Alternates is { Length: > 0 })
                existing.Alternates = update.Alternates;
            if (update.ImageUrls is { Length: > 0 })
                existing.ImageUrls = update.ImageUrls;
            if (update.VideoUrls is { Length: > 0 })
                existing.VideoUrls = update.VideoUrls;
            if (update.NoIndex)
                existing.NoIndex = true;
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
            Title = update.Title,
            Description = update.Description,
            Section = update.Section,
            ChangeFrequency = update.ChangeFrequency,
            Priority = update.Priority,
            LastModified = update.LastModified,
            Alternates = update.Alternates,
            ImageUrls = update.ImageUrls,
            VideoUrls = update.VideoUrls,
            NoIndex = update.NoIndex
        };
    }

    private static void CollapseHtmlRouteAliases(Dictionary<string, WebSitemapEntry> entries)
    {
        if (entries.Count == 0)
            return;

        var htmlKeys = entries.Keys
            .Where(static key => key.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            .Where(static key => !key.EndsWith("/404.html", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var htmlKey in htmlKeys)
        {
            if (!entries.TryGetValue(htmlKey, out var htmlEntry))
                continue;

            var slashAlias = ToSlashAliasRoute(htmlKey);
            if (string.IsNullOrWhiteSpace(slashAlias))
                continue;

            if (entries.TryGetValue(slashAlias, out var canonicalEntry))
            {
                MergeEntryMetadata(canonicalEntry, htmlEntry);
                entries.Remove(htmlKey);
                continue;
            }

            // API docs generated in docs mode can still surface legacy *.html aliases
            // (for backward-compatible redirects/bookmarks). Prefer slash canonical URLs
            // in sitemap output even when only the alias route was merged.
            if (!IsApiLegacyHtmlAliasRoute(htmlKey))
                continue;

            entries[slashAlias] = new WebSitemapEntry
            {
                Path = slashAlias,
                Title = htmlEntry.Title,
                Description = htmlEntry.Description,
                Section = htmlEntry.Section,
                ChangeFrequency = htmlEntry.ChangeFrequency,
                Priority = htmlEntry.Priority,
                LastModified = htmlEntry.LastModified,
                Alternates = htmlEntry.Alternates,
                ImageUrls = htmlEntry.ImageUrls,
                VideoUrls = htmlEntry.VideoUrls,
                NoIndex = htmlEntry.NoIndex
            };
            entries.Remove(htmlKey);
        }
    }

    private static string ToSlashAliasRoute(string htmlRoute)
    {
        if (string.IsNullOrWhiteSpace(htmlRoute))
            return string.Empty;
        if (!htmlRoute.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var withoutExtension = htmlRoute[..^".html".Length];
        if (string.IsNullOrWhiteSpace(withoutExtension) || withoutExtension == "/")
            return "/";
        if (!withoutExtension.EndsWith("/", StringComparison.Ordinal))
            withoutExtension += "/";

        return NormalizeRoute(withoutExtension);
    }

    private static bool IsApiLegacyHtmlAliasRoute(string route)
    {
        if (string.IsNullOrWhiteSpace(route) ||
            !route.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalized = NormalizeRoute(route).ToLowerInvariant();
        if (normalized.EndsWith("/index.html", StringComparison.Ordinal) ||
            normalized.EndsWith("/404.html", StringComparison.Ordinal))
        {
            return false;
        }

        if (normalized.IndexOf("/api/", StringComparison.Ordinal) < 0)
            return false;

        // Keep simple-template API routes (/api/types/*.html) untouched.
        if (normalized.Contains("/types/", StringComparison.Ordinal))
            return false;

        return true;
    }

    private static void MergeEntryMetadata(WebSitemapEntry destination, WebSitemapEntry source)
    {
        if (destination is null || source is null)
            return;

        if (string.IsNullOrWhiteSpace(destination.Title) && !string.IsNullOrWhiteSpace(source.Title))
            destination.Title = source.Title;
        if (string.IsNullOrWhiteSpace(destination.Description) && !string.IsNullOrWhiteSpace(source.Description))
            destination.Description = source.Description;
        if (string.IsNullOrWhiteSpace(destination.Section) && !string.IsNullOrWhiteSpace(source.Section))
            destination.Section = source.Section;
        if (string.IsNullOrWhiteSpace(destination.ChangeFrequency) && !string.IsNullOrWhiteSpace(source.ChangeFrequency))
            destination.ChangeFrequency = source.ChangeFrequency;
        if (string.IsNullOrWhiteSpace(destination.Priority) && !string.IsNullOrWhiteSpace(source.Priority))
            destination.Priority = source.Priority;
        if (string.IsNullOrWhiteSpace(destination.LastModified) && !string.IsNullOrWhiteSpace(source.LastModified))
            destination.LastModified = source.LastModified;
        if ((destination.Alternates is null || destination.Alternates.Length == 0) &&
            source.Alternates is { Length: > 0 })
        {
            destination.Alternates = source.Alternates;
        }
        if ((destination.ImageUrls is null || destination.ImageUrls.Length == 0) &&
            source.ImageUrls is { Length: > 0 })
        {
            destination.ImageUrls = source.ImageUrls;
        }
        if ((destination.VideoUrls is null || destination.VideoUrls.Length == 0) &&
            source.VideoUrls is { Length: > 0 })
        {
            destination.VideoUrls = source.VideoUrls;
        }
        if (source.NoIndex)
            destination.NoIndex = true;
    }

    private static bool ShouldIncludeGeneratedSitemapEntry(
        string siteRoot,
        string route,
        WebSitemapEntry entry,
        WebSitemapOptions options)
    {
        if (string.IsNullOrWhiteSpace(route))
            return false;
        if (entry.NoIndex && !options.IncludeNoIndexHtml && !options.IncludeNoIndexPages)
            return false;
        // Re-check the rendered HTML because metadata files from older engine
        // versions did not carry NoIndex, and stale/custom metadata may omit it.
        if (!TryResolveHtmlFileForRoute(siteRoot, route, out var htmlPath))
            return false;
        if (!options.IncludeNoIndexHtml &&
            !options.IncludeNoIndexPages &&
            HtmlDeclaresNoIndex(htmlPath))
        {
            return false;
        }

        return true;
    }

    private static bool TryResolveHtmlFileForRoute(string siteRoot, string route, out string htmlPath)
    {
        htmlPath = string.Empty;
        if (string.IsNullOrWhiteSpace(siteRoot) || string.IsNullOrWhiteSpace(route))
            return false;

        var trimmed = NormalizeRoute(route).Trim('/');
        var relative = string.IsNullOrWhiteSpace(trimmed)
            ? "index.html"
            : trimmed.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : Path.Combine(trimmed.Replace('/', Path.DirectorySeparatorChar), "index.html");

        var fullPath = Path.GetFullPath(Path.Combine(siteRoot, relative));
        if (!IsUnderRoot(siteRoot, fullPath) || !File.Exists(fullPath))
            return false;

        htmlPath = fullPath;
        return true;
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

    private static bool IsUnderRoot(string root, string fullPath)
    {
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var targetFull = Path.GetFullPath(fullPath);
        return targetFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] BuildExcludePatterns(WebSitemapOptions options)
    {
        var patterns = new List<string>();
        if (options.UseDefaultExcludePatterns)
            patterns.AddRange(DefaultExcludedHtmlPatterns);

        if (options.ExcludePatterns is { Length: > 0 })
            patterns.AddRange(options.ExcludePatterns.Where(static pattern => !string.IsNullOrWhiteSpace(pattern)));

        return patterns
            .Select(static pattern => pattern.Trim())
            .Where(static pattern => !string.IsNullOrWhiteSpace(pattern))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsExcludedHtml(string relativePath, string[] patterns)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || patterns is null || patterns.Length == 0)
            return false;

        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;
            if (GlobMatch(pattern, normalized))
                return true;
        }

        return false;
    }

    private static bool HtmlDeclaresNoIndex(string filePath)
    {
        string html;
        try
        {
            html = File.ReadAllText(filePath);
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(html) ||
            html.IndexOf("noindex", StringComparison.OrdinalIgnoreCase) < 0 ||
            html.IndexOf("<meta", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        var start = 0;
        while (true)
        {
            var metaStart = html.IndexOf("<meta", start, StringComparison.OrdinalIgnoreCase);
            if (metaStart < 0)
                break;

            var metaEnd = html.IndexOf('>', metaStart);
            if (metaEnd < 0)
                break;

            var tag = html.Substring(metaStart, metaEnd - metaStart + 1);
            if (tag.IndexOf("noindex", StringComparison.OrdinalIgnoreCase) >= 0 &&
                RobotsNoIndexNames.Any(name => MetaTagHasName(tag, name)))
            {
                return true;
            }

            start = metaEnd + 1;
        }

        return false;
    }

    private static bool MetaTagHasName(string tag, string name)
    {
        if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(name))
            return false;

        var needle = name.Trim();
        if (needle.Length == 0)
            return false;

        var pattern = $@"\bname\s*=\s*(?:""{Regex.Escape(needle)}""|'{Regex.Escape(needle)}'|{Regex.Escape(needle)})(?=\s|/|>)";
        return Regex.IsMatch(tag, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool GlobMatch(string pattern, string value)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(value))
            return false;

        var normalizedPattern = pattern.Replace('\\', '/').Trim().TrimStart('/');
        var normalizedValue = value.Replace('\\', '/').Trim().TrimStart('/');

        var regexPattern = "^" + Regex.Escape(normalizedPattern)
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*")
            .Replace("\\?", ".") + "$";
        return Regex.IsMatch(normalizedValue, regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private sealed class ResolvedLocalizationConfig
    {
        public bool Enabled { get; init; }
        public string DefaultLanguage { get; init; } = "en";
        public Dictionary<string, string> ByPrefix { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> LanguageBaseUrlsByCode { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, bool> RenderAtRootByCode { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class LocalizedRouteInfo
    {
        public string Route { get; init; } = "/";
        public string BaseRoute { get; init; } = "/";
        public string Language { get; init; } = "en";
    }
}
