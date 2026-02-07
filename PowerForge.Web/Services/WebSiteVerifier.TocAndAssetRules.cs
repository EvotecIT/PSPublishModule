using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PowerForge.Web;

/// <summary>TOC, Prism, and asset portability verification rules.</summary>
public static partial class WebSiteVerifier
{
    private static void ValidateNotFoundAssetBundles(SiteSpec spec, IEnumerable<string> routes, List<string> warnings)
    {
        if (spec is null || warnings is null || routes is null) return;

        var assets = spec.AssetRegistry;
        if (assets is null) return;
        if (assets.Bundles is null || assets.Bundles.Length == 0) return;
        if (assets.RouteBundles is null || assets.RouteBundles.Length == 0) return;

        var notFoundRoute = routes
            .FirstOrDefault(route => string.Equals(NormalizeRouteNoTrailingSlash(route), "/404", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(notFoundRoute))
            return;

        var matched = ResolveBundlesForRoute(assets, notFoundRoute);
        if (matched.Count > 0)
            return;

        warnings.Add($"Route '{notFoundRoute}' does not match any AssetRegistry.RouteBundles entry. " +
                     "404 pages may render without full CSS/JS. Add '/**' or '/404' mapping to your global bundle.");
    }

    private static void ValidatePrismAssets(SiteSpec spec, WebSitePlan plan, List<string> warnings)
    {
        if (spec is null || plan is null || warnings is null) return;

        var source = spec.Prism?.Source ?? spec.AssetPolicy?.Mode ?? "cdn";
        if (!source.Equals("local", StringComparison.OrdinalIgnoreCase) &&
            !source.Equals("hybrid", StringComparison.OrdinalIgnoreCase))
            return;

        var (light, dark, core, autoloader, _) = ResolvePrismLocalAssets(spec.Prism);
        var missing = new List<string>();

        CheckPrismAsset(plan.RootPath, light, missing);
        CheckPrismAsset(plan.RootPath, dark, missing);
        CheckPrismAsset(plan.RootPath, core, missing);
        CheckPrismAsset(plan.RootPath, autoloader, missing);

        if (missing.Count > 0)
        {
            var files = string.Join(", ", missing);
            warnings.Add($"Prism local assets not found: {files}. " +
                         "Add the files to the site root or set Prism.Source to 'cdn'/'hybrid'.");
        }
    }

    private static (string light, string dark, string core, string autoloader, string langPath) ResolvePrismLocalAssets(PrismSpec? prismSpec)
    {
        var local = prismSpec?.Local;
        var lightOverride = local?.ThemeLight ?? prismSpec?.ThemeLight;
        var darkOverride = local?.ThemeDark ?? prismSpec?.ThemeDark;
        var light = ResolvePrismThemeHref(lightOverride, defaultLocalPath: "/assets/prism/prism.css");
        var dark = ResolvePrismThemeHref(darkOverride, defaultLocalPath: "/assets/prism/prism-okaidia.css");
        var core = local?.Core ?? "/assets/prism/prism-core.js";
        var autoloader = local?.Autoloader ?? "/assets/prism/prism-autoloader.js";
        var langPath = local?.LanguagesPath ?? "/assets/prism/components/";
        return (light, dark, core, autoloader, langPath);
    }

    private static string ResolvePrismThemeHref(string? value, string defaultLocalPath)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultLocalPath;

        var trimmed = value.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("//", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        if (trimmed.StartsWith("/"))
            return trimmed;

        if (trimmed.Contains("/"))
            return "/" + trimmed.TrimStart('/');

        if (trimmed.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            return "/" + trimmed.TrimStart('/');

        return "/assets/prism/prism-" + trimmed + ".css";
    }

    private static void CheckPrismAsset(string rootPath, string? href, List<string> missing)
    {
        if (string.IsNullOrWhiteSpace(href)) return;
        if (IsExternalPath(href)) return;

        var trimmed = href.TrimStart('/');
        if (string.IsNullOrWhiteSpace(trimmed)) return;

        var relative = trimmed.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.Combine(rootPath, relative);
        if (File.Exists(fullPath))
            return;

        var staticPath = Path.Combine(rootPath, "static", relative);
        if (!File.Exists(staticPath))
            missing.Add(href);
    }

    private static void ValidateTocCoverage(
        SiteSpec spec,
        WebSitePlan plan,
        Dictionary<string, List<CollectionRoute>> collectionRoutes,
        List<string> warnings)
    {
        if (spec.Collections is null || spec.Collections.Length == 0)
            return;

        foreach (var collection in spec.Collections)
        {
            if (collection is null) continue;
            if (collection.UseToc == false) continue;

            if (!collectionRoutes.TryGetValue(collection.Name, out var routes) || routes.Count == 0)
                continue;

            var tocPath = ResolveTocPath(collection, plan.RootPath);
            if (string.IsNullOrWhiteSpace(tocPath) || !File.Exists(tocPath))
            {
                warnings.Add($"TOC is enabled for collection '{collection.Name}' but no toc.json/toc.yml was found.");
                continue;
            }

            var tocItems = LoadTocFromPath(tocPath);
            if (tocItems.Length == 0)
                continue;

            var tocUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectTocUrls(tocItems, tocUrls, spec.TrailingSlash);

            if (tocUrls.Count == 0)
                continue;

            var outputRoot = string.IsNullOrWhiteSpace(collection.Output) ? "/" : collection.Output;
            var normalizedRoot = NormalizeRouteForCompare(outputRoot, spec.TrailingSlash);

            var routeSet = routes
                .Where(r => !r.Draft)
                .Select(r => r.Route)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var missing = routeSet
                .Where(r => !tocUrls.Contains(r))
                .ToList();

            if (missing.Count > 0)
            {
                var preview = string.Join(", ", missing.Take(5));
                var suffix = missing.Count > 5 ? " ..." : string.Empty;
                warnings.Add($"TOC for collection '{collection.Name}' is missing {missing.Count} page(s): {preview}{suffix}");
            }

            var extra = tocUrls
                .Where(u => u.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                .Where(u => !routeSet.Contains(u))
                .ToList();

            if (extra.Count > 0)
            {
                var preview = string.Join(", ", extra.Take(5));
                var suffix = extra.Count > 5 ? " ..." : string.Empty;
                warnings.Add($"TOC for collection '{collection.Name}' contains {extra.Count} missing page(s): {preview}{suffix}");
            }
        }
    }

    private static string? ResolveTocPath(CollectionSpec collection, string rootPath)
    {
        if (collection.UseToc == false)
            return null;

        var tocPath = collection.TocFile;
        if (!string.IsNullOrWhiteSpace(tocPath))
        {
            var resolved = Path.IsPathRooted(tocPath) ? tocPath : Path.Combine(rootPath, tocPath);
            return resolved;
        }

        var inputRoot = Path.IsPathRooted(collection.Input)
            ? collection.Input
            : Path.Combine(rootPath, collection.Input);
        if (inputRoot.Contains('*'))
            return null;

        var jsonPath = Path.Combine(inputRoot, "toc.json");
        if (File.Exists(jsonPath))
            return jsonPath;

        var yamlPath = Path.Combine(inputRoot, "toc.yml");
        if (File.Exists(yamlPath))
            return yamlPath;

        var yamlAltPath = Path.Combine(inputRoot, "toc.yaml");
        if (File.Exists(yamlAltPath))
            return yamlAltPath;

        return null;
    }

    private static TocItem[] LoadTocFromPath(string path)
    {
        if (!File.Exists(path))
            return Array.Empty<TocItem>();

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".json")
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<TocItem[]>(json, WebJson.Options) ?? Array.Empty<TocItem>();
        }

        if (ext is ".yml" or ".yaml")
        {
            var yaml = File.ReadAllText(path);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var items = deserializer.Deserialize<List<TocItem>>(yaml);
            return items?.ToArray() ?? Array.Empty<TocItem>();
        }

        return Array.Empty<TocItem>();
    }

    private static void CollectTocUrls(TocItem[] items, HashSet<string> urls, TrailingSlashMode slashMode)
    {
        foreach (var item in items ?? Array.Empty<TocItem>())
        {
            if (item is null || item.Hidden) continue;

            var url = item.Url ?? item.Href;
            var normalized = NormalizeTocUrl(url, slashMode);
            if (!string.IsNullOrWhiteSpace(normalized))
                urls.Add(normalized);

            if (item.Items is { Length: > 0 })
                CollectTocUrls(item.Items, urls, slashMode);
        }
    }

    private static string? NormalizeTocUrl(string? url, TrailingSlashMode slashMode)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        if (IsExternalPath(url))
            return null;
        if (url.StartsWith("#", StringComparison.Ordinal))
            return null;

        var trimmed = url.Trim();
        var baseUrl = trimmed.Split('?', '#')[0];
        if (string.IsNullOrWhiteSpace(baseUrl))
            return null;

        if (!baseUrl.StartsWith("/", StringComparison.Ordinal))
            baseUrl = "/" + baseUrl.TrimStart('/');

        if (baseUrl.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            baseUrl = baseUrl.Substring(0, baseUrl.Length - 3);

        return NormalizeRouteForCompare(baseUrl, slashMode);
    }

    private static string NormalizeRouteForCompare(string path, TrailingSlashMode mode)
    {
        var normalized = path.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            normalized = "/";
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = "/" + normalized.TrimStart('/');

        if (mode == TrailingSlashMode.Never && normalized.EndsWith("/") && normalized.Length > 1)
            return normalized.TrimEnd('/');
        if (mode == TrailingSlashMode.Always && !normalized.EndsWith("/"))
            return normalized + "/";
        return normalized;
    }

    private sealed class TocItem
    {
        public string? Name { get; set; }
        public string? Title { get; set; }
        public string? Href { get; set; }
        public string? Url { get; set; }
        public bool Hidden { get; set; }
        public TocItem[]? Items { get; set; }
    }

    private static List<AssetBundleSpec> ResolveBundlesForRoute(AssetRegistrySpec assets, string route)
    {
        var routeValue = string.IsNullOrWhiteSpace(route) ? "/" : route.Trim();
        var bundleMap = new Dictionary<string, AssetBundleSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var bundle in assets.Bundles ?? Array.Empty<AssetBundleSpec>())
        {
            var name = bundle.Name;
            if (string.IsNullOrWhiteSpace(name))
                continue;
            if (bundleMap.ContainsKey(name))
                continue;
            bundleMap[name] = bundle;
        }
        var selected = new List<AssetBundleSpec>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in (assets.RouteBundles ?? Array.Empty<RouteBundleSpec>())
                     .Where(mapping => GlobMatch(mapping.Match, routeValue)))
        {
            foreach (var name in mapping.Bundles ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                if (!seen.Add(name))
                    continue;
                if (bundleMap.TryGetValue(name, out var bundle))
                    selected.Add(bundle);
            }
        }

