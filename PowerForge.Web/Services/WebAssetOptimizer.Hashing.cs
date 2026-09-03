using System.Diagnostics;
using System.Text;
using AngleSharp.Dom;
using HtmlTinkerX;

namespace PowerForge.Web;

public static partial class WebAssetOptimizer
{
    private static readonly Uri AssetRewriteOrigin = new("https://powerforge.invalid/", UriKind.Absolute);

    private static void MinifyCssAssets(
        string siteRoot,
        IReadOnlySet<string> protectedStoryArtifacts,
        WebAssetOptimizerOptions options,
        WebOptimizeResult result,
        Action<string>? onUpdated)
    {
        if (!options.MinifyCss)
            return;

        foreach (var cssFile in Directory.EnumerateFiles(siteRoot, "*.css", SearchOption.AllDirectories)
                     .Where(path => !protectedStoryArtifacts.Contains(Path.GetFullPath(path))))
        {
            var css = File.ReadAllText(cssFile);
            if (string.IsNullOrWhiteSpace(css))
                continue;

            string? minified;
            try
            {
                minified = HtmlOptimizer.OptimizeCss(css);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"CSS minify failed for {cssFile}: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(minified) || string.Equals(css, minified, StringComparison.Ordinal))
                continue;

            var beforeBytes = Encoding.UTF8.GetByteCount(css);
            var afterBytes = Encoding.UTF8.GetByteCount(minified);
            File.WriteAllText(cssFile, minified);
            result.CssMinifiedCount++;
            result.CssBytesSaved += Math.Max(0, beforeBytes - afterBytes);
            onUpdated?.Invoke(cssFile);
        }
    }

    private static void MinifyJavaScriptAssets(
        string siteRoot,
        IReadOnlySet<string> protectedStoryArtifacts,
        WebAssetOptimizerOptions options,
        WebOptimizeResult result,
        Action<string>? onUpdated)
    {
        if (!options.MinifyJs)
            return;

        foreach (var jsFile in Directory.EnumerateFiles(siteRoot, "*.js", SearchOption.AllDirectories)
                     .Where(path => !protectedStoryArtifacts.Contains(Path.GetFullPath(path)))
                     .Where(path => !IsCanonicalWebMcpRuntimePath(siteRoot, path)))
        {
            var js = File.ReadAllText(jsFile);
            if (string.IsNullOrWhiteSpace(js))
                continue;

            string? minified;
            try
            {
                minified = HtmlOptimizer.OptimizeJavaScript(js);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"JS minify failed for {jsFile}: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(minified) || string.Equals(js, minified, StringComparison.Ordinal))
                continue;

            var beforeBytes = Encoding.UTF8.GetByteCount(js);
            var afterBytes = Encoding.UTF8.GetByteCount(minified);
            File.WriteAllText(jsFile, minified);
            result.JsMinifiedCount++;
            result.JsBytesSaved += Math.Max(0, beforeBytes - afterBytes);
            onUpdated?.Invoke(jsFile);
        }
    }

    private static void StabilizeHashedAssets(
        string siteRoot,
        string[] htmlFiles,
        Dictionary<string, string> hashMap,
        List<WebOptimizeHashedAssetEntry> hashedAssets,
        IReadOnlySet<string> protectedStoryArtifacts,
        WebOptimizeResult result,
        Action<string>? onUpdated)
    {
        var maximumPasses = Math.Max(1, hashedAssets.Count + 1);
        for (var pass = 0; pass < maximumPasses; pass++)
        {
            var moves = new List<(WebOptimizeHashedAssetEntry Entry, string CurrentPath, string TargetPath, string CurrentRoute, string TargetRoute)>();
            foreach (var entry in hashedAssets)
            {
                var originalRoute = entry.OriginalPath.TrimStart('/');
                var currentRoute = entry.HashedPath.TrimStart('/');
                var currentPath = Path.GetFullPath(Path.Combine(siteRoot, currentRoute.Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(currentPath))
                    throw new InvalidOperationException($"Hashed asset is missing during stabilization: {entry.HashedPath}");

                var extension = Path.GetExtension(originalRoute);
                if (string.IsNullOrEmpty(extension) || originalRoute.Length <= extension.Length)
                    continue;

                var stem = originalRoute[..^extension.Length];
                var finalHash = ComputeShortHash(File.ReadAllBytes(currentPath));
                var targetRoute = $"{stem}.{finalHash}{extension}";
                var targetPath = Path.GetFullPath(Path.Combine(siteRoot, targetRoute.Replace('/', Path.DirectorySeparatorChar)));
                if (FileSystemPathComparer.Equals(currentPath, targetPath))
                    continue;
                if (protectedStoryArtifacts.Contains(targetPath) || IsCanonicalWebMcpRuntimePath(siteRoot, targetPath))
                    throw new InvalidOperationException($"Final hashed asset destination is protected: /{targetRoute}");

                moves.Add((entry, currentPath, targetPath, currentRoute, targetRoute));
            }

            if (moves.Count == 0)
                return;

            var transitionMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var move in moves)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(move.TargetPath)!);
                File.Move(move.CurrentPath, move.TargetPath, overwrite: true);
                move.Entry.HashedPath = "/" + move.TargetRoute;
                hashMap[move.Entry.OriginalPath] = move.Entry.HashedPath;
                hashMap[move.Entry.OriginalPath.TrimStart('/')] = move.TargetRoute;
                transitionMap["/" + move.CurrentRoute] = "/" + move.TargetRoute;
                transitionMap[move.CurrentRoute] = move.TargetRoute;
                onUpdated?.Invoke(move.TargetPath);
            }

            var rewrites = RewriteHashedReferences(
                siteRoot,
                htmlFiles,
                transitionMap,
                protectedStoryArtifacts,
                onUpdated);
            result.HtmlHashRewriteCount += rewrites.HtmlFilesRewritten;
            result.CssHashRewriteCount += rewrites.CssFilesRewritten;
        }

        throw new InvalidOperationException(
            "Hashed asset references did not stabilize. Check for cyclic references between fingerprinted assets.");
    }

