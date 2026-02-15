using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PowerForge.Web;

public static partial class WebSiteVerifier
{
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
        {
            var requiredPreview = string.Join(", ", required
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => NormalizeSurfaceName(s))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8));
            if (string.IsNullOrWhiteSpace(requiredPreview))
                requiredPreview = "main,docs,apidocs";

            warnings.Add($"Theme contract: feature '{feature}' requires nav surfaces ({requiredPreview}), but site.json Navigation.Surfaces is empty. Define explicit surfaces so themes and API docs can resolve navigation deterministically.");
            return;
        }

        var defined = new HashSet<string>(siteSurfaces
            .Where(s => s is not null && !string.IsNullOrWhiteSpace(s.Name))
            .Select(s => NormalizeSurfaceName(s.Name))
            .Where(s => !string.IsNullOrWhiteSpace(s)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var surface in required)
        {
            var name = surface?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var normalized = NormalizeSurfaceName(name);
            if (string.IsNullOrWhiteSpace(normalized))
                continue;

            if (!defined.Contains(normalized))
            {
                var display = string.Equals(normalized, name, StringComparison.OrdinalIgnoreCase)
                    ? normalized
                    : $"{normalized} (from '{name}')";
                warnings.Add($"Theme contract: feature '{feature}' requires nav surface '{display}', but site.json Navigation.Surfaces does not define it.");
            }
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

        var explicitHrefs = (contract.CssHrefs ?? Array.Empty<string>())
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => h.Trim())
            .ToArray();

        var hrefs = explicitHrefs;
        var inferred = false;
        if (hrefs.Length == 0)
        {
            var sampleRoute = ResolveFeatureSampleRoute(feature);
            hrefs = ResolveCssHrefsForRoute(spec, themeRoot, loader, manifest, sampleRoute);
            inferred = hrefs.Length > 0;
        }

        if (hrefs.Length == 0)
        {
            warnings.Add($"Theme CSS contract: feature '{feature}' declares requiredCssSelectors but no CSS hrefs could be determined to validate.");
            return;
        }

        if (explicitHrefs.Length == 0 && inferred)
        {
            warnings.Add($"Theme CSS contract: feature '{feature}' declares requiredCssSelectors but does not declare cssHrefs; using best-effort href inference from asset registry. Add featureContracts.{feature}.cssHrefs for deterministic checks.");
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

        // On Windows, "/css/app.css" is considered a rooted file path, but in this context it's a web-root href.
        // Only treat it as a disk-rooted path if it is not a web-root href.
        var isWebRootHref = value.StartsWith("/", StringComparison.Ordinal);

        if (!isWebRootHref && Path.IsPathRooted(value))
            return File.Exists(value) ? Path.GetFullPath(value) : null;

        if (!isWebRootHref)
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
        // In verify/audit flows, plan.RootPath may be either:
        // - the repo/site root (where "static/" lives), or
        // - the built output root (where "/css/app.css" lives).
        // Try output-root first, then static/.
        var outputCandidate = Path.Combine(plan.RootPath, value.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(outputCandidate))
            return Path.GetFullPath(outputCandidate);

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

}
