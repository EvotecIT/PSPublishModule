using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

/// <summary>Theme contract, layout, and token verification rules.</summary>
public static partial class WebSiteVerifier
{
    private static void ValidateThemeFeatureContract(SiteSpec spec, WebSitePlan plan, List<string> warnings)
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

        var explicitFeatures = NormalizeFeatures(spec.Features);
        var inferredFeatures = explicitFeatures.Count == 0 ? InferFeatures(spec) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var enabled = explicitFeatures.Count > 0 ? explicitFeatures : inferredFeatures;

        if (explicitFeatures.Count == 0 && enabled.Count > 0)
        {
            warnings.Add("Theme contract: site does not declare 'features' in site.json; using best-effort inference for theme checks. " +
                         "Add features (e.g., [\"docs\",\"apiDocs\"]) to make this deterministic across sites.");
        }

        if (enabled.Count == 0)
            return;

        var supported = NormalizeFeatures(manifest.Features);
        var schemaVersion = manifest.SchemaVersion ?? manifest.ContractVersion ?? 1;
        if (schemaVersion >= 2 && supported.Count == 0)
        {
            warnings.Add($"Theme contract: '{manifest.Name}' schemaVersion 2 should declare 'features' to make capabilities explicit.");
        }

        foreach (var feature in enabled)
        {
            if (supported.Count > 0 && !supported.Contains(feature))
            {
                warnings.Add($"Theme contract: '{manifest.Name}' does not declare support for feature '{feature}', but the site enables it.");
            }
        }

        // Best-practice nudge: for high-drift features, encourage explicit contracts in schemaVersion 2 themes.
        if (schemaVersion >= 2)
        {
            if (enabled.Contains("apidocs") && !HasFeatureContract(manifest, "apidocs"))
            {
                warnings.Add($"Best practice: site enables 'apiDocs' but theme '{manifest.Name}' does not define featureContracts.apiDocs. " +
                             "Add requiredPartials (api-header/api-footer) and requiredCssSelectors to prevent regressions across sites.");
            }

            if (enabled.Contains("docs") && !HasFeatureContract(manifest, "docs"))
            {
                warnings.Add($"Best practice: site enables 'docs' but theme '{manifest.Name}' does not define featureContracts.docs. " +
                             "Add requiredLayouts/requiredSlots to make docs support deterministic across themes.");
            }
        }

        ValidateThemeFeatureContracts(spec, plan, themeRoot, loader, manifest, enabled, warnings);

        if (enabled.Contains("apidocs"))
        {
            var header = loader.ResolvePartialPath(themeRoot, manifest, "api-header");
            var footer = loader.ResolvePartialPath(themeRoot, manifest, "api-footer");
            if (string.IsNullOrWhiteSpace(header) || !File.Exists(header) ||
                string.IsNullOrWhiteSpace(footer) || !File.Exists(footer))
            {
                warnings.Add($"Theme contract: site uses feature 'apiDocs' but theme '{manifest.Name}' does not provide 'api-header.html' and 'api-footer.html' fragments under partials. " +
                             "API reference pages may render without site navigation unless the pipeline provides headerHtml/footerHtml.");
            }
        }

