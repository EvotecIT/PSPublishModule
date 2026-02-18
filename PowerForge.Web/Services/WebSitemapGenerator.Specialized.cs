using System.Globalization;
using System.Xml.Linq;

namespace PowerForge.Web;

public static partial class WebSitemapGenerator
{
    private static readonly string[] DefaultNewsPathPatterns = { "**/news/**", "news", "news/" };
    private static readonly string[] DefaultImagePathPatterns = { "**" };
    private static readonly string[] DefaultVideoPathPatterns = { "**" };

    private static void WriteNewsSitemap(
        string siteRoot,
        string baseUrl,
        WebSitemapNewsOptions options,
        IReadOnlyList<WebSitemapEntry> entries,
        string defaultLastModified,
        string outputPath)
    {
        var patterns = BuildNewsPathPatterns(options.PathPatterns);
        var selectedEntries = entries
            .Where(entry => MatchesAnyPathPattern(entry.Path, patterns))
            .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var sitemapNs = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
        var newsNs = XNamespace.Get("http://www.google.com/schemas/sitemap-news/0.9");
        var publicationName = ResolveNewsPublicationName(baseUrl, options.PublicationName);
        var publicationLanguage = string.IsNullOrWhiteSpace(options.PublicationLanguage)
            ? "en"
            : options.PublicationLanguage.Trim();

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(
                sitemapNs + "urlset",
                new XAttribute(XNamespace.Xmlns + "news", newsNs.NamespaceName),
                selectedEntries.Select(entry =>
                {
                    var route = NormalizeRoute(entry.Path);
                    var loc = ResolveAbsoluteUrl(baseUrl, route);
                    var title = string.IsNullOrWhiteSpace(entry.Title) ? BuildTitleFromRoute(route) : entry.Title!;
                    var publicationDate = ResolveNewsPublicationDate(entry.LastModified, defaultLastModified);

                    var news = new XElement(
                        newsNs + "news",
                        new XElement(
                            newsNs + "publication",
                            new XElement(newsNs + "name", publicationName),
                            new XElement(newsNs + "language", publicationLanguage)),
                        new XElement(newsNs + "publication_date", publicationDate),
                        new XElement(newsNs + "title", title));

                    if (!string.IsNullOrWhiteSpace(options.Genres))
                        news.Add(new XElement(newsNs + "genres", options.Genres.Trim()));
                    if (!string.IsNullOrWhiteSpace(options.Access))
                        news.Add(new XElement(newsNs + "access", options.Access.Trim()));
                    if (!string.IsNullOrWhiteSpace(options.Keywords))
                        news.Add(new XElement(newsNs + "keywords", options.Keywords.Trim()));

                    return new XElement(
                        sitemapNs + "url",
                        new XElement(sitemapNs + "loc", loc),
                        news);
                })));

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? siteRoot);
        using var stream = File.Create(outputPath);
        doc.Save(stream);
    }

    private static void WriteSitemapIndex(
        string siteRoot,
        string baseUrl,
        string outputPath,
        string defaultLastModified,
        params string?[] sitemapPaths)
    {
        var normalizedPaths = sitemapPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .Where(path => !string.Equals(path, outputPath, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var sitemapNs = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(
                sitemapNs + "sitemapindex",
                normalizedPaths.Select(path =>
                {
                    var loc = ResolveSitemapOutputUrl(siteRoot, baseUrl, path);
                    var lastModified = ResolveSitemapLastModified(path, defaultLastModified);
                    return new XElement(
                        sitemapNs + "sitemap",
                        new XElement(sitemapNs + "loc", loc),
                        new XElement(sitemapNs + "lastmod", lastModified));
                })));

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? siteRoot);
        using var stream = File.Create(outputPath);
        doc.Save(stream);
    }

    private static void WriteImageSitemap(
        string siteRoot,
        string baseUrl,
        WebSitemapImageOptions options,
        IReadOnlyList<WebSitemapEntry> entries,
        string outputPath)
    {
        var patterns = BuildImagePathPatterns(options.PathPatterns);
        var selectedEntries = entries
            .Where(entry => MatchesAnyPathPattern(entry.Path, patterns))
            .Where(entry => entry.ImageUrls is { Length: > 0 })
            .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var sitemapNs = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
        var imageNs = XNamespace.Get("http://www.google.com/schemas/sitemap-image/1.1");
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(
                sitemapNs + "urlset",
                new XAttribute(XNamespace.Xmlns + "image", imageNs.NamespaceName),
                selectedEntries.Select(entry =>
                {
                    var route = NormalizeRoute(entry.Path);
                    var urlElement = new XElement(
                        sitemapNs + "url",
                        new XElement(sitemapNs + "loc", ResolveAbsoluteUrl(baseUrl, route)));

                    foreach (var imageUrl in entry.ImageUrls
                                 .Where(url => !string.IsNullOrWhiteSpace(url))
                                 .Select(url => ResolveMediaUrl(baseUrl, route, url))
                                 .Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        urlElement.Add(
                            new XElement(
                                imageNs + "image",
                                new XElement(imageNs + "loc", imageUrl)));
                    }

                    return urlElement;
                })));

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? siteRoot);
        using var stream = File.Create(outputPath);
        doc.Save(stream);
    }

    private static void WriteVideoSitemap(
        string siteRoot,
        string baseUrl,
        WebSitemapVideoOptions options,
        IReadOnlyList<WebSitemapEntry> entries,
        string outputPath)
    {
        var patterns = BuildVideoPathPatterns(options.PathPatterns);
        var selectedEntries = entries
            .Where(entry => MatchesAnyPathPattern(entry.Path, patterns))
            .Where(entry => entry.VideoUrls is { Length: > 0 })
            .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var sitemapNs = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
        var videoNs = XNamespace.Get("http://www.google.com/schemas/sitemap-video/1.1");
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(
                sitemapNs + "urlset",
                new XAttribute(XNamespace.Xmlns + "video", videoNs.NamespaceName),
                selectedEntries.Select(entry =>
                {
                    var route = NormalizeRoute(entry.Path);
                    var pageUrl = ResolveAbsoluteUrl(baseUrl, route);
                    var title = string.IsNullOrWhiteSpace(entry.Title) ? BuildTitleFromRoute(route) : entry.Title!;
                    var description = string.IsNullOrWhiteSpace(entry.Description)
                        ? title
                        : entry.Description!.Trim();
                    var thumbnailUrl = ResolveVideoThumbnailUrl(baseUrl, route, entry.ImageUrls);

                    var urlElement = new XElement(
                        sitemapNs + "url",
                        new XElement(sitemapNs + "loc", pageUrl));

                    foreach (var videoUrl in entry.VideoUrls
                                 .Where(url => !string.IsNullOrWhiteSpace(url))
                                 .Select(url => ResolveMediaUrl(baseUrl, route, url))
                                 .Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        urlElement.Add(
                            new XElement(
                                videoNs + "video",
                                new XElement(videoNs + "thumbnail_loc", thumbnailUrl),
                                new XElement(videoNs + "title", title),
                                new XElement(videoNs + "description", description),
                                new XElement(videoNs + "content_loc", videoUrl)));
                    }

                    return urlElement;
                })));

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? siteRoot);
        using var stream = File.Create(outputPath);
        doc.Save(stream);
    }

    private static string[] BuildNewsPathPatterns(string[]? pathPatterns)
    {
        if (pathPatterns is { Length: > 0 })
        {
            var explicitPatterns = pathPatterns
                .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
                .Select(pattern => pattern.Trim())
                .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (explicitPatterns.Length > 0)
                return explicitPatterns;
        }

        return DefaultNewsPathPatterns;
    }

    private static string[] BuildImagePathPatterns(string[]? pathPatterns)
    {
        if (pathPatterns is { Length: > 0 })
        {
            var explicitPatterns = pathPatterns
                .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
                .Select(pattern => pattern.Trim())
                .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (explicitPatterns.Length > 0)
                return explicitPatterns;
        }

        return DefaultImagePathPatterns;
    }

    private static string[] BuildVideoPathPatterns(string[]? pathPatterns)
    {
        if (pathPatterns is { Length: > 0 })
        {
            var explicitPatterns = pathPatterns
                .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
                .Select(pattern => pattern.Trim())
                .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (explicitPatterns.Length > 0)
                return explicitPatterns;
        }

        return DefaultVideoPathPatterns;
    }

    private static bool MatchesAnyPathPattern(string route, IReadOnlyList<string> patterns)
    {
        if (string.IsNullOrWhiteSpace(route) || patterns.Count == 0)
            return false;

        var normalized = NormalizeRoute(route).TrimStart('/');
        foreach (var pattern in patterns)
        {
            if (GlobMatch(pattern, normalized))
                return true;
        }

        return false;
    }

    private static string ResolveNewsPublicationName(string baseUrl, string? explicitName)
    {
        if (!string.IsNullOrWhiteSpace(explicitName))
            return explicitName.Trim();

        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
            return uri.Host;

        return "Site News";
    }

    private static string ResolveNewsPublicationDate(string? lastModified, string fallbackDate)
    {
        if (!string.IsNullOrWhiteSpace(lastModified) &&
            DateTimeOffset.TryParse(
                lastModified,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture);
        }

        return fallbackDate;
    }

    private static string ResolveVideoThumbnailUrl(string baseUrl, string route, IReadOnlyList<string>? imageUrls)
    {
        var thumbnail = imageUrls?
            .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
        if (!string.IsNullOrWhiteSpace(thumbnail))
            return ResolveMediaUrl(baseUrl, route, thumbnail!);

        return ResolveAbsoluteUrl(baseUrl, route);
    }

    private static string ResolveMediaUrl(string baseUrl, string route, string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return ResolveAbsoluteUrl(baseUrl, route);

        var trimmed = source.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri) &&
            (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps))
        {
            return absoluteUri.ToString();
        }

        if (trimmed.StartsWith("//", StringComparison.Ordinal))
            return "https:" + trimmed;

        if (trimmed.StartsWith("/", StringComparison.Ordinal))
            return ResolveAbsoluteUrl(baseUrl, trimmed);

        var normalizedRoute = NormalizeRoute(route);
        var routeFolder = normalizedRoute;
        if (!routeFolder.EndsWith("/", StringComparison.Ordinal))
        {
            var slash = routeFolder.LastIndexOf('/');
            routeFolder = slash < 0 ? "/" : routeFolder[..(slash + 1)];
        }
        if (!routeFolder.StartsWith("/", StringComparison.Ordinal))
            routeFolder = "/" + routeFolder;

        var combined = routeFolder + trimmed;
        return ResolveAbsoluteUrl(baseUrl, combined);
    }

    private static string ResolveSitemapOutputUrl(string siteRoot, string baseUrl, string outputPath)
    {
        if (Uri.TryCreate(outputPath, UriKind.Absolute, out var absoluteUri) &&
            (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps))
        {
            return absoluteUri.ToString();
        }

        var full = Path.GetFullPath(outputPath);
        if (IsUnderRoot(siteRoot, full))
        {
            var relative = Path.GetRelativePath(siteRoot, full).Replace('\\', '/');
            return ResolveAbsoluteUrl(baseUrl, "/" + relative.TrimStart('/'));
        }

        var fileName = Path.GetFileName(full);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "sitemap.xml";
        return ResolveAbsoluteUrl(baseUrl, "/" + fileName);
    }

    private static string ResolveSitemapLastModified(string path, string fallbackDate)
    {
        try
        {
            if (File.Exists(path))
                return File.GetLastWriteTimeUtc(path).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch
        {
            // best-effort only
        }

        return fallbackDate;
    }
}
