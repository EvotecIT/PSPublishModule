using System.Text.RegularExpressions;

namespace PowerForge.Web;

internal static class ShortcodeProcessor
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex ShortcodeRegex = new Regex(@"\{\{<\s*(?<name>[\w\-]+)\s*(?<attrs>[^>]*)>\}\}", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex AttrRegex = new Regex("(?<key>[A-Za-z0-9_-]+)\\s*=\\s*\"(?<value>[^\"]*)\"", RegexOptions.Compiled, RegexTimeout);

    public static string Apply(string markdown, IReadOnlyDictionary<string, object?> data)
    {
        if (string.IsNullOrEmpty(markdown))
            return markdown;

        var context = ShortcodeRenderContext.FromDataOnly(data);
        return Apply(markdown, context);
    }

    public static string Apply(string markdown, ShortcodeRenderContext context)
    {
        if (string.IsNullOrEmpty(markdown))
            return markdown;
        if (context is null)
            return markdown;

        return ShortcodeRegex.Replace(markdown, match => Render(match, context));
    }

    private static string Render(Match match, ShortcodeRenderContext context)
    {
        var name = match.Groups["name"].Value.Trim().ToLowerInvariant();
        var attrs = ParseAttrs(match.Groups["attrs"].Value);
        var themed = context.TryRenderThemeShortcode(name, attrs);
        if (!string.IsNullOrWhiteSpace(themed))
            return themed;

        if (ShortcodeRegistry.TryGet(name, out var handler))
            return handler(context, attrs);

        return match.Value;
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

    internal static IEnumerable<object?>? ResolveList(IReadOnlyDictionary<string, object?> data, Dictionary<string, string> attrs)
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

    internal static string Html(IReadOnlyDictionary<string, object?> map, string key)
    {
        return map.TryGetValue(key, out var value) ? HtmlValue(value) : string.Empty;
    }

    internal static string HtmlValue(object? value)
    {
        if (value is null) return string.Empty;
        var raw = value.ToString() ?? string.Empty;
        return System.Web.HttpUtility.HtmlEncode(raw);
    }
}
