using System.Collections;

namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    private static string ResolveSocialCardThemeKey(SiteSpec spec, ContentItem item)
    {
        var themeOverride = FirstNonEmpty(
            GetMetaString(item.Meta, "social_card_theme"),
            GetMetaString(item.Meta, "social.theme"));
        if (!string.IsNullOrWhiteSpace(themeOverride))
            return themeOverride!.Trim();

        var collection = item.Collection?.Trim();
        if (!string.IsNullOrWhiteSpace(collection) &&
            TryResolveCollectionCardPreset(spec.Social?.GeneratedCardThemesByCollection, collection!, out var collectionTheme))
            return collectionTheme;

        return spec.Social?.GeneratedCardTheme?.Trim() ?? string.Empty;
    }

    private static SocialCardThemeSpec? ResolveSocialCardTheme(SocialSpec? social, string? themeKey)
    {
        if (social?.GeneratedCardThemes is null ||
            social.GeneratedCardThemes.Count == 0 ||
            string.IsNullOrWhiteSpace(themeKey))
        {
            return null;
        }

        var normalizedThemeKey = themeKey.Trim();
        if (social.GeneratedCardThemes.TryGetValue(normalizedThemeKey, out var direct))
            return direct;

        return null;
    }

    internal static string ComputeThemeTokenFingerprint(IReadOnlyDictionary<string, object?>? themeTokens)
    {
        if (themeTokens is null || themeTokens.Count == 0)
            return string.Empty;

        var canonical = NormalizeThemeTokenValue(themeTokens);
        var serialized = System.Text.Json.JsonSerializer.Serialize(canonical);
        return ComputeSocialHash(serialized);
    }

    internal static IReadOnlyDictionary<string, object?>? MergeSocialCardThemeTokens(
        IReadOnlyDictionary<string, object?>? baseTokens,
        IReadOnlyDictionary<string, object?>? themeTokens)
    {
        if (baseTokens is null || baseTokens.Count == 0)
            return NormalizeThemeTokenMap(themeTokens);
        if (themeTokens is null || themeTokens.Count == 0)
            return NormalizeThemeTokenMap(baseTokens);

        var merged = NormalizeThemeTokenMap(baseTokens);
        var overlay = NormalizeThemeTokenMap(themeTokens);
        if (merged is null || merged.Count == 0)
            return overlay;
        if (overlay is null || overlay.Count == 0)
            return merged;

        MergeThemeTokenMapInto(merged, overlay);
        return merged;
    }

    private static Dictionary<string, object?>? NormalizeThemeTokenMap(IReadOnlyDictionary<string, object?>? tokens)
    {
        if (tokens is null || tokens.Count == 0)
            return null;

        var normalized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in tokens)
            normalized[pair.Key] = NormalizeThemeTokenValue(pair.Value);
        return normalized;
    }

    private static void MergeThemeTokenMapInto(Dictionary<string, object?> target, IReadOnlyDictionary<string, object?> overlay)
    {
        foreach (var pair in overlay)
        {
            if (pair.Value is IReadOnlyDictionary<string, object?> overlayMap &&
                target.TryGetValue(pair.Key, out var targetValue) &&
                targetValue is Dictionary<string, object?> targetMap)
            {
                MergeThemeTokenMapInto(targetMap, overlayMap);
                continue;
            }

            target[pair.Key] = NormalizeThemeTokenValue(pair.Value);
        }
    }

    private static object? NormalizeThemeTokenValue(object? value)
    {
        if (value is null)
            return null;

        if (value is System.Text.Json.JsonElement element)
            return NormalizeThemeTokenValue(ConvertJsonElement(element));

        if (value is IReadOnlyDictionary<string, object?> map)
        {
            var normalized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in map.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                normalized[pair.Key] = NormalizeThemeTokenValue(pair.Value);
            return normalized;
        }

        if (value is IDictionary dictionary)
        {
            var normalized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is null)
                    continue;

                normalized[Convert.ToString(entry.Key) ?? string.Empty] = NormalizeThemeTokenValue(entry.Value);
            }

            return normalized;
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            var list = new List<object?>();
            foreach (var item in enumerable)
                list.Add(NormalizeThemeTokenValue(item));
            return list;
        }

        return value;
    }
}
