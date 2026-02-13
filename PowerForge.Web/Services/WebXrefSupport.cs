using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

internal sealed class XrefEntry
{
    public string Id { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string? Title { get; init; }
    public string Source { get; init; } = string.Empty;
}

internal static class WebXrefSupport
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex HtmlXrefHrefRegex = new(
        "href\\s*=\\s*(?<q>['\"])(?<value>xref:[^'\"]+)\\k<q>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex MarkdownFenceRegex = new(
        "```[\\s\\S]*?```",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex MarkdownXrefLinkRegex = new(
        "\\[[^\\]]*\\]\\(\\s*(?:<\\s*)?xref:(?<value>[^)\\s>\"']+)(?:\\s*>)?(?:\\s+\"[^\"]*\"|\\s+'[^']*'|\\s+\\([^\\)]*\\))?\\s*\\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex MarkdownXrefAutoLinkRegex = new(
        "<xref:(?<value>[^>\\s]+)>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex MarkdownHtmlXrefLinkRegex = new(
        "href\\s*=\\s*['\"]xref:(?<value>[^'\"]+)['\"]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly StringComparison FileSystemPathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    private static readonly string[] MetaXrefKeys =
    {
        "xref",
        "xrefs",
        "xref_id",
        "xref_ids",
        "uid",
        "uids"
    };

    internal static string NormalizeId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        if (trimmed.StartsWith("xref:", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed.Substring("xref:".Length);

        return trimmed.Trim();
    }

    internal static string[] ExtractIdsFromMeta(Dictionary<string, object?>? meta)
    {
        if (meta is null || meta.Count == 0)
            return Array.Empty<string>();

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in MetaXrefKeys)
        {
            if (!TryGetMetaValue(meta, key, out var value) || value is null)
                continue;

            foreach (var candidate in ExpandMetaValue(value))
            {
                var normalized = NormalizeId(candidate);
                if (!string.IsNullOrWhiteSpace(normalized))
                    ids.Add(normalized);
            }
        }

        return ids.ToArray();
    }

    internal static string[] ExtractMarkdownReferenceIds(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return Array.Empty<string>();

        var scrubbed = MarkdownFenceRegex.Replace(markdown, string.Empty);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectMarkdownReferences(scrubbed, MarkdownXrefLinkRegex, ids);
        CollectMarkdownReferences(scrubbed, MarkdownXrefAutoLinkRegex, ids);
        CollectMarkdownReferences(scrubbed, MarkdownHtmlXrefLinkRegex, ids);
        return ids.ToArray();
    }

    internal static string ResolveHtmlXrefLinks(
        string html,
        IReadOnlyDictionary<string, string> xrefMap,
        Action<string>? unresolvedSink = null)
    {
        if (string.IsNullOrWhiteSpace(html) ||
            xrefMap is null ||
            xrefMap.Count == 0 ||
            html.IndexOf("xref:", StringComparison.OrdinalIgnoreCase) < 0)
            return html;

        return HtmlXrefHrefRegex.Replace(
            html,
            match =>
            {
                var raw = match.Groups["value"].Value;
                if (!TrySplitIdAndSuffix(raw, out var id, out var suffix))
                    return match.Value;

                var normalized = NormalizeId(id);
                if (string.IsNullOrWhiteSpace(normalized))
                    return match.Value;

                if (!xrefMap.TryGetValue(normalized, out var target))
                {
                    unresolvedSink?.Invoke(normalized);
                    return match.Value;
                }

                var quote = match.Groups["q"].Value;
                var resolved = AppendSuffix(target, suffix);
                var encoded = System.Web.HttpUtility.HtmlEncode(resolved);
                return $"href={quote}{encoded}{quote}";
            });
    }

    internal static IReadOnlyList<XrefEntry> LoadExternalEntries(SiteSpec spec, string rootPath, Action<string>? warningSink = null)
    {
        var mapFiles = spec.Xref?.MapFiles;
        if (mapFiles is null || mapFiles.Length == 0)
            return Array.Empty<XrefEntry>();

        var list = new List<XrefEntry>();
        foreach (var mapFile in mapFiles)
        {
            if (string.IsNullOrWhiteSpace(mapFile))
                continue;

            var fullPath = ResolveMapPath(rootPath, mapFile);
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                warningSink?.Invoke($"Xref: map path '{mapFile}' resolves outside site root and was skipped.");
                continue;
            }

            if (!File.Exists(fullPath))
            {
                warningSink?.Invoke($"Xref: map file not found '{mapFile}'.");
                continue;
            }

            try
            {
                var json = File.ReadAllText(fullPath);
                using var doc = JsonDocument.Parse(json);
                list.AddRange(ParseEntries(doc.RootElement, fullPath));
            }
            catch (Exception ex)
            {
                warningSink?.Invoke($"Xref: failed to parse map file '{mapFile}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        return list;
    }

    private static void CollectMarkdownReferences(string content, Regex regex, HashSet<string> ids)
    {
        foreach (Match match in regex.Matches(content))
        {
            var raw = match.Groups["value"].Value;
            if (!TrySplitIdAndSuffix(raw, out var id, out _))
                continue;

            var normalized = NormalizeId(id);
            if (!string.IsNullOrWhiteSpace(normalized))
                ids.Add(normalized);
        }
    }

    private static string AppendSuffix(string target, string suffix)
    {
        if (string.IsNullOrWhiteSpace(suffix))
            return target;
        return (target ?? string.Empty) + suffix;
    }

    private static bool TrySplitIdAndSuffix(string raw, out string id, out string suffix)
    {
        id = string.Empty;
        suffix = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var value = raw.Trim();
        if (value.StartsWith("xref:", StringComparison.OrdinalIgnoreCase))
            value = value.Substring("xref:".Length);

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var hash = value.IndexOf('#');
        var query = value.IndexOf('?');
        var split = -1;
        if (hash >= 0 && query >= 0)
            split = Math.Min(hash, query);
        else if (hash >= 0)
            split = hash;
        else if (query >= 0)
            split = query;

        if (split >= 0)
        {
            id = value.Substring(0, split).Trim();
            suffix = value.Substring(split);
        }
        else
        {
            id = value.Trim();
        }

        return !string.IsNullOrWhiteSpace(id);
    }

    private static IEnumerable<XrefEntry> ParseEntries(JsonElement root, string sourcePath)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                foreach (var entry in ParseEntryObject(item, sourcePath, fallbackId: null))
                    yield return entry;
            }
            yield break;
        }

        if (root.ValueKind != JsonValueKind.Object)
            yield break;

        if (TryGetArray(root, "references", out var references) ||
            TryGetArray(root, "refs", out references) ||
            TryGetArray(root, "entries", out references))
        {
            foreach (var item in references.EnumerateArray())
            {
                foreach (var entry in ParseEntryObject(item, sourcePath, fallbackId: null))
                    yield return entry;
            }
            yield break;
        }

        foreach (var property in root.EnumerateObject())
        {
            var key = NormalizeId(property.Name);
            if (string.IsNullOrWhiteSpace(key))
                continue;

            if (property.Value.ValueKind == JsonValueKind.String)
            {
                var url = NormalizeUrl(property.Value.GetString());
                if (!string.IsNullOrWhiteSpace(url))
                {
                    yield return new XrefEntry
                    {
                        Id = key,
                        Url = url,
                        Source = sourcePath
                    };
                }
                continue;
            }

            foreach (var entry in ParseEntryObject(property.Value, sourcePath, key))
                yield return entry;
        }
    }

    private static IEnumerable<XrefEntry> ParseEntryObject(JsonElement element, string sourcePath, string? fallbackId)
    {
        if (element.ValueKind != JsonValueKind.Object)
            yield break;

        var id = ReadString(element, "id") ??
                 ReadString(element, "uid") ??
                 ReadString(element, "xref") ??
                 fallbackId;
        var url = ReadString(element, "url") ?? ReadString(element, "href");
        var title = ReadString(element, "title") ?? ReadString(element, "name");

        var normalizedId = NormalizeId(id);
        var normalizedUrl = NormalizeUrl(url);
        if (!string.IsNullOrWhiteSpace(normalizedId) && !string.IsNullOrWhiteSpace(normalizedUrl))
        {
            yield return new XrefEntry
            {
                Id = normalizedId,
                Url = normalizedUrl,
                Title = title,
                Source = sourcePath
            };
        }

        foreach (var alias in ReadStringArray(element, "aliases"))
        {
            var normalizedAlias = NormalizeId(alias);
            if (string.IsNullOrWhiteSpace(normalizedAlias) || string.IsNullOrWhiteSpace(normalizedUrl))
                continue;
            yield return new XrefEntry
            {
                Id = normalizedAlias,
                Url = normalizedUrl,
                Title = title,
                Source = sourcePath
            };
        }
    }

    private static string? ResolveMapPath(string rootPath, string mapFile)
    {
        var combined = Path.IsPathRooted(mapFile)
            ? mapFile
            : Path.Combine(rootPath, mapFile);
        var full = Path.GetFullPath(combined);
        var normalizedRoot = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (full.Equals(normalizedRoot, FileSystemPathComparison))
            return full;
        return full.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, FileSystemPathComparison)
            ? full
            : null;
    }

