using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

internal static class DocumentationFallbackEnricher
{
    private static readonly Regex ParameterRegex = new(
        pattern: @"-(?<name>[A-Za-z][A-Za-z0-9_-]*)",
        options: RegexOptions.Compiled);

    public static void Enrich(DocumentationExtractionPayload payload, ILogger logger)
    {
        if (payload is null) throw new ArgumentNullException(nameof(payload));
        if (logger is null) throw new ArgumentNullException(nameof(logger));

        foreach (var cmd in (payload.Commands ?? new List<DocumentationCommandHelp>())
                 .Where(c => c is not null && !string.IsNullOrWhiteSpace(c.Name)))
        {
            try
            {
                EnsureExamples(cmd);
            }
            catch (Exception ex)
            {
                logger.Warn($"Failed to generate fallback examples for '{cmd.Name}'. Error: {ex.Message}");
                if (logger.IsVerbose) logger.Verbose(ex.ToString());
            }
        }
    }

    private static void EnsureExamples(DocumentationCommandHelp cmd)
    {
        var hasExamples = (cmd.Examples ?? new List<DocumentationExampleHelp>())
            .Any(e => e is not null && (!string.IsNullOrWhiteSpace(e.Code) || !string.IsNullOrWhiteSpace(e.Remarks)));
        if (hasExamples) return;

        var examples = new List<DocumentationExampleHelp>();

        var syntaxSets = (cmd.Syntax ?? new List<DocumentationSyntaxHelp>())
            .Where(s => s is not null && !string.IsNullOrWhiteSpace(s.Text))
            .OrderByDescending(s => s.IsDefault)
            .ThenBy(s => s.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (syntaxSets.Length == 0)
        {
            examples.Add(new DocumentationExampleHelp
            {
                Title = "EXAMPLE 1",
                Code = BuildExampleInvocation(cmd, requiredParameterNames: Array.Empty<string>()),
                Remarks = string.Empty
            });

            cmd.Examples = examples;
            return;
        }

        var uniqueCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < syntaxSets.Length && examples.Count < 3; i++)
        {
            var set = syntaxSets[i];
            var requiredParams = ExtractRequiredParameterNamesFromSyntax(set.Text!, cmd.Name)
                .Take(5)
                .ToArray();
            var code = BuildExampleInvocation(cmd, requiredParams);
            if (!uniqueCodes.Add(code)) continue;

            examples.Add(new DocumentationExampleHelp
            {
                Title = $"EXAMPLE {examples.Count + 1}",
                Code = code,
                Remarks = string.Empty
            });
        }

        if (examples.Count == 0)
        {
            examples.Add(new DocumentationExampleHelp
            {
                Title = "EXAMPLE 1",
                Code = BuildExampleInvocation(cmd, requiredParameterNames: Array.Empty<string>()),
                Remarks = string.Empty
            });
        }

        cmd.Examples = examples;
    }

    private static IEnumerable<string> ExtractRequiredParameterNamesFromSyntax(string syntaxText, string commandName)
    {
        if (string.IsNullOrWhiteSpace(syntaxText)) return Array.Empty<string>();

        var text = syntaxText.Trim();
        if (!string.IsNullOrWhiteSpace(commandName) && text.StartsWith(commandName.Trim(), StringComparison.OrdinalIgnoreCase))
            text = text.Substring(commandName.Trim().Length);

        var outside = StripBracketedContent(text);
        if (string.IsNullOrWhiteSpace(outside)) return Array.Empty<string>();

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in ParameterRegex.Matches(outside))
        {
            var n = m.Groups["name"].Value;
            if (string.IsNullOrWhiteSpace(n)) continue;
            names.Add(n.Trim());
        }

