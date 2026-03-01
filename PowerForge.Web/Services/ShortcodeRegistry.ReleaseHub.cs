namespace PowerForge.Web;

internal static partial class ShortcodeDefaults
{
    private const string DefaultReleasePlacementsPath = "release_placements";

    internal static string RenderReleaseButton(ShortcodeRenderContext context, Dictionary<string, string> attrs)
    {
        var product = ReadAttr(attrs, "product", "id");
        var channel = ReadAttr(attrs, "channel");
        var platform = ReadAttr(attrs, "platform", "os");
        var arch = ReadAttr(attrs, "arch");
        var kind = ReadAttr(attrs, "kind", "type");
        var label = ReadAttr(attrs, "label", "text", "title");
        var cssClass = ReadAttr(attrs, "class", "cssClass", "css_class");
        var dataPath = ReadAttr(attrs, "data", "from");
        var placement = ResolveReleasePlacement(context, attrs);
        if (placement is not null)
        {
            product = FirstNonEmpty(product, ReadMapString(placement, "product", "id"));
            channel = FirstNonEmpty(channel, ReadMapString(placement, "channel"));
            platform = FirstNonEmpty(platform, ReadMapString(placement, "platform", "os"));
            arch = FirstNonEmpty(arch, ReadMapString(placement, "arch"));
            kind = FirstNonEmpty(kind, ReadMapString(placement, "kind", "type"));
            label = FirstNonEmpty(label, ReadMapString(placement, "label", "text", "title"));
            cssClass = FirstNonEmpty(cssClass, ReadMapString(placement, "class", "cssClass", "css_class"));
            dataPath = FirstNonEmpty(dataPath, ReadMapString(placement, "data", "from", "dataPath", "data_path"));
        }

        return ReleaseHubRenderer.RenderReleaseButton(
            context.Data,
            context.Site.Markdown,
            product,
            channel,
            platform,
            arch,
            kind,
            label,
            cssClass,
            dataPath);
    }

    internal static string RenderReleaseButtons(ShortcodeRenderContext context, Dictionary<string, string> attrs)
    {
        var product = ReadAttr(attrs, "product", "id");
        var channel = ReadAttr(attrs, "channel");
        var limitText = ReadAttr(attrs, "limit", "max");
        var limit = ReadIntAttr(attrs, "limit", "max") ?? 0;
        var groupBy = ReadAttr(attrs, "groupBy", "group_by", "group");
        var platform = ReadAttr(attrs, "platform", "os");
        var arch = ReadAttr(attrs, "arch");
        var kind = ReadAttr(attrs, "kind", "type");
        var cssClass = ReadAttr(attrs, "class", "cssClass", "css_class");
        var dataPath = ReadAttr(attrs, "data", "from");
        var placement = ResolveReleasePlacement(context, attrs);
        if (placement is not null)
        {
            product = FirstNonEmpty(product, ReadMapString(placement, "product", "id"));
            channel = FirstNonEmpty(channel, ReadMapString(placement, "channel"));
            if (string.IsNullOrWhiteSpace(limitText))
                limit = ReadMapInt(placement, "limit", "max") ?? limit;
            groupBy = FirstNonEmpty(groupBy, ReadMapString(placement, "groupBy", "group_by", "group"));
            platform = FirstNonEmpty(platform, ReadMapString(placement, "platform", "os"));
            arch = FirstNonEmpty(arch, ReadMapString(placement, "arch"));
            kind = FirstNonEmpty(kind, ReadMapString(placement, "kind", "type"));
            cssClass = FirstNonEmpty(cssClass, ReadMapString(placement, "class", "cssClass", "css_class"));
            dataPath = FirstNonEmpty(dataPath, ReadMapString(placement, "data", "from", "dataPath", "data_path"));
        }

        return ReleaseHubRenderer.RenderReleaseButtons(
            context.Data,
            context.Site.Markdown,
            product,
            channel,
            limit,
            groupBy,
            platform,
            arch,
            kind,
            cssClass,
            dataPath);
    }

