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
        var map = tokens ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

        foreach (var kv in map)
        {
            if (string.IsNullOrWhiteSpace(kv.Key)) continue;
            text = text.Replace("{{" + kv.Key + "}}", kv.Value ?? string.Empty);
        }

        var unresolved = TokenRegex.Matches(text)
            .Cast<Match>()
            .Select(m => m.Groups.Count > 1 ? m.Groups[1].Value : string.Empty)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unresolved.Length > 0)
        {
            throw new InvalidOperationException(
                $"Template '{name}' contains unresolved token(s): {string.Join(", ", unresolved)}.");
        }

        return text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
    }
}
