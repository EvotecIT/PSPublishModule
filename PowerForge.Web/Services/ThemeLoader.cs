using System.Text.Json;

namespace PowerForge.Web;

/// <summary>Loads theme manifests and resolves theme assets.</summary>
public sealed class ThemeLoader
{
    /// <summary>Loads a theme manifest and resolves inheritance.</summary>
    /// <param name="themeRoot">Root directory of the theme.</param>
    /// <param name="themesRoot">Optional parent directory for theme inheritance.</param>
    /// <returns>The resolved theme manifest, or null if not found.</returns>
    public ThemeManifest? Load(string themeRoot, string? themesRoot = null)
    {
        var chain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return LoadInternal(themeRoot, themesRoot, chain);
    }

    /// <summary>Resolves a layout path for the specified theme.</summary>
    /// <param name="themeRoot">Root directory of the theme.</param>
    /// <param name="manifest">Theme manifest.</param>
    /// <param name="layoutName">Layout name or path.</param>
    /// <returns>Resolved layout path, or null when not found.</returns>
    public string? ResolveLayoutPath(string themeRoot, ThemeManifest? manifest, string layoutName)
    {
        if (string.IsNullOrWhiteSpace(themeRoot)) return null;
        if (string.IsNullOrWhiteSpace(layoutName)) return null;

        if (manifest?.Layouts is not null && TryResolveMappedPath(themeRoot, manifest.Layouts, layoutName, out var mapped))
            return mapped;

        var layoutsDir = manifest?.LayoutsPath ?? "layouts";
        var fileName = layoutName.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            ? layoutName
            : layoutName + ".html";
        var layoutPath = Path.Combine(themeRoot, layoutsDir, fileName);
        if (File.Exists(layoutPath))
            return layoutPath;

        if (manifest?.Base is not null && !string.IsNullOrWhiteSpace(manifest.BaseRoot))
            return ResolveLayoutPath(manifest.BaseRoot, manifest.Base, layoutName);

        return null;
    }

    /// <summary>Resolves a partial path for the specified theme.</summary>
    /// <param name="themeRoot">Root directory of the theme.</param>
    /// <param name="manifest">Theme manifest.</param>
    /// <param name="partialName">Partial name or path.</param>
    /// <returns>Resolved partial path, or null when not found.</returns>
    public string? ResolvePartialPath(string themeRoot, ThemeManifest? manifest, string partialName)
    {
        if (string.IsNullOrWhiteSpace(themeRoot)) return null;
        if (string.IsNullOrWhiteSpace(partialName)) return null;

        if (manifest?.Partials is not null && TryResolveMappedPath(themeRoot, manifest.Partials, partialName, out var mapped))
            return mapped;

        var partialsDir = manifest?.PartialsPath ?? "partials";
        var fileName = partialName.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            ? partialName
            : partialName + ".html";
        var partialPath = Path.Combine(themeRoot, partialsDir, fileName);
        if (File.Exists(partialPath))
            return partialPath;

        if (manifest?.Base is not null && !string.IsNullOrWhiteSpace(manifest.BaseRoot))
            return ResolvePartialPath(manifest.BaseRoot, manifest.Base, partialName);

        return null;
    }

    private static ThemeManifest? LoadInternal(string themeRoot, string? themesRoot, HashSet<string> chain)
    {
        if (string.IsNullOrWhiteSpace(themeRoot)) return null;
        var fullRoot = Path.GetFullPath(themeRoot);
        if (!Directory.Exists(fullRoot)) return null;

        if (!chain.Add(fullRoot))
            throw new InvalidOperationException($"Theme inheritance loop detected at {fullRoot}");

        var manifest = LoadRaw(fullRoot);
        if (manifest is null) return null;

        if (!string.IsNullOrWhiteSpace(manifest.Extends))
        {
            var baseRootParent = !string.IsNullOrWhiteSpace(themesRoot)
                ? Path.GetFullPath(themesRoot)
                : Path.GetDirectoryName(fullRoot);
            if (!string.IsNullOrWhiteSpace(baseRootParent))
            {
                var baseRoot = Path.Combine(baseRootParent, manifest.Extends);
                var baseManifest = LoadInternal(baseRoot, baseRootParent, chain);
                if (baseManifest is not null)
                {
                    var merged = Merge(baseManifest, manifest);
                    merged.Base = baseManifest;
                    merged.BaseRoot = baseRoot;
                    chain.Remove(fullRoot);
                    return merged;
                }
            }
        }

        chain.Remove(fullRoot);
        return manifest;
    }

