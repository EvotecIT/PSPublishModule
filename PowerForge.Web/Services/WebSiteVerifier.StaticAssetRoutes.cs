namespace PowerForge.Web;

public static partial class WebSiteVerifier
{
    private static IEnumerable<string> DiscoverStaticAssetRoutes(SiteSpec spec, string rootPath)
    {
        var projectionRoot = Path.GetFullPath(Path.Combine(rootPath, ".powerforge-static-route-projection"));
        if (!WebSiteBuilder.HasExplicitConventionalStaticMapping(spec, rootPath))
        {
            var conventionalRoot = Path.GetFullPath(Path.Combine(rootPath, "static"));
            if (Directory.Exists(conventionalRoot))
            {
                foreach (var file in EnumerateStaticFiles(conventionalRoot))
                {
                    foreach (var route in GetStaticAssetRoutes(file))
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

                foreach (var route in GetStaticAssetRoutes(Path.GetRelativePath(projectionRoot, targetPath)))
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

            foreach (var file in EnumerateStaticFiles(sourcePath))
            {
                var projectedFile = Path.Combine(targetRoot, file);
                foreach (var route in GetStaticAssetRoutes(Path.GetRelativePath(projectionRoot, projectedFile)))
                    yield return route;
            }
        }
    }

    private static IEnumerable<string> EnumerateStaticFiles(string sourceRoot)
    {
        var root = new DirectoryInfo(sourceRoot);
        WebSiteBuilder.RejectLinkedAsset(root);

        foreach (var file in root.EnumerateFiles())
        {
            WebSiteBuilder.RejectLinkedAsset(file);
            yield return file.Name;
        }

        foreach (var directory in root.EnumerateDirectories())
        {
            WebSiteBuilder.RejectLinkedAsset(directory);
            foreach (var file in EnumerateStaticFiles(directory.FullName))
            {
                yield return Path.Combine(directory.Name, file);
            }
        }
    }

    private static IEnumerable<string> GetStaticAssetRoutes(string destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
            yield break;

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
        var extension = Path.GetExtension(fileName);
        if (string.Equals(extension, ".html", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".htm", StringComparison.OrdinalIgnoreCase))
        {
            yield return "/" + encoded[..^extension.Length];
        }

        if (!string.Equals(fileName, "index.html", StringComparison.Ordinal))
            yield break;

        var separatorIndex = encoded.LastIndexOf('/');
        var directory = separatorIndex < 0 ? string.Empty : encoded[..separatorIndex].Trim('/');
        yield return string.IsNullOrWhiteSpace(directory) ? "/" : "/" + directory + "/";
    }

    private static IEnumerable<string> DiscoverGeneratedFeatureRoutes(
        SiteSpec spec,
        IEnumerable<CollectionRoute> contentRoutes)
    {
        var searchEnabled = (spec.Features ?? Array.Empty<string>()).Any(feature =>
            string.Equals(feature?.Trim(), "search", StringComparison.OrdinalIgnoreCase));
        if (!searchEnabled || !contentRoutes.Any(route =>
                WebSiteBuilder.IsSearchableContent(route.Draft, route.Route)))
            yield break;

        yield return "/search/index.html";
        yield return "/search/";
    }
}