    internal static string RenderReleaseChangelog(ShortcodeRenderContext context, Dictionary<string, string> attrs)
    {
        var product = ReadAttr(attrs, "product", "id");
        var limitText = ReadAttr(attrs, "limit", "max");
        var limit = ReadIntAttr(attrs, "limit", "max") ?? 20;
        var includePreviewText = ReadAttr(attrs, "includePreview", "include_preview", "includePrerelease", "include_prerelease");
        var includePreview = ReadBoolAttr(attrs, defaultValue: true, "includePreview", "include_preview", "includePrerelease", "include_prerelease");
        var cssClass = ReadAttr(attrs, "class", "cssClass", "css_class");
        var dataPath = ReadAttr(attrs, "data", "from");
        var placement = ResolveReleasePlacement(context, attrs);
        if (placement is not null)
        {
            product = FirstNonEmpty(product, ReadMapString(placement, "product", "id"));
            if (string.IsNullOrWhiteSpace(limitText))
                limit = ReadMapInt(placement, "limit", "max") ?? limit;
            if (string.IsNullOrWhiteSpace(includePreviewText))
                includePreview = ReadMapBool(placement, "includePreview", "include_preview", "includePrerelease", "include_prerelease") ?? includePreview;
            cssClass = FirstNonEmpty(cssClass, ReadMapString(placement, "class", "cssClass", "css_class"));
            dataPath = FirstNonEmpty(dataPath, ReadMapString(placement, "data", "from", "dataPath", "data_path"));
        }

        return ReleaseHubRenderer.RenderReleaseChangelog(
            context.Data,
            context.Site.Markdown,
            product,
            limit,
            includePreview,
            cssClass,
            dataPath);
    }

    private static IReadOnlyDictionary<string, object?>? ResolveReleasePlacement(
        ShortcodeRenderContext context,
        Dictionary<string, string> attrs)
    {
        var placementPath = ReadAttr(attrs, "placement", "slot", "preset");
        if (string.IsNullOrWhiteSpace(placementPath))
            return null;

        var placementDataPath = ReadAttr(attrs, "placementsData", "placements_data", "placementsPath", "placements_path");
        var rootPath = string.IsNullOrWhiteSpace(placementDataPath)
            ? DefaultReleasePlacementsPath
            : placementDataPath.Trim();
        var keyPath = placementPath.Trim();

        object? resolved = ResolveDataPath(context.Data, keyPath);
        if (!TryReadMap(resolved, out var map))
        {
            resolved = ResolveDataPath(context.Data, $"{rootPath}.{keyPath}");
            if (!TryReadMap(resolved, out map))
            {
                resolved = ResolveDataPath(context.Data, $"release-placements.{keyPath}");
                if (!TryReadMap(resolved, out map))
                {
                    resolved = ResolveDataPath(context.Data, $"releasePlacements.{keyPath}");
                    if (!TryReadMap(resolved, out map))
                        return null;
                }
            }
        }

        return map;
    }

    private static string? ReadMapString(IReadOnlyDictionary<string, object?> map, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!map.TryGetValue(key, out var value) || value is null)
                continue;

            var text = value.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return null;
    }

    private static int? ReadMapInt(IReadOnlyDictionary<string, object?> map, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!map.TryGetValue(key, out var value) || value is null)
                continue;
            if (value is int intValue)
                return intValue;
            if (value is long longValue && longValue is <= int.MaxValue and >= int.MinValue)
                return (int)longValue;
            if (int.TryParse(value.ToString(), out var parsed))
                return parsed;
        }

        return null;
    }

    private static bool? ReadMapBool(IReadOnlyDictionary<string, object?> map, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!map.TryGetValue(key, out var value) || value is null)
                continue;
            if (value is bool boolValue)
                return boolValue;
            if (bool.TryParse(value.ToString(), out var parsed))
                return parsed;
            if (int.TryParse(value.ToString(), out var intValue))
                return intValue != 0;
        }

        return null;
    }

    private static object? ResolveDataPath(IReadOnlyDictionary<string, object?> data, string path)
    {
        if (data is null || string.IsNullOrWhiteSpace(path))
            return null;

        object? current = data;
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in parts)
        {
            if (!TryReadMap(current, out var map))
                return null;

            var key = raw.Trim();
            if (!map.TryGetValue(key, out current))
                return null;
        }

        return current;
    }

    private static bool TryReadMap(object? value, out IReadOnlyDictionary<string, object?> map)
    {
        map = null!;
        if (value is IReadOnlyDictionary<string, object?> ro)
        {
            map = ro;
            return true;
        }

        if (value is Dictionary<string, object?> dictionary)
        {
            map = dictionary;
            return true;
        }

        return false;
    }

    private static string? FirstNonEmpty(string? preferred, string? fallback)
        => string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;
}
