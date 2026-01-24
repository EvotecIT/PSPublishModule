using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

internal static class ShortcodeProcessor
{
    private static readonly Regex ShortcodeRegex = new Regex(@"\{\{<\s*(?<name>[\w\-]+)\s*(?<attrs>[^>]*)>\}\}", RegexOptions.Compiled);
    private static readonly Regex AttrRegex = new Regex("(?<key>[A-Za-z0-9_-]+)\\s*=\\s*\"(?<value>[^\"]*)\"", RegexOptions.Compiled);

    public static string Apply(string markdown, IReadOnlyDictionary<string, object?> data)
    {
        if (string.IsNullOrEmpty(markdown))
            return markdown;

        return ShortcodeRegex.Replace(markdown, match => Render(match, data));
    }

    private static string Render(Match match, IReadOnlyDictionary<string, object?> data)
    {
        var name = match.Groups["name"].Value.Trim().ToLowerInvariant();
        var attrs = ParseAttrs(match.Groups["attrs"].Value);
        return name switch
        {
            "cards" => RenderCards(data, attrs),
            "metrics" => RenderMetrics(data, attrs),
            "showcase" => RenderShowcase(data, attrs),
            _ => match.Value
        };
    }

    private static Dictionary<string, string> ParseAttrs(string raw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw))
            return result;

        foreach (Match match in AttrRegex.Matches(raw))
        {
            var key = match.Groups["key"].Value;
            var value = match.Groups["value"].Value;
            if (!string.IsNullOrWhiteSpace(key))
                result[key] = value;
        }

        return result;
    }

    private static string RenderCards(IReadOnlyDictionary<string, object?> data, Dictionary<string, string> attrs)
    {
        var list = ResolveList(data, attrs);
        if (list is null) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"pf-grid\">");
        foreach (var item in list)
        {
            if (item is not IReadOnlyDictionary<string, object?> map)
                continue;

            var title = Html(map, "title");
            var text = Html(map, "text");
            var tag = Html(map, "tag");

            sb.AppendLine("  <div class=\"pf-card\">");
            if (!string.IsNullOrWhiteSpace(tag))
                sb.AppendLine($"    <div class=\"pf-card-tag\">{tag}</div>");
            if (!string.IsNullOrWhiteSpace(title))
                sb.AppendLine($"    <h3>{title}</h3>");
            if (!string.IsNullOrWhiteSpace(text))
                sb.AppendLine($"    <p>{text}</p>");
            sb.AppendLine("  </div>");
        }
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    private static string RenderMetrics(IReadOnlyDictionary<string, object?> data, Dictionary<string, string> attrs)
    {
        var list = ResolveList(data, attrs);
        if (list is null) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"pf-metrics\">");
        foreach (var item in list)
        {
            if (item is not IReadOnlyDictionary<string, object?> map)
                continue;

            var value = Html(map, "value");
            var label = Html(map, "label");

            sb.AppendLine("  <div class=\"pf-metric\">");
            if (!string.IsNullOrWhiteSpace(value))
                sb.AppendLine($"    <strong>{value}</strong>");
            if (!string.IsNullOrWhiteSpace(label))
                sb.AppendLine($"    <span>{label}</span>");
            sb.AppendLine("  </div>");
        }
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    private static string RenderShowcase(IReadOnlyDictionary<string, object?> data, Dictionary<string, string> attrs)
    {
        var list = ResolveList(data, attrs);
        if (list is null) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"pf-showcase\">");
        foreach (var item in list)
        {
            if (item is not IReadOnlyDictionary<string, object?> map)
                continue;

            var title = Html(map, "title");
            var summary = Html(map, "summary");
            var link = Html(map, "link");

            sb.AppendLine($"  <a class=\"pf-showcase-card\" href=\"{link}\">");
            if (!string.IsNullOrWhiteSpace(title))
                sb.AppendLine($"    <h3>{title}</h3>");
            if (!string.IsNullOrWhiteSpace(summary))
                sb.AppendLine($"    <p>{summary}</p>");

            if (map.TryGetValue("chips", out var chipsObj) && chipsObj is IEnumerable<object?> chips)
            {
                sb.AppendLine("    <div class=\"pf-showcase-chips\">");
                foreach (var chip in chips)
                {
                    var label = HtmlValue(chip);
                    if (string.IsNullOrWhiteSpace(label)) continue;
                    sb.AppendLine($"      <span class=\"pf-chip\">{label}</span>");
                }
                sb.AppendLine("    </div>");
            }

            sb.AppendLine("  </a>");
        }
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    private static IEnumerable<object?>? ResolveList(IReadOnlyDictionary<string, object?> data, Dictionary<string, string> attrs)
    {
        var key = attrs.TryGetValue("data", out var value) ? value : null;
        if (string.IsNullOrWhiteSpace(key) && attrs.TryGetValue("from", out var from))
            key = from;
        if (string.IsNullOrWhiteSpace(key))
            return null;

        var resolved = ResolveDataPath(data, key);
        return resolved as IEnumerable<object?>;
    }

    private static object? ResolveDataPath(IReadOnlyDictionary<string, object?> data, string path)
    {
        object? current = data;
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current is IReadOnlyDictionary<string, object?> map)
            {
                if (!map.TryGetValue(part, out current))
                    return null;
                continue;
            }
            return null;
        }

        return current;
    }

    private static string Html(IReadOnlyDictionary<string, object?> map, string key)
    {
        return map.TryGetValue(key, out var value) ? HtmlValue(value) : string.Empty;
    }

    private static string HtmlValue(object? value)
    {
        if (value is null) return string.Empty;
        var raw = value.ToString() ?? string.Empty;
        return System.Web.HttpUtility.HtmlEncode(raw);
    }
}