        return selected;
    }

    private static string NormalizeRouteNoTrailingSlash(string route)
    {
        if (string.IsNullOrWhiteSpace(route))
            return "/";

        var normalized = route.Trim();
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = "/" + normalized.TrimStart('/');

        if (normalized.Length > 1 && normalized.EndsWith("/", StringComparison.Ordinal))
            normalized = normalized.TrimEnd('/');

        return string.IsNullOrWhiteSpace(normalized) ? "/" : normalized;
    }

    private static void ValidateAssetRegistryPaths(
        AssetRegistrySpec? assets,
        string rootPath,
        string label,
        List<string> warnings)
    {
        if (assets is null) return;

        foreach (var bundle in assets.Bundles ?? Array.Empty<AssetBundleSpec>())
        {
            foreach (var css in bundle.Css ?? Array.Empty<string>())
                ValidateAssetPath(css, rootPath, $"{label} bundle '{bundle.Name}' css", warnings);
            foreach (var js in bundle.Js ?? Array.Empty<string>())
                ValidateAssetPath(js, rootPath, $"{label} bundle '{bundle.Name}' js", warnings);
        }

        foreach (var preload in assets.Preloads ?? Array.Empty<PreloadSpec>())
            ValidateAssetPath(preload.Href, rootPath, $"{label} preload", warnings);

        foreach (var css in assets.CriticalCss ?? Array.Empty<CriticalCssSpec>())
        {
            if (string.IsNullOrWhiteSpace(css.Path)) continue;
            if (IsExternalPath(css.Path)) continue;
            var fullPath = Path.IsPathRooted(css.Path)
                ? css.Path
                : Path.Combine(rootPath, css.Path);
            if (!File.Exists(fullPath))
                warnings.Add($"Missing {label} critical CSS: {css.Path}");
        }
    }

    private static void ValidateAssetPath(string? path, string rootPath, string label, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (IsExternalPath(path)) return;
        if (path.StartsWith("/", StringComparison.Ordinal)) return;
        var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(rootPath, path);
        if (!File.Exists(fullPath))
            warnings.Add($"Missing {label} asset: {path}");
    }

    private static void ValidateThemeManifestPath(string? path, string label, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        if (IsExternalPath(path))
        {
            warnings.Add($"{label} should be a local relative folder path, not an external URL: {path}");
            return;
        }
        if (!IsPortableRelativePath(path))
            warnings.Add($"{label} should be a portable relative path (no rooted paths, no '..'): {path}");
    }

    private static void ValidateThemeMappedPaths(Dictionary<string, string>? map, string label, List<string> warnings)
    {
        if (map is null || map.Count == 0)
            return;

        foreach (var kvp in map)
        {
            var key = kvp.Key ?? string.Empty;
            var value = kvp.Value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                warnings.Add($"{label} contains an empty mapping key.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(value))
            {
                warnings.Add($"{label} mapping '{key}' is empty.");
                continue;
            }
            if (!IsPortableRelativePath(value))
            {
                warnings.Add($"{label} mapping '{key}' should be a portable relative path (no rooted paths, no '..'): {value}");
            }
        }
    }

    private static void ValidateThemeAssetPortability(ThemeManifest manifest, List<string> warnings)
    {
        if (manifest.Assets is null)
            return;

        var label = $"theme:{manifest.Name} assets";
        foreach (var bundle in manifest.Assets.Bundles ?? Array.Empty<AssetBundleSpec>())
        {
            foreach (var css in bundle.Css ?? Array.Empty<string>())
                ValidateThemeBundleAssetPath(css, $"{label} bundle '{bundle.Name}' css", warnings);
            foreach (var js in bundle.Js ?? Array.Empty<string>())
                ValidateThemeBundleAssetPath(js, $"{label} bundle '{bundle.Name}' js", warnings);
        }

        foreach (var preload in manifest.Assets.Preloads ?? Array.Empty<PreloadSpec>())
        {
            if (string.IsNullOrWhiteSpace(preload.Href))
                continue;
            if (!IsPortableRelativePath(preload.Href) &&
                !preload.Href.StartsWith("/", StringComparison.Ordinal) &&
                !IsExternalPath(preload.Href))
            {
                warnings.Add($"{label} preload href should be relative, absolute web URL, or root-relative URL: {preload.Href}");
            }

            if (preload.Href.StartsWith("/themes/", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"{label} preload href hard-codes '/themes/...'. Prefer relative paths in theme assets for portability: {preload.Href}");
            }
        }

        foreach (var critical in manifest.Assets.CriticalCss ?? Array.Empty<CriticalCssSpec>())
        {
            if (string.IsNullOrWhiteSpace(critical.Path))
                continue;
            if (!IsPortableRelativePath(critical.Path))
            {
                warnings.Add($"{label} criticalCss '{critical.Name}' should be a portable relative path: {critical.Path}");
            }
        }
    }

    private static void ValidateThemeBundleAssetPath(string? path, string label, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        if (IsExternalPath(path))
        {
            warnings.Add($"{label} should use local relative paths in theme manifest. Move external URLs to site-level overrides when possible: {path}");
            return;
        }
        if (!IsPortableRelativePath(path))
            warnings.Add($"{label} should be a portable relative path (no rooted paths, no '..'): {path}");
        if (path.StartsWith("/themes/", StringComparison.OrdinalIgnoreCase))
            warnings.Add($"{label} hard-codes '/themes/...'. Use relative paths and let the engine normalize them: {path}");
    }

    private static bool IsPortableRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var trimmed = path.Trim();

        // Explicit drive-letter guard keeps behavior consistent across OSes
        // (e.g. C:\foo should never be treated as portable).
        if (trimmed.Length >= 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
            return false;

        // Guard URI-like schemes (file:, ftp:, custom:), not just http/https.
        var colonIndex = trimmed.IndexOf(':');
        if (colonIndex > 1 && LooksLikeUriScheme(trimmed, colonIndex))
            return false;

        if (Path.IsPathRooted(trimmed))
            return false;
        if (trimmed.StartsWith("/", StringComparison.Ordinal) || trimmed.StartsWith("\\", StringComparison.Ordinal))
            return false;

        var normalized = trimmed.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.All(segment => !string.Equals(segment, "..", StringComparison.Ordinal));
    }

    private static bool LooksLikeUriScheme(string value, int colonIndex)
    {
        if (string.IsNullOrWhiteSpace(value) || colonIndex <= 0 || colonIndex >= value.Length)
            return false;
        if (!char.IsLetter(value[0]))
            return false;

        for (var i = 1; i < colonIndex; i++)
        {
            var ch = value[i];
            if (char.IsLetterOrDigit(ch) || ch == '+' || ch == '-' || ch == '.')
                continue;
            return false;
        }

        return true;
    }
}
