namespace PowerForge.Web;

public static partial class WebSiteVerifier
{
    private static IEnumerable<string> DiscoverStaticHtmlRoutes(SiteSpec spec, string rootPath)
    {
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
                var destination = string.IsNullOrWhiteSpace(mapping.Destination)
                    ? Path.GetFileName(sourcePath)
                    : Path.HasExtension(mapping.Destination)
                        ? mapping.Destination
                        : Path.Combine(mapping.Destination, Path.GetFileName(sourcePath));

                foreach (var route in GetStaticHtmlRoutes(destination))
                    yield return route;
                continue;
            }

            if (!Directory.Exists(sourcePath))
                continue;

            foreach (var file in EnumerateStaticHtmlFiles(sourcePath))
            {
                var destination = string.IsNullOrWhiteSpace(mapping.Destination)
                    ? file
                    : Path.Combine(mapping.Destination, file);
                foreach (var route in GetStaticHtmlRoutes(destination))
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
            if (string.Equals(file.Extension, ".html", StringComparison.OrdinalIgnoreCase))
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
        if (string.IsNullOrWhiteSpace(destination) ||
            !string.Equals(Path.GetExtension(destination), ".html", StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        var normalized = destination.Replace('\\', '/').TrimStart('/');
        if (normalized.Length == 0 ||
            Path.IsPathRooted(normalized) ||
            normalized.Split('/').Any(segment => segment == ".."))
            yield break;

        var fileRoute = "/" + normalized;
        yield return fileRoute;

        if (!string.Equals(Path.GetFileName(normalized), "index.html", StringComparison.OrdinalIgnoreCase))
            yield break;

        var directory = Path.GetDirectoryName(normalized)?.Replace('\\', '/').Trim('/');
        yield return string.IsNullOrWhiteSpace(directory) ? "/" : "/" + directory + "/";
    }
}