        return names.ToArray();
    }

    private static string StripBracketedContent(string text)
    {
        var sb = new StringBuilder(text.Length);
        var depth = 0;
        foreach (var ch in text)
        {
            if (ch == '[') { depth++; continue; }
            if (ch == ']') { if (depth > 0) depth--; continue; }
            if (depth == 0) sb.Append(ch);
        }
        return sb.ToString();
    }

    private static string BuildExampleInvocation(DocumentationCommandHelp cmd, IReadOnlyCollection<string> requiredParameterNames)
    {
        var name = (cmd.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var parameters = (cmd.Parameters ?? new List<DocumentationParameterHelp>())
            .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Name))
            .ToArray();

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = new List<string> { name };

        var picked = requiredParameterNames
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Where(p => used.Add(p))
            .ToArray();

        if (picked.Length == 0)
        {
            // No required params: choose a likely useful one so the example isn't just the command name.
            var candidate =
                parameters.FirstOrDefault(p => p.Name.Equals("Path", StringComparison.OrdinalIgnoreCase)) ??
                parameters.FirstOrDefault(p => p.Name.EndsWith("Path", StringComparison.OrdinalIgnoreCase)) ??
                parameters.FirstOrDefault(p => p.Name.Equals("ModuleName", StringComparison.OrdinalIgnoreCase)) ??
                parameters.FirstOrDefault(p => p.Name.Equals("Name", StringComparison.OrdinalIgnoreCase)) ??
                parameters.FirstOrDefault(p => p.Name.Equals("Enable", StringComparison.OrdinalIgnoreCase)) ??
                parameters.FirstOrDefault();

            if (candidate is not null)
                picked = new[] { candidate.Name.Trim() };
        }

        foreach (var pName in picked)
        {
            var p = parameters.FirstOrDefault(x => x.Name.Equals(pName, StringComparison.OrdinalIgnoreCase));
            var type = (p?.Type ?? string.Empty).Trim();
            parts.Add("-" + pName);

            if (IsSwitchParameter(type)) continue;
            parts.Add(SampleValue(pName, type));
        }

        return string.Join(" ", parts.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private static bool IsSwitchParameter(string typeName)
        => typeName.Equals("SwitchParameter", StringComparison.OrdinalIgnoreCase);

    private static bool IsBoolean(string typeName)
        => typeName.Equals("Boolean", StringComparison.OrdinalIgnoreCase) ||
           typeName.Equals("Bool", StringComparison.OrdinalIgnoreCase);

    private static string SampleValue(string parameterName, string typeName)
    {
        var name = (parameterName ?? string.Empty).Trim();
        var type = (typeName ?? string.Empty).Trim();

        if (IsBoolean(type)) return "$true";

        if (type.EndsWith("[]", StringComparison.Ordinal))
        {
            var inner = type.Substring(0, type.Length - 2);
            var innerValue = SampleValue(name, inner);
            return $"@({innerValue})";
        }

        if (name.EndsWith("Path", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Path", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("LiteralPath", StringComparison.OrdinalIgnoreCase))
            return "'C:\\Path'";

        if (type.Equals("String", StringComparison.OrdinalIgnoreCase))
        {
            if (name.Equals("ModuleName", StringComparison.OrdinalIgnoreCase)) return "'MyModule'";
            if (name.Equals("ProjectName", StringComparison.OrdinalIgnoreCase)) return "'MyProject'";
            if (name.EndsWith("Version", StringComparison.OrdinalIgnoreCase)) return "'1.0.0'";
            if (name.EndsWith("Culture", StringComparison.OrdinalIgnoreCase)) return "'en-US'";
            if (name.EndsWith("Uri", StringComparison.OrdinalIgnoreCase)) return "'https://example.com'";
            if (name.Contains("Thumbprint", StringComparison.OrdinalIgnoreCase)) return "'0123456789ABCDEF'";
            if (name.EndsWith("Name", StringComparison.OrdinalIgnoreCase)) return "'Name'";
            return "'Value'";
        }
        if (type.Equals("Int32", StringComparison.OrdinalIgnoreCase) || type.Equals("Int64", StringComparison.OrdinalIgnoreCase)) return "1";
        if (type.Equals("UInt32", StringComparison.OrdinalIgnoreCase) || type.Equals("UInt64", StringComparison.OrdinalIgnoreCase)) return "1";
        if (type.Equals("Double", StringComparison.OrdinalIgnoreCase) || type.Equals("Single", StringComparison.OrdinalIgnoreCase) || type.Equals("Decimal", StringComparison.OrdinalIgnoreCase)) return "1";
        if (type.Equals("DateTime", StringComparison.OrdinalIgnoreCase)) return "'2000-01-01'";

        if (type.Equals("IDictionary", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("Hashtable", StringComparison.OrdinalIgnoreCase) ||
            type.EndsWith("Dictionary", StringComparison.OrdinalIgnoreCase))
            return "@{}";

        if (type.Equals("ScriptBlock", StringComparison.OrdinalIgnoreCase)) return "{ }";
        if (type.Equals("PSCredential", StringComparison.OrdinalIgnoreCase)) return "Get-Credential";
        if (type.Equals("SecureString", StringComparison.OrdinalIgnoreCase)) return "(Read-Host -AsSecureString)";

        return "'Value'";
    }
}