        if (enabled.Contains("docs"))
        {
            var docsLayout = loader.ResolveLayoutPath(themeRoot, manifest, "docs");
            if (string.IsNullOrWhiteSpace(docsLayout) || !File.Exists(docsLayout))
            {
                warnings.Add($"Theme contract: site uses feature 'docs' but theme '{manifest.Name}' does not provide a 'docs' layout.");
            }
        }
    }

    private static bool HasFeatureContract(ThemeManifest manifest, string feature)
    {
        if (manifest is null || string.IsNullOrWhiteSpace(feature))
            return false;
        if (manifest.FeatureContracts is null || manifest.FeatureContracts.Count == 0)
            return false;

        var key = NormalizeFeatureName(feature);
        if (string.IsNullOrWhiteSpace(key))
            return false;

        foreach (var entry in manifest.FeatureContracts)
        {
            var candidate = NormalizeFeatureName(entry.Key);
            if (string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void ValidateThemeFeatureContracts(
        SiteSpec spec,
        WebSitePlan plan,
        string themeRoot,
        ThemeLoader loader,
        ThemeManifest manifest,
        HashSet<string> enabled,
        List<string> warnings)
    {
        if (spec is null || plan is null || string.IsNullOrWhiteSpace(themeRoot) || loader is null || manifest is null || enabled is null || warnings is null)
            return;
        if (manifest.FeatureContracts is null || manifest.FeatureContracts.Count == 0)
            return;

        var normalizedMap = new Dictionary<string, ThemeFeatureContractSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in manifest.FeatureContracts)
        {
            var key = NormalizeFeatureName(kvp.Key);
            if (string.IsNullOrWhiteSpace(key) || kvp.Value is null)
                continue;
            normalizedMap[key] = kvp.Value;
        }

        foreach (var feature in enabled)
        {
            if (!normalizedMap.TryGetValue(feature, out var contract) || contract is null)
                continue;

            ValidateFeatureRequiredLayouts(feature, contract, themeRoot, loader, manifest, warnings);
            ValidateFeatureRequiredPartials(feature, contract, themeRoot, loader, manifest, warnings);
            ValidateFeatureRequiredSlots(feature, contract, themeRoot, loader, manifest, warnings);
            ValidateFeatureRequiredSurfaces(feature, contract, spec, warnings);
            ValidateFeatureCssSelectors(feature, contract, spec, plan, themeRoot, loader, manifest, warnings);
        }
    }

    private static void ValidateFeatureRequiredLayouts(
        string feature,
        ThemeFeatureContractSpec contract,
        string themeRoot,
        ThemeLoader loader,
        ThemeManifest manifest,
        List<string> warnings)
    {
        foreach (var layout in contract.RequiredLayouts ?? Array.Empty<string>())
        {
            var name = layout?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var path = loader.ResolveLayoutPath(themeRoot, manifest, name);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                warnings.Add($"Theme contract: feature '{feature}' requires layout '{name}', but theme '{manifest.Name}' does not provide it.");
        }
    }

    private static void ValidateFeatureRequiredPartials(
        string feature,
        ThemeFeatureContractSpec contract,
        string themeRoot,
        ThemeLoader loader,
        ThemeManifest manifest,
        List<string> warnings)
    {
        foreach (var partial in contract.RequiredPartials ?? Array.Empty<string>())
        {
            var name = partial?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var path = loader.ResolvePartialPath(themeRoot, manifest, name);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                warnings.Add($"Theme contract: feature '{feature}' requires partial '{name}', but theme '{manifest.Name}' does not provide it.");
        }
    }

    private static void ValidateFeatureRequiredSlots(
        string feature,
        ThemeFeatureContractSpec contract,
        string themeRoot,
        ThemeLoader loader,
        ThemeManifest manifest,
        List<string> warnings)
    {
        foreach (var slot in contract.RequiredSlots ?? Array.Empty<string>())
        {
            var name = slot?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (manifest.Slots is null || !manifest.Slots.ContainsKey(name))
            {
                warnings.Add($"Theme contract: feature '{feature}' requires slot '{name}', but theme '{manifest.Name}' does not define it in 'slots'.");
                continue;
            }

            var path = loader.ResolvePartialPath(themeRoot, manifest, name);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                warnings.Add($"Theme contract: feature '{feature}' requires slot '{name}', but theme '{manifest.Name}' slot mapping does not resolve to a partial on disk.");
        }
    }

    private static void ValidateFeatureRequiredSurfaces(
        string feature,
        ThemeFeatureContractSpec contract,
        SiteSpec spec,
        List<string> warnings)
    {
        var required = contract.RequiredSurfaces ?? Array.Empty<string>();
        if (required.Length == 0)
            return;

        var siteSurfaces = spec.Navigation?.Surfaces ?? Array.Empty<NavigationSurfaceSpec>();
        if (siteSurfaces.Length == 0)
            return; // only enforce when the site opts into explicit surfaces

        var defined = new HashSet<string>(siteSurfaces
            .Where(s => s is not null && !string.IsNullOrWhiteSpace(s.Name))
            .Select(s => s.Name.Trim()), StringComparer.OrdinalIgnoreCase);

        foreach (var surface in required)
        {
            var name = surface?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (!defined.Contains(name))
                warnings.Add($"Navigation lint: theme requires nav surface '{name}' for feature '{feature}', but site.json Navigation.Surfaces does not define it.");
        }
    }

    private static void ValidateFeatureCssSelectors(
        string feature,
        ThemeFeatureContractSpec contract,
        SiteSpec spec,
        WebSitePlan plan,
        string themeRoot,
        ThemeLoader loader,
        ThemeManifest manifest,
        List<string> warnings)
    {
        var selectors = (contract.RequiredCssSelectors ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (selectors.Length == 0)
            return;

        var hrefs = (contract.CssHrefs ?? Array.Empty<string>())
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => h.Trim())
            .ToArray();

        if (hrefs.Length == 0)
        {
            var sampleRoute = ResolveFeatureSampleRoute(feature);
            hrefs = ResolveCssHrefsForRoute(spec, themeRoot, loader, manifest, sampleRoute);
        }

        if (hrefs.Length == 0)
        {
            warnings.Add($"Theme CSS contract: feature '{feature}' declares requiredCssSelectors but no CSS hrefs could be determined to validate.");
            return;
        }

        var cssPaths = hrefs
            .Select(href => TryResolveCssDiskPath(href, spec, plan, themeRoot))
            .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
            .Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (cssPaths.Length == 0)
        {
            var preview = string.Join(", ", hrefs.Take(4));
            var more = hrefs.Length > 4 ? $" (+{hrefs.Length - 4} more)" : string.Empty;
            warnings.Add($"Theme CSS contract: feature '{feature}' could not resolve CSS hrefs to local files for validation: {preview}{more}.");
            return;
        }

        var combined = new StringBuilder();
        foreach (var path in cssPaths)
        {
            try
            {
                combined.AppendLine(File.ReadAllText(path));
            }
            catch
            {
                // ignore unreadable CSS file to keep the check best-effort
            }
        }

        var css = combined.ToString();
        if (string.IsNullOrWhiteSpace(css))
            return;

        var missing = selectors
            .Where(sel => css.IndexOf(sel, StringComparison.OrdinalIgnoreCase) < 0)
            .ToArray();

        if (missing.Length == 0)
            return;

        var missingPreview = string.Join(", ", missing.Take(8));
        var missingMore = missing.Length > 8 ? $" (+{missing.Length - 8} more)" : string.Empty;
        var checkedPreview = string.Join(", ", cssPaths.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).Take(4));
        warnings.Add($"Theme CSS contract: feature '{feature}' missing expected selectors: {missingPreview}{missingMore} (checked: {checkedPreview}).");
    }

    private static string ResolveFeatureSampleRoute(string feature)
    {
        if (string.Equals(feature, "docs", StringComparison.OrdinalIgnoreCase))
            return "/docs/";
        if (string.Equals(feature, "apidocs", StringComparison.OrdinalIgnoreCase))
            return "/api/";
        if (string.Equals(feature, "blog", StringComparison.OrdinalIgnoreCase))
            return "/blog/";
        if (string.Equals(feature, "search", StringComparison.OrdinalIgnoreCase))
            return "/search/";
        if (string.Equals(feature, "notfound", StringComparison.OrdinalIgnoreCase))
            return "/404.html";

        return "/";
    }

    private static string[] ResolveCssHrefsForRoute(
        SiteSpec spec,
        string themeRoot,
        ThemeLoader loader,
        ThemeManifest manifest,
        string route)
    {
        // Best-effort: prefer site asset registry, fall back to theme assets.
        var css = new List<string>();

        if (spec.AssetRegistry is not null)
            css.AddRange(ResolveCssHrefsFromAssets(spec.AssetRegistry, route));

        if (css.Count == 0 && manifest.Assets is not null)
            css.AddRange(ResolveCssHrefsFromThemeAssets(themeRoot, spec, manifest.Assets, route));

        return css
            .Where(static h => !string.IsNullOrWhiteSpace(h))
            .Select(static h => h.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> ResolveCssHrefsFromAssets(AssetRegistrySpec assets, string route)
    {
        if (assets is null)
            yield break;

        var bundleMap = (assets.Bundles ?? Array.Empty<AssetBundleSpec>())
            .Where(b => b is not null && !string.IsNullOrWhiteSpace(b.Name))
            .ToDictionary(b => b.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in assets.RouteBundles ?? Array.Empty<RouteBundleSpec>())
        {
            if (mapping is null || string.IsNullOrWhiteSpace(mapping.Match))
                continue;
            if (!GlobMatch(mapping.Match, route))
                continue;

            foreach (var name in mapping.Bundles ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                if (!bundleMap.TryGetValue(name, out var bundle) || bundle is null)
                    continue;

                foreach (var href in bundle.Css ?? Array.Empty<string>())
                {
                    if (!string.IsNullOrWhiteSpace(href))
                        yield return href;
                }
            }
        }
    }

    private static IEnumerable<string> ResolveCssHrefsFromThemeAssets(string themeRoot, SiteSpec spec, AssetRegistrySpec assets, string route)
    {
        // Theme asset paths are normalized to output URLs during build, but in theme.manifest.json they are theme-local.
        var themesFolder = string.IsNullOrWhiteSpace(spec.ThemesRoot) || Path.IsPathRooted(spec.ThemesRoot)
            ? "themes"
            : spec.ThemesRoot.Trim().TrimStart('/', '\\');

        foreach (var href in ResolveCssHrefsFromAssets(assets, route))
        {
            if (string.IsNullOrWhiteSpace(href))
                continue;

            var trimmed = href.Trim();
            if (trimmed.StartsWith("/", StringComparison.Ordinal))
            {
                yield return trimmed;
                continue;
            }

            // Convert theme-local to output URL so TryResolveCssDiskPath can map it deterministically.
            yield return "/" + themesFolder.TrimEnd('/', '\\') + "/" + spec.DefaultTheme!.Trim().Trim('/', '\\') + "/" + trimmed.TrimStart('/', '\\');
        }
    }

    private static string? TryResolveCssDiskPath(string href, SiteSpec spec, WebSitePlan plan, string themeRoot)
    {
        if (string.IsNullOrWhiteSpace(href))
            return null;

        var value = href.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return null;

        if (Path.IsPathRooted(value))
            return File.Exists(value) ? Path.GetFullPath(value) : null;

        if (!value.StartsWith("/", StringComparison.Ordinal))
        {
            // Relative href: ambiguous. Try theme root first (theme-local bundles), then site root.
            var themeCandidate = Path.GetFullPath(Path.Combine(themeRoot, value.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(themeCandidate))
                return themeCandidate;

            var siteCandidate = Path.GetFullPath(Path.Combine(plan.RootPath, value.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(siteCandidate))
                return siteCandidate;

            return null;
        }

        // Root-relative href: try mapping to static/ overlay.
        var staticCandidate = Path.Combine(plan.RootPath, "static", value.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(staticCandidate))
            return Path.GetFullPath(staticCandidate);

        // Root-relative href under themes: map back to theme root.
        var themesFolder = string.IsNullOrWhiteSpace(spec.ThemesRoot) || Path.IsPathRooted(spec.ThemesRoot)
            ? "themes"
            : spec.ThemesRoot.Trim().TrimStart('/', '\\');
        var prefix = "/" + themesFolder.TrimEnd('/', '\\') + "/" + spec.DefaultTheme!.Trim().Trim('/', '\\') + "/";
        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var rel = value.Substring(prefix.Length).Replace('/', Path.DirectorySeparatorChar);
            var themePath = Path.Combine(themeRoot, rel);
            if (File.Exists(themePath))
                return Path.GetFullPath(themePath);
        }

        return null;
    }

    // Uses verifier's shared GlobMatch helper (defined in WebSiteVerifier.ThemeAndNavigationRules.cs).

    private static HashSet<string> NormalizeFeatures(string[]? features)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (features is null || features.Length == 0)
            return set;
        foreach (var feature in features)
        {
            var normalized = NormalizeFeatureName(feature);
            if (!string.IsNullOrWhiteSpace(normalized))
                set.Add(normalized);
        }
        return set;
    }

    private static string NormalizeFeatureName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        if (trimmed.Equals("apiDocs", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("apidocs", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("api", StringComparison.OrdinalIgnoreCase))
            return "apidocs";

        if (trimmed.Equals("notFound", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("404", StringComparison.OrdinalIgnoreCase))
            return "notfound";

        return trimmed.ToLowerInvariant();
    }

    private static HashSet<string> InferFeatures(SiteSpec spec)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (spec.Collections is not null)
        {
            foreach (var c in spec.Collections)
            {
                if (c is null) continue;
                if (!string.IsNullOrWhiteSpace(c.Name) && c.Name.Equals("docs", StringComparison.OrdinalIgnoreCase))
                    set.Add("docs");
                if (!string.IsNullOrWhiteSpace(c.Output) && c.Output.StartsWith("/docs", StringComparison.OrdinalIgnoreCase))
                    set.Add("docs");
                if (!string.IsNullOrWhiteSpace(c.Name) && c.Name.Equals("blog", StringComparison.OrdinalIgnoreCase))
                    set.Add("blog");
                if (!string.IsNullOrWhiteSpace(c.Output) && c.Output.StartsWith("/blog", StringComparison.OrdinalIgnoreCase))
                    set.Add("blog");
            }
        }

        if (spec.AssetRegistry?.RouteBundles is not null)
        {
            foreach (var route in spec.AssetRegistry.RouteBundles)
            {
                var match = route?.Match;
                if (!string.IsNullOrWhiteSpace(match) && match.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
                {
                    set.Add("apidocs");
                    break;
                }
            }
        }

        if (!set.Contains("apidocs") && spec.Navigation?.Menus is not null)
        {
            foreach (var menu in spec.Navigation.Menus)
            {
                if (menu?.Items is null) continue;
                if (ContainsApiLink(menu.Items))
                {
                    set.Add("apidocs");
                    break;
                }
            }
        }

        return set;
    }

    private static bool ContainsApiLink(MenuItemSpec[]? items)
    {
        if (items is null || items.Length == 0) return false;
        foreach (var item in items)
        {
            if (item is null) continue;
            if (!string.IsNullOrWhiteSpace(item.Url) && item.Url.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
                return true;
            if (ContainsApiLink(item.Items))
                return true;
        }
        return false;
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

        var manifestPath = ResolveThemeManifestPath(themeRoot);
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            warnings.Add($"Theme '{spec.DefaultTheme}' does not define 'theme.manifest.json' (or legacy 'theme.json'). Add an explicit manifest to make theme behavior portable.");
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

        if (Path.GetFileName(manifestPath).Equals("theme.json", StringComparison.OrdinalIgnoreCase))
            warnings.Add($"Theme contract: '{manifest.Name}' uses legacy manifest file 'theme.json'. Prefer 'theme.manifest.json'.");

        var contractVersion = ResolveThemeContractVersion(manifest, warnings);

        if (string.IsNullOrWhiteSpace(manifest.Engine))
        {
            warnings.Add($"Theme contract: '{manifest.Name}' does not set 'engine'. Set 'simple' or 'scriban' explicitly.");
        }
        else if (!manifest.Engine.Equals("simple", StringComparison.OrdinalIgnoreCase) &&
                 !manifest.Engine.Equals("scriban", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"Theme contract: '{manifest.Name}' uses unsupported engine '{manifest.Engine}'. Supported values: simple, scriban.");
        }

        ValidateThemeManifestPath(manifest.LayoutsPath, $"theme:{manifest.Name} layoutsPath", warnings);
        ValidateThemeManifestPath(manifest.PartialsPath, $"theme:{manifest.Name} partialsPath", warnings);
        ValidateThemeManifestPath(manifest.AssetsPath, $"theme:{manifest.Name} assetsPath", warnings);
        ValidateThemeManifestPath(manifest.ScriptsPath, $"theme:{manifest.Name} scriptsPath", warnings);

        ValidateThemeMappedPaths(manifest.Layouts, $"theme:{manifest.Name} layouts", warnings);
        ValidateThemeMappedPaths(manifest.Partials, $"theme:{manifest.Name} partials", warnings);
        ValidateThemeMappedPaths(manifest.Slots, $"theme:{manifest.Name} slots", warnings);
        ValidateThemeAssetPortability(manifest, warnings);
        ValidateThemeSlots(loader, themeRoot, manifest, warnings);

        if (contractVersion >= 2)
        {
            if (string.IsNullOrWhiteSpace(manifest.DefaultLayout))
                warnings.Add($"Theme contract: '{manifest.Name}' schemaVersion 2 should set 'defaultLayout'.");

            if (manifest.Slots is null || manifest.Slots.Count == 0)
                warnings.Add($"Theme contract: '{manifest.Name}' schemaVersion 2 should define 'slots' for portable hook points.");

            if (string.IsNullOrWhiteSpace(manifest.ScriptsPath))
                warnings.Add($"Theme contract: '{manifest.Name}' schemaVersion 2 should set 'scriptsPath' for portable JS assets.");
        }

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


    private static int ResolveThemeContractVersion(ThemeManifest manifest, List<string> warnings)
    {
        var schemaVersion = manifest.SchemaVersion;
        var legacyVersion = manifest.ContractVersion;

        if (schemaVersion is null && legacyVersion is null)
        {
            warnings.Add($"Theme contract: '{manifest.Name}' does not declare 'schemaVersion'. Set 'schemaVersion': 2 for portable reusable themes.");
        }
        else if (schemaVersion is null && legacyVersion is not null)
        {
            warnings.Add($"Theme contract: '{manifest.Name}' uses legacy 'contractVersion'. Rename to 'schemaVersion' to keep contract intent explicit.");
        }

        if (schemaVersion is not null && legacyVersion is not null && schemaVersion.Value != legacyVersion.Value)
        {
            warnings.Add($"Theme contract: '{manifest.Name}' defines schemaVersion={schemaVersion.Value} and contractVersion={legacyVersion.Value}. Keep them aligned or remove the legacy field.");
        }

        var resolved = schemaVersion ?? legacyVersion ?? 1;
        if (resolved != 1 && resolved != 2)
            warnings.Add($"Theme contract: '{manifest.Name}' uses unsupported schemaVersion '{resolved}'. Supported values: 1, 2.");

        return resolved;
    }


    private static void ValidateThemeSlots(ThemeLoader loader, string themeRoot, ThemeManifest manifest, List<string> warnings)
    {
        if (loader is null || string.IsNullOrWhiteSpace(themeRoot) || manifest is null || warnings is null)
            return;
        if (manifest.Slots is null || manifest.Slots.Count == 0)
            return;

        foreach (var slot in manifest.Slots)
        {
            var key = slot.Key ?? string.Empty;
            var partialName = slot.Value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                warnings.Add($"Theme '{manifest.Name}' contains an empty slot name.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(partialName))
            {
                warnings.Add($"Theme '{manifest.Name}' slot '{key}' has an empty partial mapping.");
                continue;
            }

            var path = loader.ResolvePartialPath(themeRoot, manifest, partialName);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                warnings.Add($"Theme '{manifest.Name}' slot '{key}' maps to missing partial '{partialName}'.");
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


    private static string? ResolveThemeManifestPath(string themeRoot)
    {
        if (string.IsNullOrWhiteSpace(themeRoot))
            return null;

        var contractPath = Path.Combine(themeRoot, "theme.manifest.json");
        if (File.Exists(contractPath))
            return contractPath;

        var legacyPath = Path.Combine(themeRoot, "theme.json");
        return File.Exists(legacyPath) ? legacyPath : null;
    }

}