    private static bool IsCanonicalWebMcpRuntimePath(string siteRoot, string path)
    {
        var canonicalPath = Path.GetFullPath(Path.Combine(
            siteRoot,
            WebSiteBuilder.WebMcpSiteSearchAssetRoute.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        return FileSystemPathComparer.Equals(canonicalPath, Path.GetFullPath(path));
    }

    private static Uri CreateSiteDocumentUri(string relativeDocumentPath) =>
        new(AssetRewriteOrigin, EncodeUrlPath(relativeDocumentPath.Replace('\\', '/').TrimStart('/')));

    private static Uri? ResolveDocumentBaseUri(string html, Uri documentUri)
    {
        if (html.IndexOf("<base", StringComparison.OrdinalIgnoreCase) < 0)
            return documentUri;

        try
        {
            var document = HtmlParser.ParseWithAngleSharp(html);
            var baseHref = document.QuerySelector("base[href]")?.GetAttribute("href");
            if (baseHref is null)
                return documentUri;
            return Uri.TryCreate(documentUri, baseHref, out var resolvedBase) ? resolvedBase : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? DecodeUrlPathForLookup(string escapedPath)
    {
        try
        {
            var decodedSegments = escapedPath
                .Split('/', StringSplitOptions.None)
                .Select(Uri.UnescapeDataString)
                .ToArray();
            if (decodedSegments.Any(segment => segment.Contains('/') || segment.Contains('\\')))
                return null;
            return string.Join('/', decodedSegments);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private static string EncodeUrlPath(string path) =>
        string.Join('/', path.Split('/', StringSplitOptions.None).Select(Uri.EscapeDataString));
}
