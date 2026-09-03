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
        HashSet<string> rewrittenHtmlFiles,
        HashSet<string> rewrittenCssFiles,
        Dictionary<string, string> cssRewriteIdentityByRoute,
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
                cssRewriteIdentityByRoute[move.TargetRoute] = move.Entry.OriginalPath.TrimStart('/');
                transitionMap["/" + move.CurrentRoute] = "/" + move.TargetRoute;
                transitionMap[move.CurrentRoute] = move.TargetRoute;
                onUpdated?.Invoke(move.TargetPath);
            }

            RewriteHashedReferences(
                siteRoot,
                htmlFiles,
                transitionMap,
                protectedStoryArtifacts,
                rewrittenHtmlFiles,
                rewrittenCssFiles,
                cssRewriteIdentityByRoute,
                onUpdated);
        }

        throw new InvalidOperationException(
            "Hashed asset references did not stabilize. Check for cyclic references between fingerprinted assets.");
    }

    private static string RewriteCssAssetReferences(
        string css,
        Dictionary<string, string> map,
        Uri stylesheetUri)
    {
        List<(int Start, int Length, string Value)>? replacements = null;
        var braceDepth = 0;
        for (var index = 0; index < css.Length;)
        {
            if (index + 1 < css.Length && css[index] == '/' && css[index + 1] == '*')
            {
                index = SkipCssComment(css, index + 2);
                continue;
            }

            if (css[index] is '\'' or '"')
            {
                index = SkipCssString(css, index + 1, css[index]);
                continue;
            }

            if (css[index] == '{')
            {
                braceDepth++;
                index++;
                continue;
            }

            if (css[index] == '}')
            {
                braceDepth = Math.Max(0, braceDepth - 1);
                index++;
                continue;
            }

            if (TryReadCssUrl(css, index, out var urlStart, out var urlLength, out var nextIndex) ||
                (braceDepth == 0 &&
                 TryReadQuotedCssImport(css, index, out urlStart, out urlLength, out nextIndex)))
            {
                var url = css.Substring(urlStart, urlLength);
                var mapped = RewriteUrlWithMap(url, map, stylesheetUri);
                if (!string.Equals(mapped, url, StringComparison.Ordinal))
                {
                    replacements ??= new List<(int, int, string)>();
                    replacements.Add((urlStart, urlLength, mapped));
                }

                index = nextIndex;
                continue;
            }

            index++;
        }

        if (replacements is null)
            return css;

        var rewritten = new StringBuilder(css.Length + replacements.Sum(replacement => replacement.Value.Length - replacement.Length));
        var copyFrom = 0;
        foreach (var replacement in replacements)
        {
            rewritten.Append(css, copyFrom, replacement.Start - copyFrom);
            rewritten.Append(replacement.Value);
            copyFrom = replacement.Start + replacement.Length;
        }

        rewritten.Append(css, copyFrom, css.Length - copyFrom);
        return rewritten.ToString();
    }

    private static bool TryReadCssUrl(
        string css,
        int index,
        out int urlStart,
        out int urlLength,
        out int nextIndex)
    {
        urlStart = 0;
        urlLength = 0;
        nextIndex = index + 1;
        if (!StartsWithCssKeyword(css, index, "url") ||
            (index > 0 && IsCssIdentifierCharacter(css[index - 1])))
        {
            return false;
        }

        var cursor = index + 3;
        while (cursor < css.Length && char.IsWhiteSpace(css[cursor]))
            cursor++;
        if (cursor >= css.Length || css[cursor] != '(')
            return false;

        cursor++;
        while (cursor < css.Length && char.IsWhiteSpace(css[cursor]))
            cursor++;
        if (cursor >= css.Length)
            return false;

        if (css[cursor] is '\'' or '"')
        {
            var quote = css[cursor++];
            urlStart = cursor;
            var afterString = SkipCssString(css, cursor, quote);
            if (afterString <= cursor || afterString > css.Length || css[afterString - 1] != quote)
                return false;
            urlLength = afterString - cursor - 1;
            cursor = afterString;
            while (cursor < css.Length && char.IsWhiteSpace(css[cursor]))
                cursor++;
            if (cursor >= css.Length || css[cursor] != ')')
                return false;
            nextIndex = cursor + 1;
            return true;
        }

        urlStart = cursor;
        while (cursor < css.Length && css[cursor] != ')')
        {
            if (css[cursor] == '\\' && cursor + 1 < css.Length)
                cursor += 2;
            else
                cursor++;
        }
        if (cursor >= css.Length)
            return false;
        var urlEnd = cursor;
        while (urlEnd > urlStart && char.IsWhiteSpace(css[urlEnd - 1]))
            urlEnd--;
        urlLength = urlEnd - urlStart;
        nextIndex = cursor + 1;
        return urlLength > 0;
    }

    private static bool TryReadQuotedCssImport(
        string css,
        int index,
        out int urlStart,
        out int urlLength,
        out int nextIndex)
    {
        urlStart = 0;
        urlLength = 0;
        nextIndex = index + 1;
        const string keyword = "@import";
        if (!StartsWithCssKeyword(css, index, keyword) ||
            (index + keyword.Length < css.Length && IsCssIdentifierCharacter(css[index + keyword.Length])))
        {
            return false;
        }

        var cursor = index + keyword.Length;
        cursor = SkipCssTrivia(css, cursor);
        if (cursor >= css.Length || css[cursor] is not ('\'' or '"'))
            return false;

        var quote = css[cursor++];
        urlStart = cursor;
        var afterString = SkipCssString(css, cursor, quote);
        if (afterString <= cursor || afterString > css.Length || css[afterString - 1] != quote)
            return false;
        urlLength = afterString - cursor - 1;
        nextIndex = afterString;
        return true;
    }

    private static int SkipCssTrivia(string css, int index)
    {
        while (index < css.Length)
        {
            if (char.IsWhiteSpace(css[index]))
            {
                index++;
                continue;
            }
            if (index + 1 < css.Length && css[index] == '/' && css[index + 1] == '*')
            {
                index = SkipCssComment(css, index + 2);
                continue;
            }
            break;
        }
        return index;
    }

    private static int SkipCssComment(string css, int index)
    {
        while (index + 1 < css.Length)
        {
            if (css[index] == '*' && css[index + 1] == '/')
                return index + 2;
            index++;
        }
        return css.Length;
    }

    private static int SkipCssString(string css, int index, char quote)
    {
        while (index < css.Length)
        {
            if (css[index] == '\\' && index + 1 < css.Length)
            {
                index += 2;
                continue;
            }
            if (css[index] == quote)
                return index + 1;
            index++;
        }
        return css.Length;
    }

    private static bool StartsWithCssKeyword(string css, int index, string keyword) =>
        index + keyword.Length <= css.Length &&
        css.AsSpan(index, keyword.Length).Equals(keyword.AsSpan(), StringComparison.OrdinalIgnoreCase);

    private static bool IsCssIdentifierCharacter(char value) =>
        char.IsLetterOrDigit(value) || value is '_' or '-' || value >= 0x80;

    private static bool IsCanonicalWebMcpRuntimePath(string siteRoot, string path)
    {
        var canonicalPath = Path.GetFullPath(Path.Combine(
            siteRoot,
            WebSiteBuilder.WebMcpSiteSearchAssetRoute.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        return FileSystemPathComparer.Equals(canonicalPath, Path.GetFullPath(path));
    }

    private static bool IsCanonicalWebMcpRuntimeReference(string url, Uri? documentBaseUri)
    {
        if (documentBaseUri is null ||
            !Uri.TryCreate(documentBaseUri, url, out var resolved) ||
            !HasAssetRewriteOrigin(resolved))
        {
            return false;
        }

        var resolvedPath = DecodeUrlPathForLookup(resolved.AbsolutePath.TrimStart('/'));
        return string.Equals(
            resolvedPath,
            WebSiteBuilder.WebMcpSiteSearchAssetRoute.TrimStart('/'),
            StringComparison.Ordinal);
    }

    private static void ValidateHashableDocumentBases(IEnumerable<string> htmlFiles, string siteRoot)
    {
        foreach (var htmlFile in htmlFiles)
        {
            var html = File.ReadAllText(htmlFile);
            if (html.IndexOf("<base", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            var relativeHtmlPath = Path.GetRelativePath(siteRoot, htmlFile).Replace('\\', '/');
            var documentBaseUri = ResolveDocumentBaseUri(html, CreateSiteDocumentUri(relativeHtmlPath));
            if (documentBaseUri is not null && HasAssetRewriteOrigin(documentBaseUri))
                continue;

            throw new InvalidOperationException(
                $"Asset hashing cannot safely rewrite '{relativeHtmlPath}' because its HTML base URL is invalid or points at another origin.");
        }
    }

    private static bool HasAssetRewriteOrigin(Uri uri) =>
        string.Equals(uri.Scheme, AssetRewriteOrigin.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(uri.Host, AssetRewriteOrigin.Host, StringComparison.OrdinalIgnoreCase) &&
        uri.Port == AssetRewriteOrigin.Port;

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