    private static bool TryGetArray(JsonElement element, string propertyName, out JsonElement array)
    {
        array = default;
        if (element.ValueKind != JsonValueKind.Object)
            return false;
        foreach (var property in element.EnumerateObject())
        {
            if (!property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (property.Value.ValueKind != JsonValueKind.Array)
                return false;
            array = property.Value;
            return true;
        }
        return false;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var property in element.EnumerateObject())
        {
            if (!property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (property.Value.ValueKind == JsonValueKind.String)
                return property.Value.GetString();
            return property.Value.ToString();
        }
        return null;
    }

    private static IEnumerable<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (!TryGetArray(element, propertyName, out var array))
            return Array.Empty<string>();

        return array.EnumerateArray()
            .Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
    }

    private static IEnumerable<string> ExpandMetaValue(object value)
    {
        if (value is string text)
            return SplitMultiValue(text);

        if (value is IEnumerable<object?> items && value is not string)
        {
            return items
                .Where(item => item is not null)
                .SelectMany(item => SplitMultiValue(item?.ToString() ?? string.Empty))
                .ToArray();
        }

        return SplitMultiValue(value.ToString() ?? string.Empty);
    }

    private static IEnumerable<string> SplitMultiValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();

        return value
            .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();
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
            if (current is IReadOnlyDictionary<string, object?> map)
            {
                if (!map.TryGetValue(part, out current))
                    return false;
                continue;
            }
            return false;
        }

        value = current;
        return true;
    }

    private static string NormalizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        var trimmed = url.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("//", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("#", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        if (trimmed.StartsWith("/"))
            return trimmed;

        return "/" + trimmed.TrimStart('/');
    }
}
