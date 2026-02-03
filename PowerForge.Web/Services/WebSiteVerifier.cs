using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PowerForge.Web;

/// <summary>Verifies site content and routing integrity.</summary>
public static class WebSiteVerifier
{
    /// <summary>Validates the site spec against discovered content.</summary>
    /// <param name="spec">Site configuration.</param>
    /// <param name="plan">Resolved site plan.</param>
    /// <returns>Verification result.</returns>
    public static WebVerifyResult Verify(SiteSpec spec, WebSitePlan plan)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        var errors = new List<string>();
        var warnings = new List<string>();

        if (spec.Collections is null || spec.Collections.Length == 0)
        {
            warnings.Add("No collections defined.");
            return new WebVerifyResult { Success = true, Warnings = warnings.ToArray(), Errors = Array.Empty<string>() };
        }

        var routes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var collectionRoutes = new Dictionary<string, List<CollectionRoute>>(StringComparer.OrdinalIgnoreCase);
        foreach (var collection in spec.Collections)
        {
            if (collection is null) continue;
            var files = EnumerateCollectionFiles(plan.RootPath, collection.Input).ToArray();
            if (files.Length == 0)
            {
                warnings.Add($"Collection '{collection.Name}' has no files.");
                continue;
            }

            var leafBundleRoots = BuildLeafBundleRoots(files);
            foreach (var file in files)
            {
                if (IsUnderAnyRoot(file, leafBundleRoots) && !IsLeafBundleIndex(file))
                    continue;

                var markdown = File.ReadAllText(file);
                var (matter, body) = FrontMatterParser.Parse(markdown);
                var title = matter?.Title ?? FrontMatterParser.ExtractTitleFromMarkdown(body) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(title))
                {
                    errors.Add($"Missing title in: {file}");
                }

                var collectionRoot = ResolveCollectionRootForFile(plan.RootPath, collection.Input, file);
                var relativePath = ResolveRelativePath(collectionRoot, file);
                var relativeDir = NormalizePath(Path.GetDirectoryName(relativePath) ?? string.Empty);
                var isSectionIndex = IsSectionIndex(file);
                var isBundleIndex = IsLeafBundleIndex(file);
                var slugPath = ResolveSlugPath(relativePath, relativeDir, matter?.Slug);
                if (isSectionIndex || isBundleIndex)
                    slugPath = ApplySlugOverride(relativeDir, matter?.Slug);
                if (string.IsNullOrWhiteSpace(slugPath))
                {
                    errors.Add($"Missing slug in: {file}");
                    continue;
                }

                var projectSlug = ResolveProjectSlug(plan, file);
                var baseOutput = ReplaceProjectPlaceholder(collection.Output, projectSlug);
                var route = BuildRoute(baseOutput, slugPath, spec.TrailingSlash);
                if (routes.TryGetValue(route, out var existing))
                {
                    errors.Add($"Duplicate route '{route}' from '{file}' and '{existing}'.");
                }
                else
                {
                    routes[route] = file;
                }

                if (!collectionRoutes.TryGetValue(collection.Name, out var list))
                {
                    list = new List<CollectionRoute>();
                    collectionRoutes[collection.Name] = list;
                }
                list.Add(new CollectionRoute(route, file, matter?.Draft ?? false));
            }
        }

        ValidateDataFiles(spec, plan, warnings);
        ValidateThemeAssets(spec, plan, warnings);
        ValidateLayoutHooks(spec, plan, warnings);
        ValidatePrismAssets(spec, plan, warnings);
        ValidateTocCoverage(spec, plan, collectionRoutes, warnings);

        return new WebVerifyResult
        {
            Success = errors.Count == 0,
            Errors = errors.ToArray(),
            Warnings = warnings.ToArray()
        };
    }

    private static string BuildRoute(string baseOutput, string slug, TrailingSlashMode slashMode)
    {
        var basePath = NormalizePath(baseOutput);
        var slugPath = NormalizePath(slug);
        string combined;

        if (string.IsNullOrWhiteSpace(slugPath) || slugPath == "index")
        {
            combined = basePath;
        }
        else if (string.IsNullOrWhiteSpace(basePath) || basePath == "/")
        {
            combined = slugPath;
        }
        else
        {
            combined = $"{basePath}/{slugPath}";
        }

        combined = "/" + combined.Trim('/');
        return EnsureTrailingSlash(combined, slashMode);
    }

    private static string EnsureTrailingSlash(string path, TrailingSlashMode mode)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";

        if (mode == TrailingSlashMode.Never)
            return path.EndsWith("/") && path.Length > 1 ? path.TrimEnd('/') : path;

        if (mode == TrailingSlashMode.Always && !path.EndsWith("/"))
            return path + "/";

        return path;
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var trimmed = path.Trim();
        if (trimmed == "/") return "/";
        return trimmed.Trim('/');
    }

    private static string Slugify(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var lower = input.Trim().ToLowerInvariant();
        var sb = new System.Text.StringBuilder();
        foreach (var ch in lower)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (char.IsWhiteSpace(ch) || ch == '-' || ch == '_') sb.Append('-');
        }
        var slug = sb.ToString();
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }

    private static IEnumerable<string> EnumerateCollectionFiles(string rootPath, string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Array.Empty<string>();

        var full = Path.IsPathRooted(input) ? input : Path.Combine(rootPath, input);
        if (full.Contains('*'))
            return EnumerateCollectionFilesWithWildcard(full);

        if (!Directory.Exists(full))
            return Array.Empty<string>();

        return Directory.EnumerateFiles(full, "*.md", SearchOption.AllDirectories);
    }

    private static HashSet<string> BuildLeafBundleRoots(IReadOnlyList<string> markdownFiles)
    {
        if (markdownFiles.Count == 0)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var byDir = markdownFiles
            .GroupBy(f => Path.GetDirectoryName(f) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in byDir)
        {
            var dir = kvp.Key;
            var files = kvp.Value;
            var hasIndex = files.Any(IsLeafBundleIndex);
            if (!hasIndex) continue;
            if (files.Any(IsSectionIndex)) continue;

            var hasOtherMarkdown = files.Any(f =>
            {
                var name = Path.GetFileName(f);
                if (name.Equals("index.md", StringComparison.OrdinalIgnoreCase)) return false;
                if (name.Equals("_index.md", StringComparison.OrdinalIgnoreCase)) return false;
                return true;
            });

            if (!hasOtherMarkdown)
                roots.Add(dir);
        }

        return roots;
    }

    private static bool IsLeafBundleIndex(string filePath)
        => Path.GetFileName(filePath).Equals("index.md", StringComparison.OrdinalIgnoreCase);

    private static bool IsSectionIndex(string filePath)
        => Path.GetFileName(filePath).Equals("_index.md", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnderAnyRoot(string filePath, HashSet<string> roots)
    {
        if (roots.Count == 0) return false;
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            if (filePath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                filePath.Equals(root, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string ResolveRelativePath(string? collectionRoot, string filePath)
    {
        if (string.IsNullOrWhiteSpace(collectionRoot))
            return Path.GetFileName(filePath);
        return Path.GetRelativePath(collectionRoot, filePath).Replace('\\', '/');
    }

    private static void ValidateDataFiles(SiteSpec spec, WebSitePlan plan, List<string> warnings)
    {
        if (spec is null || plan is null) return;

        var dataRoot = string.IsNullOrWhiteSpace(spec.DataRoot) ? "data" : spec.DataRoot;
        var basePath = Path.IsPathRooted(dataRoot) ? dataRoot : Path.Combine(plan.RootPath, dataRoot);

        ValidateKnownDataFile(basePath, "faq.json", "data/faq.json", ValidateFaqJson, warnings);
        ValidateKnownDataFile(basePath, "showcase.json", "data/showcase.json", ValidateShowcaseJson, warnings);
        ValidateKnownDataFile(basePath, "pricing.json", "data/pricing.json", ValidatePricingJson, warnings);
        ValidateKnownDataFile(basePath, "benchmarks.json", "data/benchmarks.json", ValidateBenchmarksJson, warnings);

        foreach (var project in plan.Projects ?? Array.Empty<WebProjectPlan>())
        {
            if (string.IsNullOrWhiteSpace(project.RootPath))
                continue;

            var projectDataRoot = Path.Combine(project.RootPath, "data");
            ValidateKnownDataFile(projectDataRoot, "faq.json", $"projects/{project.Slug}/data/faq.json", ValidateFaqJson, warnings);
            ValidateKnownDataFile(projectDataRoot, "showcase.json", $"projects/{project.Slug}/data/showcase.json", ValidateShowcaseJson, warnings);
            ValidateKnownDataFile(projectDataRoot, "pricing.json", $"projects/{project.Slug}/data/pricing.json", ValidatePricingJson, warnings);
            ValidateKnownDataFile(projectDataRoot, "benchmarks.json", $"projects/{project.Slug}/data/benchmarks.json", ValidateBenchmarksJson, warnings);
        }
    }

    private static void ValidateKnownDataFile(
        string basePath,
        string fileName,
        string label,
        Action<JsonElement, string, List<string>> validator,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(basePath))
            return;

        var path = Path.Combine(basePath, fileName);
        if (!File.Exists(path))
            return;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            validator(doc.RootElement, label, warnings);
        }
        catch (Exception ex)
        {
            warnings.Add($"Data file '{label}' could not be read: {ex.Message}");
        }
    }

    private static void ValidateThemeAssets(SiteSpec spec, WebSitePlan plan, List<string> warnings)
    {
        if (spec is null || plan is null || warnings is null) return;
        if (string.IsNullOrWhiteSpace(spec.DefaultTheme)) return;

        var themeRoot = ResolveThemeRoot(spec, plan.RootPath, plan.ThemesRoot);
        if (string.IsNullOrWhiteSpace(themeRoot))
            return;

        var loader = new ThemeLoader();
        ThemeManifest? manifest = null;
        try
        {
            manifest = loader.Load(themeRoot, ResolveThemesRoot(spec, plan.RootPath, plan.ThemesRoot));
        }
        catch (Exception ex)
        {
            warnings.Add($"Theme '{spec.DefaultTheme}' failed to load: {ex.Message}");
            return;
        }

        if (manifest is null)
        {
            warnings.Add($"Theme '{spec.DefaultTheme}' was not found at '{themeRoot}'.");
            return;
        }

        ValidateAssetRegistryPaths(manifest.Assets, themeRoot, $"theme:{manifest.Name}", warnings);
        ValidateAssetRegistryPaths(spec.AssetRegistry, plan.RootPath, "site", warnings);
    }

    private static void ValidateLayoutHooks(SiteSpec spec, WebSitePlan plan, List<string> warnings)
    {
        if (spec is null || plan is null || warnings is null) return;
        if (string.IsNullOrWhiteSpace(spec.DefaultTheme)) return;

        var themeRoot = ResolveThemeRoot(spec, plan.RootPath, plan.ThemesRoot);
        if (string.IsNullOrWhiteSpace(themeRoot))
            return;

        var loader = new ThemeLoader();
        ThemeManifest? manifest = null;
        try
        {
            manifest = loader.Load(themeRoot, ResolveThemesRoot(spec, plan.RootPath, plan.ThemesRoot));
        }
        catch
        {
            return;
        }

        if (manifest is null)
            return;

        var engine = spec.ThemeEngine ?? manifest.Engine ?? "simple";
        var requiredTokens = ResolveRequiredLayoutTokens(engine);
        if (requiredTokens.Length == 0) return;

        var layoutNames = CollectLayoutNames(spec, manifest);
        foreach (var layoutName in layoutNames)
        {
            var layoutPath = loader.ResolveLayoutPath(themeRoot, manifest, layoutName);
            if (string.IsNullOrWhiteSpace(layoutPath) || !File.Exists(layoutPath))
                continue;

            var content = File.ReadAllText(layoutPath);
            foreach (var token in requiredTokens)
            {
                if (content.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                warnings.Add($"Layout '{layoutName}' is missing required token '{token}'. " +
                             "Per-page assets (e.g., Prism) may not load.");
            }
        }
    }

    private static string[] ResolveRequiredLayoutTokens(string engine)
    {
        if (engine.Equals("scriban", StringComparison.OrdinalIgnoreCase))
            return new[] { "extra_css_html", "extra_scripts_html" };
        return new[] { "EXTRA_CSS", "EXTRA_SCRIPTS" };
    }

    private static HashSet<string> CollectLayoutNames(SiteSpec spec, ThemeManifest manifest)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(manifest.DefaultLayout))
            names.Add(manifest.DefaultLayout);

        if (manifest.Layouts is not null)
        {
            foreach (var key in manifest.Layouts.Keys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                    names.Add(key);
            }
        }

        if (spec.Collections is not null)
        {
            foreach (var collection in spec.Collections)
            {
                var layout = collection?.DefaultLayout;
                if (!string.IsNullOrWhiteSpace(layout))
                    names.Add(layout);
            }
        }

        return names;
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

    private sealed record CollectionRoute(string Route, string File, bool Draft);

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

    private static string? ResolveThemeRoot(SiteSpec spec, string rootPath, string? planThemesRoot)
    {
        if (string.IsNullOrWhiteSpace(spec.DefaultTheme))
            return null;
        var basePath = ResolveThemesRoot(spec, rootPath, planThemesRoot);
        return string.IsNullOrWhiteSpace(basePath)
            ? null
            : Path.Combine(basePath, spec.DefaultTheme);
    }

    private static string ResolveThemesRoot(SiteSpec spec, string rootPath, string? planThemesRoot)
    {
        if (!string.IsNullOrWhiteSpace(planThemesRoot))
            return planThemesRoot;
        var themesRoot = string.IsNullOrWhiteSpace(spec.ThemesRoot) ? "themes" : spec.ThemesRoot;
        return Path.IsPathRooted(themesRoot) ? themesRoot : Path.Combine(rootPath, themesRoot);
    }

    private static bool IsExternalPath(string path)
    {
        return path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("//", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("data:", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateFaqJson(JsonElement root, string label, List<string> warnings)
    {
        if (!TryGetArray(root, "sections", out var sections))
        {
            warnings.Add($"Data file '{label}' missing required array 'sections'.");
            return;
        }

        var sectionIndex = 0;
        foreach (var section in sections)
        {
            if (!TryGetArray(section, "items", out var items))
            {
                warnings.Add($"Data file '{label}' section[{sectionIndex}] missing required array 'items'.");
                sectionIndex++;
                continue;
            }

            var itemIndex = 0;
            foreach (var item in items)
            {
                if (!HasAnyProperty(item, "question", "q", "title"))
                    warnings.Add($"Data file '{label}' section[{sectionIndex}].items[{itemIndex}] missing 'question'.");
                if (!HasAnyProperty(item, "answer", "a", "text", "summary"))
                    warnings.Add($"Data file '{label}' section[{sectionIndex}].items[{itemIndex}] missing 'answer'.");
                itemIndex++;
            }
            sectionIndex++;
        }
    }

    private static void ValidateShowcaseJson(JsonElement root, string label, List<string> warnings)
    {
        if (TryGetArray(root, "cards", out var cards))
        {
            var cardIndex = 0;
            foreach (var card in cards)
            {
                if (!HasAnyProperty(card, "title", "name"))
                    warnings.Add($"Data file '{label}' cards[{cardIndex}] missing 'title'.");

                if (TryGetObject(card, "gallery", out var gallery))
                {
                    if (!TryGetArray(gallery, "themes", out var themes))
                    {
                        warnings.Add($"Data file '{label}' cards[{cardIndex}].gallery missing array 'themes'.");
                    }
                    else
                    {
                        var themeIndex = 0;
                        foreach (var theme in themes)
                        {
                            if (!TryGetArray(theme, "slides", out _))
                                warnings.Add($"Data file '{label}' cards[{cardIndex}].gallery.themes[{themeIndex}] missing array 'slides'.");
                            themeIndex++;
                        }
                    }
                }

                cardIndex++;
            }
            return;
        }

        if (TryGetArray(root, "items", out var items))
        {
            var itemIndex = 0;
            foreach (var item in items)
            {
                if (!HasAnyProperty(item, "title", "name"))
                    warnings.Add($"Data file '{label}' items[{itemIndex}] missing 'name'.");
                itemIndex++;
            }
            return;
        }

        warnings.Add($"Data file '{label}' missing required array 'cards' or 'items'.");
    }

    private static void ValidatePricingJson(JsonElement root, string label, List<string> warnings)
    {
        if (!TryGetArray(root, "cards", out var cards))
        {
            warnings.Add($"Data file '{label}' missing required array 'cards'.");
            return;
        }

        var cardIndex = 0;
        foreach (var card in cards)
        {
            if (!HasAnyProperty(card, "title", "name"))
                warnings.Add($"Data file '{label}' cards[{cardIndex}] missing 'title'.");
            cardIndex++;
        }
    }

    private static void ValidateBenchmarksJson(JsonElement root, string label, List<string> warnings)
    {
        if (TryGetObject(root, "hero", out var hero))
        {
            if (!HasAnyProperty(hero, "title"))
                warnings.Add($"Data file '{label}' hero missing 'title'.");
        }

        if (TryGetObject(root, "about", out var about) && TryGetArray(about, "cards", out var cards))
        {
            var cardIndex = 0;
            foreach (var card in cards)
            {
                if (!HasAnyProperty(card, "title", "name"))
                    warnings.Add($"Data file '{label}' about.cards[{cardIndex}] missing 'title'.");
                cardIndex++;
            }
        }
    }

    private static bool TryGetArray(JsonElement element, string property, out JsonElement.ArrayEnumerator items)
    {
        items = default;
        if (element.ValueKind != JsonValueKind.Object)
            return false;
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
            return false;
        items = value.EnumerateArray();
        return true;
    }

    private static bool TryGetObject(JsonElement element, string property, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
            return false;
        if (!element.TryGetProperty(property, out var found) || found.ValueKind != JsonValueKind.Object)
            return false;
        value = found;
        return true;
    }

    private static bool HasAnyProperty(JsonElement element, params string[] properties)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        foreach (var prop in properties)
        {
            if (element.TryGetProperty(prop, out _))
                return true;
        }

        return false;
    }

    private static string ResolveSlugPath(string relativePath, string relativeDir, string? slugOverride)
    {
        var withoutExtension = NormalizePath(Path.ChangeExtension(relativePath, null) ?? string.Empty);
        return ApplySlugOverride(withoutExtension, slugOverride);
    }

    private static string ApplySlugOverride(string basePath, string? slugOverride)
    {
        if (string.IsNullOrWhiteSpace(slugOverride))
            return basePath;

        var normalized = NormalizePath(slugOverride);
        if (normalized.Contains('/'))
            return normalized;

        if (string.IsNullOrWhiteSpace(basePath))
            return normalized;

        var idx = basePath.LastIndexOf('/');
        if (idx < 0)
            return normalized;

        var parent = basePath.Substring(0, idx);
        if (string.IsNullOrWhiteSpace(parent))
            return normalized;

        return parent + "/" + normalized;
    }

    private static string? ResolveCollectionRootForFile(string rootPath, string input, string filePath)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var full = Path.IsPathRooted(input) ? input : Path.Combine(rootPath, input);
        if (!full.Contains('*'))
            return Path.GetFullPath(full);

        var normalized = full.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var parts = normalized.Split('*');
        if (parts.Length != 2)
            return Path.GetFullPath(full);

        var basePath = parts[0].TrimEnd(Path.DirectorySeparatorChar);
        var tail = parts[1].TrimStart(Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(basePath) || string.IsNullOrWhiteSpace(tail))
            return Path.GetFullPath(full);

        if (!filePath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(full);

        var relative = Path.GetRelativePath(basePath, filePath);
        var segments = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return Path.GetFullPath(full);

        var wildcardSegment = segments[0];
        var candidate = Path.Combine(basePath, wildcardSegment, tail);
        return Path.GetFullPath(candidate);
    }

    private static string ReplaceProjectPlaceholder(string output, string? projectSlug)
    {
        if (string.IsNullOrWhiteSpace(output))
            return output;
        if (string.IsNullOrWhiteSpace(projectSlug))
            return output.Replace("{project}", string.Empty, StringComparison.OrdinalIgnoreCase);
        return output.Replace("{project}", projectSlug, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveProjectSlug(WebSitePlan plan, string filePath)
    {
        foreach (var project in plan.Projects)
        {
            if (string.IsNullOrWhiteSpace(project.ContentPath))
                continue;

            if (filePath.StartsWith(project.ContentPath, StringComparison.OrdinalIgnoreCase))
                return project.Slug;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCollectionFilesWithWildcard(string path)
    {
        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var parts = normalized.Split('*');
        if (parts.Length != 2)
            return Array.Empty<string>();

        var basePath = parts[0].TrimEnd(Path.DirectorySeparatorChar);
        var tail = parts[1].TrimStart(Path.DirectorySeparatorChar);
        if (!Directory.Exists(basePath))
            return Array.Empty<string>();

        var results = new List<string>();
        foreach (var dir in Directory.GetDirectories(basePath))
        {
            var candidate = string.IsNullOrEmpty(tail) ? dir : Path.Combine(dir, tail);
            if (!Directory.Exists(candidate))
                continue;
            results.AddRange(Directory.EnumerateFiles(candidate, "*.md", SearchOption.AllDirectories));
        }

        return results;
    }
}
