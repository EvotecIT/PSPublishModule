using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PowerForge;

internal static class ScriptTemplateRenderer
{
    private static readonly Regex TokenRegex = new(@"\{\{([A-Za-z0-9_]+)\}\}", RegexOptions.CultureInvariant);

    internal static string Render(
        string templateName,
        string template,
        IReadOnlyDictionary<string, string>? tokens)
    {
        var name = string.IsNullOrWhiteSpace(templateName) ? "template" : templateName.Trim();
        var text = template ?? string.Empty;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in tokens ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(kv.Key)) continue;
            map[kv.Key] = kv.Value ?? string.Empty;
        }

        var referencedTokens = TokenRegex.Matches(text)
            .Cast<Match>()
            .Select(m => m.Groups.Count > 1 ? m.Groups[1].Value : string.Empty)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var missing = referencedTokens
            .Where(token => !map.ContainsKey(token))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Template '{name}' references missing token(s): {string.Join(", ", missing)}.");
        }

        text = TokenRegex.Replace(text, match =>
        {
            var token = match.Groups[1].Value;
            if (!map.TryGetValue(token, out var value))
                throw new InvalidOperationException($"Template '{name}' references missing token(s): {token}.");

            return value;
        });

        return text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
    }
}