    private static ThemeManifest? LoadRaw(string themeRoot)
    {
        if (!Directory.Exists(themeRoot))
            return null;

        var manifestPath = ResolveThemeManifestPath(themeRoot);
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            return new ThemeManifest { Name = Path.GetFileName(themeRoot) };

        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<ThemeManifest>(json, WebJson.Options);
        if (manifest is null)
            return new ThemeManifest { Name = Path.GetFileName(themeRoot) };

        if (string.IsNullOrWhiteSpace(manifest.Name))
            manifest.Name = Path.GetFileName(themeRoot);

        if (manifest.Tokens is not null)
            manifest.Tokens = MergeTokens(null, manifest.Tokens);

        return manifest;
    }

    private static ThemeManifest Merge(ThemeManifest parent, ThemeManifest child)
    {
        var merged = new ThemeManifest
        {
            Name = string.IsNullOrWhiteSpace(child.Name) ? parent.Name : child.Name,
            ContractVersion = child.ContractVersion ?? parent.ContractVersion,
            Version = child.Version ?? parent.Version,
            Author = child.Author ?? parent.Author,
            Engine = child.Engine ?? parent.Engine,
            Extends = child.Extends ?? parent.Extends,
            DefaultLayout = child.DefaultLayout ?? parent.DefaultLayout,
            LayoutsPath = child.LayoutsPath ?? parent.LayoutsPath,
            PartialsPath = child.PartialsPath ?? parent.PartialsPath,
            AssetsPath = child.AssetsPath ?? parent.AssetsPath,
            ScriptsPath = child.ScriptsPath ?? parent.ScriptsPath,
            Layouts = MergeDictionary(parent.Layouts, child.Layouts),
            Partials = MergeDictionary(parent.Partials, child.Partials),
            Slots = MergeDictionary(parent.Slots, child.Slots),
            Tokens = MergeTokens(parent.Tokens, child.Tokens),
            Assets = child.Assets ?? parent.Assets
        };

        return merged;
    }

    private static Dictionary<string, string>? MergeDictionary(
        Dictionary<string, string>? parent,
        Dictionary<string, string>? child)
    {
        if (parent is null && child is null) return null;
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (parent is not null)
        {
            foreach (var kvp in parent)
                merged[kvp.Key] = kvp.Value;
        }
        if (child is not null)
        {
            foreach (var kvp in child)
                merged[kvp.Key] = kvp.Value;
        }
        return merged;
    }

    private static Dictionary<string, object?>? MergeTokens(
        Dictionary<string, object?>? parent,
        Dictionary<string, object?>? child)
    {
        if (parent is null && child is null) return null;
        var merged = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (parent is not null)
        {
            foreach (var kvp in parent)
                merged[kvp.Key] = NormalizeTokenValue(kvp.Value);
        }
        if (child is not null)
        {
            foreach (var kvp in child)
            {
                var value = NormalizeTokenValue(kvp.Value);
                if (merged.TryGetValue(kvp.Key, out var existing) &&
                    existing is Dictionary<string, object?> existingMap &&
                    value is Dictionary<string, object?> childMap)
                {
                    merged[kvp.Key] = MergeTokens(existingMap, childMap);
                }
                else
                {
                    merged[kvp.Key] = value;
                }
            }
        }
        return merged;
    }


    private static bool TryResolveMappedPath(string themeRoot, Dictionary<string, string> map, string key, out string? path)
    {
        path = null;
        if (!map.TryGetValue(key, out var mapped) || string.IsNullOrWhiteSpace(mapped))
            return false;

        var candidate = mapped.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            ? mapped
            : mapped + ".html";

        var fullPath = Path.IsPathRooted(candidate)
            ? candidate
            : Path.Combine(themeRoot, candidate);
        if (!File.Exists(fullPath))
            return false;
        path = fullPath;
        return true;
    }

    private static object? NormalizeTokenValue(object? value)
    {
        if (value is JsonElement element)
            return ConvertJsonElement(element);
        return value;
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in element.EnumerateObject())
                    map[prop.Name] = ConvertJsonElement(prop.Value);
                return map;
            }
            case JsonValueKind.Array:
            {
                var list = new List<object?>();
                foreach (var item in element.EnumerateArray())
                    list.Add(ConvertJsonElement(item));
                return list;
            }
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var i)) return i;
                if (element.TryGetDouble(out var d)) return d;
                return element.ToString();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            default:
                return null;
        }
    }

    private static string? ResolveThemeManifestPath(string themeRoot)
    {
        if (string.IsNullOrWhiteSpace(themeRoot))
            return null;

        var manifestPath = Path.Combine(themeRoot, "theme.manifest.json");
        if (File.Exists(manifestPath))
            return manifestPath;

        var legacyPath = Path.Combine(themeRoot, "theme.json");
        return File.Exists(legacyPath) ? legacyPath : null;
    }
}
