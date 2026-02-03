using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

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
                {
                    lists[currentListKey].Add(Unquote(item));
                }
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

    private static void ApplyLists(FrontMatter matter, Dictionary<string, List<string>> lists)
    {
        foreach (var kv in lists)
        {
            switch (kv.Key.ToLowerInvariant())
            {
                case "tags":
                    matter.Tags = kv.Value.ToArray();
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
            case "date":
                if (DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
                    matter.Date = dt;
                break;
            case "slug":
                matter.Slug = v;
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
