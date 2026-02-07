using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge.Web;

/// <summary>Theme contract, layout, and token verification rules.</summary>
public static partial class WebSiteVerifier
{
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