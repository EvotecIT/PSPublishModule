using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace PowerForge.Web;

/// <summary>Parses YAML-like front matter from markdown content.</summary>
public static class FrontMatterParser
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex H1Regex = new Regex(@"^#\s+(.+)$", RegexOptions.Multiline | RegexOptions.Compiled, RegexTimeout);

    /// <summary>Extracts front matter and body from markdown.</summary>
    /// <param name="markdown">Raw markdown content.</param>
    /// <returns>Front matter (if any) and remaining body.</returns>
    public static (FrontMatter? Matter, string Body) Parse(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return (null, string.Empty);

        using var reader = new StringReader(markdown);
        var firstLine = reader.ReadLine();
        if (firstLine is null || firstLine.Trim() != "---")
            return (null, markdown);

        var fmLines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Trim() == "---" || line.Trim() == "...")
                break;
            fmLines.Add(line);
        }

        var body = reader.ReadToEnd() ?? string.Empty;
        var matter = ParseYamlSubset(fmLines);
        return (matter, body.TrimStart('\r', '\n'));
    }

    /// <summary>Extracts the first H1 heading from markdown.</summary>
    /// <param name="markdown">Markdown content.</param>
    /// <returns>Heading text or null if missing.</returns>
    public static string? ExtractTitleFromMarkdown(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return null;
        var match = H1Regex.Match(markdown);
        if (!match.Success) return null;
        return match.Groups[1].Value.Trim();
    }

    private static FrontMatter ParseYamlSubset(IEnumerable<string> lines)
    {
        var yaml = string.Join("\n", lines);
        if (TryParseYamlDocument(yaml, out var root))
            return ParseYamlDocument(root);

        return ParseYamlSubsetLegacy(lines);
    }

    private static FrontMatter ParseYamlSubsetLegacy(IEnumerable<string> lines)
    {
        var matter = new FrontMatter();
        string? currentListKey = null;
        var lists = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                continue;

            if (IsListItem(line))
            {
                if (currentListKey is null) continue;
                var item = line.Trim().TrimStart('-').Trim();
                if (!string.IsNullOrWhiteSpace(item))
                    lists[currentListKey].Add(Unquote(item));
                continue;
            }

            var idx = line.IndexOf(':');
            if (idx < 0)
                continue;

            var key = line.Substring(0, idx).Trim();
            var value = line.Substring(idx + 1).Trim();
            currentListKey = null;

            if (string.IsNullOrEmpty(value))
            {
                currentListKey = key;
                if (!lists.ContainsKey(key))
                    lists[key] = new List<string>();
                continue;
            }

            if (value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal))
            {
                var inner = value.Substring(1, value.Length - 2);
                var items = inner.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => Unquote(v.Trim()))
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .ToList();
                lists[key] = items;
                continue;
            }

            ApplyScalar(matter, key, value);
        }

        ApplyLists(matter, lists);
        return matter;
    }

    private static bool IsListItem(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("-") && trimmed.Length > 1 && char.IsWhiteSpace(trimmed[1]);
    }

    private static bool TryParseYamlDocument(string yaml, out Dictionary<string, object?> root)
    {
        root = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(yaml))
            return true;

        try
        {
            var deserializer = new DeserializerBuilder().Build();
            var parsed = deserializer.Deserialize<object?>(yaml);
            if (parsed is null)
                return true;

            if (TryConvertToStringObjectDictionary(parsed, out var map))
            {
                root = map;
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static FrontMatter ParseYamlDocument(Dictionary<string, object?> root)
    {
        var matter = new FrontMatter();
        if (root.Count == 0)
            return matter;

        foreach (var entry in root)
            AddMetaEntry(matter.Meta, entry.Key, entry.Value);

        if (TryGetMetaString(matter.Meta, "title", out var title))
            matter.Title = title;

        if (TryGetMetaString(matter.Meta, "description", out var description))
            matter.Description = description;

        if (TryGetMetaDate(matter.Meta, "date", out var date))
            matter.Date = date;

        if (TryGetMetaString(matter.Meta, "slug", out var slug))
            matter.Slug = slug;

        if (TryGetMetaInt(matter.Meta, "order", out var order))
            matter.Order = order;

        if (TryGetMetaBool(matter.Meta, "draft", out var draft))
            matter.Draft = draft;

        if (TryGetMetaString(matter.Meta, "collection", out var collection))
            matter.Collection = collection;

        if (TryGetMetaString(matter.Meta, "canonical", out var canonical))
            matter.Canonical = canonical;

        if (TryGetMetaString(matter.Meta, "editpath", out var editPath))
            matter.EditPath = editPath;

        if (TryGetMetaString(matter.Meta, "layout", out var layout))
            matter.Layout = layout;

        if (TryGetMetaString(matter.Meta, "template", out var template))
            matter.Template = template;

        var tags = ReadMetaStringList(matter.Meta, "tags");
        if (tags.Length > 0)
            matter.Tags = tags;

        var categories = ReadMetaStringList(matter.Meta, "categories", "category");
        if (categories.Length > 0)
        {
            matter.Categories = categories;
            SetMetaValue(matter.Meta, "categories", categories);
        }

        var aliases = ReadMetaStringList(matter.Meta, "aliases");
        if (aliases.Length > 0)
            matter.Aliases = aliases;

        if (TryGetMetaString(matter.Meta, "language", out var language) ||
            TryGetMetaString(matter.Meta, "lang", out language) ||
            TryGetMetaString(matter.Meta, "i18n.language", out language) ||
            TryGetMetaString(matter.Meta, "i18n.lang", out language))
        {
            matter.Language = language;
        }

        if (TryGetMetaString(matter.Meta, "translation_key", out var translationKey) ||
            TryGetMetaString(matter.Meta, "translationKey", out translationKey) ||
            TryGetMetaString(matter.Meta, "translation.key", out translationKey) ||
            TryGetMetaString(matter.Meta, "i18n.key", out translationKey) ||
            TryGetMetaString(matter.Meta, "i18n.translation_key", out translationKey) ||
            TryGetMetaString(matter.Meta, "i18n.translationKey", out translationKey) ||
            TryGetMetaString(matter.Meta, "i18n.group", out translationKey))
        {
            matter.TranslationKey = translationKey;
            if (!TryGetMetaString(matter.Meta, "translation_key", out _))
                SetMetaValue(matter.Meta, "translation_key", translationKey);
        }

        return matter;
    }

    private static void AddMetaEntry(Dictionary<string, object?> meta, string key, object? value)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        if (key.Equals("meta", StringComparison.OrdinalIgnoreCase) &&
            value is IReadOnlyDictionary<string, object?> metaMap)
        {
            foreach (var pair in metaMap)
                AddMetaEntry(meta, pair.Key, pair.Value);
            return;
        }

        SetMetaValue(meta, key, value);
    }

    private static bool TryConvertToStringObjectDictionary(object? value, out Dictionary<string, object?> map)
    {
        map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        switch (value)
        {
            case IReadOnlyDictionary<object, object?> roObj:
                foreach (var entry in roObj)
                    map[entry.Key?.ToString() ?? string.Empty] = ConvertYamlValue(entry.Value);
                break;
            case IDictionary<object, object?> obj:
                foreach (var entry in obj)
                    map[entry.Key?.ToString() ?? string.Empty] = ConvertYamlValue(entry.Value);
                break;
            case IReadOnlyDictionary<string, object?> roString:
                foreach (var entry in roString)
                    map[entry.Key] = ConvertYamlValue(entry.Value);
                break;
            case IDictionary<string, object?> stringMap:
                foreach (var entry in stringMap)
                    map[entry.Key] = ConvertYamlValue(entry.Value);
                break;
            default:
                return false;
        }

        var invalidKeys = map.Keys.Where(static key => string.IsNullOrWhiteSpace(key)).ToArray();
        foreach (var invalidKey in invalidKeys)
            map.Remove(invalidKey);

        return true;
    }

    private static object? ConvertYamlValue(object? value)
    {
        if (value is null)
            return null;

        if (TryConvertToStringObjectDictionary(value, out var dict))
            return dict;

        if (value is string text)
            return Unquote(text);

        if (value is IEnumerable<object?> enumerable)
        {
            var items = enumerable.Select(ConvertYamlValue).ToList();
            if (items.All(static item => item is string or null))
            {
                return items
                    .Select(static item => item?.ToString() ?? string.Empty)
                    .Where(static item => !string.IsNullOrWhiteSpace(item))
                    .ToArray();
            }

            return items.ToArray();
        }

        if (value is IEnumerable<object> enumerableNoNull)
        {
            var items = enumerableNoNull.Cast<object?>().Select(ConvertYamlValue).ToList();
            if (items.All(static item => item is string or null))
            {
                return items
                    .Select(static item => item?.ToString() ?? string.Empty)
                    .Where(static item => !string.IsNullOrWhiteSpace(item))
                    .ToArray();
            }

            return items.ToArray();
        }

        return value;
    }

    private static string[] ReadMetaStringList(Dictionary<string, object?> meta, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!TryGetMetaValue(meta, key, out var value) || value is null)
                continue;

            if (TryConvertToStringArray(value, out var values))
            {
                var normalized = values
                    .Where(static item => !string.IsNullOrWhiteSpace(item))
                    .Select(static item => item.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (normalized.Length > 0)
                    return normalized;
            }
        }

        return Array.Empty<string>();
    }

    private static bool TryConvertToStringArray(object value, out string[] items)
    {
        items = Array.Empty<string>();

        if (value is string text)
        {
            if (text.Contains(',', StringComparison.Ordinal))
            {
                items = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return items.Length > 0;
            }

            items = string.IsNullOrWhiteSpace(text) ? Array.Empty<string>() : new[] { text.Trim() };
            return items.Length > 0;
        }

        if (value is IEnumerable<object?> list)
        {
            items = list
                .Select(static entry => entry?.ToString() ?? string.Empty)
                .Where(static entry => !string.IsNullOrWhiteSpace(entry))
                .Select(static entry => entry.Trim())
                .ToArray();
            return items.Length > 0;
        }

        if (value is IEnumerable<object> listNoNull)
        {
            items = listNoNull
                .Select(static entry => entry?.ToString() ?? string.Empty)
                .Where(static entry => !string.IsNullOrWhiteSpace(entry))
                .Select(static entry => entry.Trim())
                .ToArray();
            return items.Length > 0;
        }

        return false;
    }

    private static bool TryGetMetaDate(Dictionary<string, object?> meta, string key, out DateTime value)
    {
        value = default;
        if (!TryGetMetaValue(meta, key, out var raw) || raw is null)
            return false;

        switch (raw)
        {
            case DateTime dt:
                value = dt;
                return true;
            case DateTimeOffset dto:
                value = dto.DateTime;
                return true;
            default:
                var text = raw.ToString();
                if (string.IsNullOrWhiteSpace(text))
                    return false;
                if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                {
                    value = parsed;
                    return true;
                }

                return false;
        }
    }

    private static bool TryGetMetaInt(Dictionary<string, object?> meta, string key, out int value)
    {
        value = default;
        if (!TryGetMetaValue(meta, key, out var raw) || raw is null)
            return false;

        switch (raw)
        {
            case int i:
                value = i;
                return true;
            case long l when l is >= int.MinValue and <= int.MaxValue:
                value = (int)l;
                return true;
            default:
                var text = raw.ToString();
                return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }

    private static bool TryGetMetaBool(Dictionary<string, object?> meta, string key, out bool value)
    {
        value = default;
        if (!TryGetMetaValue(meta, key, out var raw) || raw is null)
            return false;

        switch (raw)
        {
            case bool flag:
                value = flag;
                return true;
            default:
                var text = raw.ToString();
                return bool.TryParse(text, out value);
        }
    }

    private static bool TryGetMetaString(Dictionary<string, object?> meta, string key, out string value)
    {
        value = string.Empty;
        if (!TryGetMetaValue(meta, key, out var raw) || raw is null)
            return false;

        if (raw is IReadOnlyDictionary<string, object?> ||
            raw is IDictionary<string, object?> ||
            raw is IDictionary<string, object> ||
            raw is IReadOnlyDictionary<object, object?> ||
            raw is IDictionary<object, object?> ||
            raw is IDictionary<object, object>)
        {
            return false;
        }

        value = raw.ToString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetMetaValue(Dictionary<string, object?> meta, string key, out object? value)
    {
        value = null;
        if (meta is null || string.IsNullOrWhiteSpace(key))
            return false;

        var parts = key.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        object? current = meta;
        foreach (var part in parts)
        {
            if (current is IReadOnlyDictionary<string, object?> ro)
            {
                if (!ro.TryGetValue(part, out current))
                    return false;
                continue;
            }

            if (current is IDictionary<string, object?> map)
            {
                if (!map.TryGetValue(part, out current))
                    return false;
                continue;
            }

            if (current is IDictionary<string, object> mapNoNull)
            {
                if (!mapNoNull.TryGetValue(part, out var next))
                    return false;
                current = next;
                continue;
            }

            return false;
        }

        value = current;
        return true;
    }

    private static void ApplyLists(FrontMatter matter, Dictionary<string, List<string>> lists)
    {
        foreach (var kv in lists)
        {
            switch (kv.Key.ToLowerInvariant())
            {
                case "tags":
                    matter.Tags = kv.Value.ToArray();
                    break;
                case "categories":
                    matter.Categories = kv.Value.ToArray();
                    SetMetaValue(matter.Meta, "categories", matter.Categories);
                    break;
                case "aliases":
                    matter.Aliases = kv.Value.ToArray();
                    break;
                default:
                    SetMetaValue(matter.Meta, kv.Key, kv.Value.ToArray());
                    break;
            }
        }
    }

    private static void ApplyScalar(FrontMatter matter, string key, string value)
    {
        var v = Unquote(value);
        switch (key.ToLowerInvariant())
        {
            case "title":
                matter.Title = v;
                break;
            case "description":
                matter.Description = v;
                break;
            case "categories":
            case "category":
            {
                var categories = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .ToArray();
                matter.Categories = categories;
                SetMetaValue(matter.Meta, "categories", categories);
                break;
            }
            case "date":
                if (DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
                    matter.Date = dt;
                break;
            case "slug":
                matter.Slug = v;
                break;
            case "language":
            case "lang":
                matter.Language = v;
                SetMetaValue(matter.Meta, key, v);
                break;
            case "translation_key":
            case "translationkey":
            case "translation.key":
            case "i18n.key":
            case "i18n.translation_key":
            case "i18n.translationkey":
            case "i18n.group":
                matter.TranslationKey = v;
                SetMetaValue(matter.Meta, key, v);
                if (!key.Equals("translation_key", StringComparison.OrdinalIgnoreCase))
                    SetMetaValue(matter.Meta, "translation_key", v);
                break;
            case "order":
                if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                    matter.Order = i;
                break;
            case "draft":
                if (bool.TryParse(v, out var b))
                    matter.Draft = b;
                break;
            case "collection":
                matter.Collection = v;
                break;
            case "canonical":
                matter.Canonical = v;
                break;
            case "editpath":
                matter.EditPath = v;
                break;
            case "layout":
                matter.Layout = v;
                break;
            case "template":
                matter.Template = v;
                break;
            default:
                SetMetaValue(matter.Meta, key, v);
                break;
        }
    }

    private static void SetMetaValue(Dictionary<string, object?> meta, string key, object? value)
    {
        if (meta is null) return;
        if (string.IsNullOrWhiteSpace(key)) return;
        var normalized = key.Trim();
        if (normalized.StartsWith("meta.", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring(5);
        if (string.IsNullOrWhiteSpace(normalized)) return;

        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return;

        var current = meta;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var part = parts[i];
            if (!current.TryGetValue(part, out var existing) || existing is not Dictionary<string, object?> child)
            {
                child = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                current[part] = child;
            }
            current = child;
        }

        current[parts[^1]] = value;
    }

    private static string Unquote(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if ((value.StartsWith("\"") && value.EndsWith("\"")) || (value.StartsWith("'") && value.EndsWith("'")))
            return value.Substring(1, value.Length - 2);
        return value;
    }
}
