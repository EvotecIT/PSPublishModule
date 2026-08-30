namespace PowerForge.Web;

public static partial class WebSiteVerifier
{
    private static IEnumerable<string> DiscoverStaticHtmlRoutes(SiteSpec spec, string rootPath)
    {
        var projectionRoot = Path.GetFullPath(Path.Combine(rootPath, ".powerforge-static-route-projection"));
        if (!WebSiteBuilder.HasExplicitConventionalStaticMapping(spec, rootPath))
        {
            var conventionalRoot = Path.GetFullPath(Path.Combine(rootPath, "static"));
            if (Directory.Exists(conventionalRoot))
            {
                foreach (var file in EnumerateStaticHtmlFiles(conventionalRoot))
                {
                    foreach (var route in GetStaticHtmlRoutes(file))
                        yield return route;
                }
            }
        }

        foreach (var mapping in spec.StaticAssets ?? Array.Empty<StaticAssetSpec>())
        {
            if (mapping is null || string.IsNullOrWhiteSpace(mapping.Source))
                continue;

            string sourcePath;
            try
            {
                sourcePath = Path.IsPathRooted(mapping.Source)
                    ? Path.GetFullPath(mapping.Source)
                    : Path.GetFullPath(Path.Combine(rootPath, mapping.Source));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (File.Exists(sourcePath))
            {
                WebSiteBuilder.RejectLinkedAsset(new FileInfo(sourcePath));
                if (!WebSiteBuilder.TryResolveStaticAssetTargetPath(
                        projectionRoot,
                        sourcePath,
                        mapping.Destination,
                        out var targetPath))
                    continue;

                foreach (var route in GetStaticHtmlRoutes(Path.GetRelativePath(projectionRoot, targetPath)))
                    yield return route;
                continue;
            }

            if (!Directory.Exists(sourcePath))
                continue;

            if (!WebSiteBuilder.TryResolveStaticAssetTargetPath(
                    projectionRoot,
                    sourcePath,
                    mapping.Destination,
                    out var targetRoot))
                continue;

            foreach (var file in EnumerateStaticHtmlFiles(sourcePath))
            {
                var projectedFile = Path.Combine(targetRoot, file);
                foreach (var route in GetStaticHtmlRoutes(Path.GetRelativePath(projectionRoot, projectedFile)))
                    yield return route;
            }
        }
    }

    private static IEnumerable<string> EnumerateStaticHtmlFiles(string sourceRoot)
    {
        var root = new DirectoryInfo(sourceRoot);
        WebSiteBuilder.RejectLinkedAsset(root);

        foreach (var file in root.EnumerateFiles())
        {
            WebSiteBuilder.RejectLinkedAsset(file);
            if (IsHtmlExtension(file.Extension))
                yield return file.Name;
        }

        foreach (var directory in root.EnumerateDirectories())
        {
            WebSiteBuilder.RejectLinkedAsset(directory);
            foreach (var file in EnumerateStaticHtmlFiles(directory.FullName))
            {
                yield return Path.Combine(directory.Name, file);
            }
        }
    }

    private static IEnumerable<string> GetStaticHtmlRoutes(string destination)
    {
        if (string.IsNullOrWhiteSpace(destination) || !IsHtmlExtension(Path.GetExtension(destination)))
        {
            yield break;
        }

        var normalized = destination.Replace('\\', '/').TrimStart('/');
        if (normalized.Length == 0 ||
            Path.IsPathRooted(normalized) ||
            normalized.Split('/').Any(segment => segment == ".."))
            yield break;

        var encoded = string.Join(
            "/",
            normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        var fileRoute = "/" + encoded;
        yield return fileRoute;

        var fileName = Path.GetFileName(normalized);
        if (!string.Equals(fileName, "index.html", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fileName, "index.htm", StringComparison.OrdinalIgnoreCase))
            yield break;

        var directory = Path.GetDirectoryName(encoded)?.Replace('\\', '/').Trim('/');
        yield return string.IsNullOrWhiteSpace(directory) ? "/" : "/" + directory + "/";
    }

    private static bool IsHtmlExtension(string? extension) =>
        string.Equals(extension, ".html", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(extension, ".htm", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> DiscoverGeneratedFeatureRoutes(SiteSpec spec, bool hasContentItems)
    {
        if (hasContentItems && (spec.Features ?? Array.Empty<string>()).Any(feature =>
                string.Equals(feature?.Trim(), "search", StringComparison.OrdinalIgnoreCase)))
        {
            yield return "/search/index.html";
            yield return "/search/";
        }
    }
}
