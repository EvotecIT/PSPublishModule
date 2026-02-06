using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PowerForge.Web;

/// <summary>Verifies site content and routing integrity.</summary>
public static class WebSiteVerifier
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex MarkdownFenceRegex = new("```[\\s\\S]*?```", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex MarkdownRawHtmlRegex = new("<\\s*(b|i|strong|em|u|h[1-6]|p|ul|ol|li|br)\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);

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
                ValidateMarkdownHygiene(plan.RootPath, file, collection.Name, body, warnings);

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
        ValidateThemeContract(spec, plan, warnings);
        ValidateLayoutHooks(spec, plan, warnings);
        ValidateThemeTokens(spec, plan, warnings);
        ValidatePrismAssets(spec, plan, warnings);
        ValidateTocCoverage(spec, plan, collectionRoutes, warnings);
        ValidateNavigationDefaults(spec, warnings);
        ValidateSiteNavExport(spec, plan, warnings);
        ValidateNotFoundAssetBundles(spec, routes.Keys, warnings);

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

    private static void ValidateThemeContract(SiteSpec spec, WebSitePlan plan, List<string> warnings)
    {
        if (spec is null || plan is null || warnings is null) return;
        if (string.IsNullOrWhiteSpace(spec.DefaultTheme)) return;

        var themeRoot = ResolveThemeRoot(spec, plan.RootPath, plan.ThemesRoot);
        if (string.IsNullOrWhiteSpace(themeRoot))
            return;

        var manifestPath = Path.Combine(themeRoot, "theme.json");
        if (!File.Exists(manifestPath))
        {
            warnings.Add($"Theme '{spec.DefaultTheme}' does not define 'theme.json'. Add an explicit manifest to make theme behavior portable.");
            return;
        }

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

        if (string.IsNullOrWhiteSpace(manifest.Engine))
        {
            warnings.Add($"Theme '{manifest.Name}' does not set 'engine' in theme.json. Set 'simple' or 'scriban' explicitly.");
        }
        else if (!manifest.Engine.Equals("simple", StringComparison.OrdinalIgnoreCase) &&
                 !manifest.Engine.Equals("scriban", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"Theme '{manifest.Name}' uses unsupported engine '{manifest.Engine}'. Supported values: simple, scriban.");
        }

        ValidateThemeManifestPath(manifest.LayoutsPath, $"theme:{manifest.Name} layoutsPath", warnings);
        ValidateThemeManifestPath(manifest.PartialsPath, $"theme:{manifest.Name} partialsPath", warnings);
        ValidateThemeManifestPath(manifest.AssetsPath, $"theme:{manifest.Name} assetsPath", warnings);

        ValidateThemeMappedPaths(manifest.Layouts, $"theme:{manifest.Name} layouts", warnings);
        ValidateThemeMappedPaths(manifest.Partials, $"theme:{manifest.Name} partials", warnings);
        ValidateThemeAssetPortability(manifest, warnings);

        if (!string.IsNullOrWhiteSpace(manifest.DefaultLayout))
        {
            var defaultLayoutPath = loader.ResolveLayoutPath(themeRoot, manifest, manifest.DefaultLayout);
            if (string.IsNullOrWhiteSpace(defaultLayoutPath) || !File.Exists(defaultLayoutPath))
            {
                warnings.Add($"Theme '{manifest.Name}' defaultLayout '{manifest.DefaultLayout}' does not resolve to a layout file.");
            }
        }

        if (manifest.Tokens is { Count: > 0 })
        {
            var tokensPartialPath = loader.ResolvePartialPath(themeRoot, manifest, "theme-tokens");
            if (string.IsNullOrWhiteSpace(tokensPartialPath) || !File.Exists(tokensPartialPath))
            {
                warnings.Add($"Theme '{manifest.Name}' defines tokens but does not provide partial 'theme-tokens'. Tokens will not be emitted unless layouts render them manually.");
            }
        }
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

    private static void ValidateThemeTokens(SiteSpec spec, WebSitePlan plan, List<string> warnings)
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

        var hasThemeTokens = false;
        if (manifest.Partials is not null && manifest.Partials.ContainsKey("theme-tokens"))
            hasThemeTokens = true;
        else
        {
            var partialPath = loader.ResolvePartialPath(themeRoot, manifest, "theme-tokens");
            if (!string.IsNullOrWhiteSpace(partialPath) && File.Exists(partialPath))
                hasThemeTokens = true;
        }

        if (!hasThemeTokens)
            return;

        var layoutNames = CollectLayoutNames(spec, manifest);
        foreach (var layoutName in layoutNames)
        {
            var layoutPath = loader.ResolveLayoutPath(themeRoot, manifest, layoutName);
            if (string.IsNullOrWhiteSpace(layoutPath) || !File.Exists(layoutPath))
                continue;

            var content = File.ReadAllText(layoutPath);
            if (content.IndexOf("theme-tokens", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;

            warnings.Add($"Layout '{layoutName}' does not include 'theme-tokens' partial. " +
                         "Design tokens may not apply consistently across pages.");
        }
    }

    private static void ValidateNavigationDefaults(SiteSpec spec, List<string> warnings)
    {
        if (spec is null || warnings is null) return;

        var nav = spec.Navigation;
        if (nav is null) return;

        var hasMenus = nav.Menus is not null && nav.Menus.Length > 0;
        var hasAuto = nav.Auto is not null && nav.Auto.Length > 0;
        if (!hasMenus && !hasAuto && !nav.AutoDefaults)
        {
            warnings.Add("Navigation.AutoDefaults is disabled and no menus/auto navigation are defined.");
        }

        if (hasMenus)
        {
            var menus = nav.Menus!;
            var mainMenu = menus.FirstOrDefault(menu => string.Equals(menu.Name, "main", StringComparison.OrdinalIgnoreCase));
            if (mainMenu?.Items is { Length: > 0 })
            {
                var hasHome = mainMenu.Items.Any(item =>
                    string.Equals(item.Url, "/", StringComparison.OrdinalIgnoreCase));
                if (!hasHome)
                    warnings.Add("Navigation main menu does not contain '/'. Add a Home link to keep global navigation consistent.");
            }
        }
    }

    private static void ValidateSiteNavExport(SiteSpec spec, WebSitePlan plan, List<string> warnings)
    {
        if (spec is null || plan is null || warnings is null) return;

        var nav = spec.Navigation;
        if (nav is null || nav.Menus is null || nav.Menus.Length == 0) return;
        if (nav.Auto is not null && nav.Auto.Length > 0) return;

        var dataRoot = string.IsNullOrWhiteSpace(spec.DataRoot) ? "data" : spec.DataRoot;
        var dataPath = Path.IsPathRooted(dataRoot)
            ? Path.Combine(dataRoot, "site-nav.json")
            : Path.Combine(plan.RootPath, dataRoot, "site-nav.json");
        var staticPath = Path.Combine(plan.RootPath, "static", "data", "site-nav.json");

        var navPath = File.Exists(dataPath) ? dataPath : (File.Exists(staticPath) ? staticPath : null);
        if (string.IsNullOrWhiteSpace(navPath))
            return;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(navPath));
            if (!doc.RootElement.TryGetProperty("primary", out var primary) ||
                primary.ValueKind != JsonValueKind.Array)
                return;

            var menu = nav.Menus.FirstOrDefault(m => string.Equals(m.Name, "main", StringComparison.OrdinalIgnoreCase));
            if (menu is null || menu.Items is null || menu.Items.Length == 0) return;

            var expected = menu.Items
                .Where(i => !string.IsNullOrWhiteSpace(i.Url))
                .Select(i => (Href: i.Url ?? string.Empty, Text: i.Text ?? i.Title))
                .ToArray();

            var actual = primary.EnumerateArray()
                .Select(item =>
                {
                    var href = item.TryGetProperty("href", out var h) ? h.GetString() : null;
                    var text = item.TryGetProperty("text", out var t) ? t.GetString() : null;
                    return (Href: href ?? string.Empty, Text: text ?? string.Empty);
                })
                .Where(i => !string.IsNullOrWhiteSpace(i.Href))
                .ToArray();

            if (expected.Length != actual.Length)
            {
                warnings.Add($"site-nav.json primary count ({actual.Length}) differs from Navigation main menu ({expected.Length}).");
                return;
            }

            for (var i = 0; i < expected.Length; i++)
            {
                if (!string.Equals(expected[i].Href, actual[i].Href, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(expected[i].Text, actual[i].Text, StringComparison.Ordinal))
                {
                    warnings.Add("site-nav.json primary entries differ from Navigation main menu. " +
                                 "Regenerate the nav export or update the custom data file.");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to read site-nav.json for navigation consistency: {ex.Message}");
        }
    }

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
            warnings.Add($"{label} should use local relative paths in theme.json. Move external URLs to site-level overrides when possible: {path}");
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
        if (Path.IsPathRooted(path))
            return false;
        if (path.StartsWith("/", StringComparison.Ordinal) || path.StartsWith("\\", StringComparison.Ordinal))
            return false;

        var normalized = path.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.All(segment => !string.Equals(segment, "..", StringComparison.Ordinal));
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

    private static bool GlobMatch(string pattern, string value)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        var regex = "^" + Regex.Escape(pattern)
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*") + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
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

    private static void ValidateMarkdownHygiene(string rootPath, string filePath, string? collectionName, string body, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(body))
            return;
        if (string.IsNullOrWhiteSpace(collectionName) ||
            collectionName.IndexOf("doc", StringComparison.OrdinalIgnoreCase) < 0)
            return;

        var markdownHygieneWarnings = warnings.Count(warning =>
            warning.StartsWith("Markdown hygiene:", StringComparison.OrdinalIgnoreCase));
        if (markdownHygieneWarnings >= 10)
            return;

        var withoutCodeBlocks = MarkdownFenceRegex.Replace(body, string.Empty);
        var matches = MarkdownRawHtmlRegex.Matches(withoutCodeBlocks);
        if (matches.Count == 0)
            return;

        var tags = matches
            .Select(match => match.Groups[1].Value.ToLowerInvariant())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();

        if (tags.Length == 0)
            return;

        var relative = Path.GetRelativePath(rootPath, filePath).Replace('\\', '/');
        warnings.Add($"Markdown hygiene: '{relative}' contains raw HTML tags ({string.Join(", ", tags)}). Prefer Markdown syntax when possible.");
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
