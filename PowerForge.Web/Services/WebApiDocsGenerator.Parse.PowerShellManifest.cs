using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    private static PowerShellCommandKindHints LoadPowerShellCommandKindHints(string? manifestPath, List<string> warnings)
    {
        var hints = new PowerShellCommandKindHints();
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            return hints;

        string text;
        try
        {
            text = File.ReadAllText(manifestPath);
        }
        catch (Exception ex)
        {
            warnings?.Add($"PowerShell manifest metadata unavailable: {Path.GetFileName(manifestPath)} ({ex.GetType().Name}: {ex.Message})");
            return hints;
        }

        hints.CmdletsWildcard = ParseManifestExportValues(text, "CmdletsToExport", hints.Cmdlets);
        hints.FunctionsWildcard = ParseManifestExportValues(text, "FunctionsToExport", hints.Functions);
        hints.AliasesWildcard = ParseManifestExportValues(text, "AliasesToExport", hints.Aliases);

        var rootModule = ParseManifestScalarValue(text, "RootModule");
        if (!string.IsNullOrWhiteSpace(rootModule))
        {
            var manifestDir = Path.GetDirectoryName(manifestPath) ?? string.Empty;
            var modulePath = Path.IsPathRooted(rootModule)
                ? rootModule
                : Path.Combine(manifestDir, rootModule);
            if (modulePath.EndsWith(".psm1", StringComparison.OrdinalIgnoreCase) && File.Exists(modulePath))
            {
                try
                {
                    var script = File.ReadAllText(modulePath);
                    var functionRegex = new Regex(@"(?im)^\s*function\s+(?<name>[A-Za-z_][A-Za-z0-9_-]*)\b",
                        RegexOptions.Compiled | RegexOptions.CultureInvariant,
                        RegexTimeout);
                    foreach (Match match in functionRegex.Matches(script))
                    {
                        var functionName = match.Groups["name"].Value.Trim();
                        if (!string.IsNullOrWhiteSpace(functionName))
                            hints.Functions.Add(functionName);
                    }
                }
                catch
                {
                    // best-effort only
                }
            }
        }

        return hints;
    }

    private static bool ParseManifestExportValues(string text, string key, ISet<string> values)
    {
        var wildcard = false;
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(key))
            return wildcard;

        if (!TryExtractPowerShellManifestValue(text, key, out var rawValue))
            return wildcard;

        foreach (var value in ParsePowerShellStringLiterals(rawValue))
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (string.Equals(value, "*", StringComparison.Ordinal))
            {
                wildcard = true;
            }
            else
            {
                values.Add(value);
            }
        }

        foreach (var token in ParsePowerShellBareTokens(rawValue))
        {
            if (string.IsNullOrWhiteSpace(token))
                continue;

            if (string.Equals(token, "*", StringComparison.Ordinal))
            {
                wildcard = true;
            }
            else
            {
                values.Add(token);
            }
        }

        return wildcard;
    }

    private static string? ParseManifestScalarValue(string text, string key)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(key))
            return null;

        if (!TryExtractPowerShellManifestValue(text, key, out var raw))
            return null;

        var literals = ParsePowerShellStringLiterals(raw);
        if (literals.Count > 0)
            return literals[0];

        var token = ParsePowerShellBareTokens(raw).FirstOrDefault();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    private static bool TryExtractPowerShellManifestValue(string text, string key, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(key))
            return false;

        var keyPattern = $"(?im)^\\s*{Regex.Escape(key)}\\s*=";
        var match = Regex.Match(text, keyPattern, RegexOptions.CultureInvariant, RegexTimeout);
        if (!match.Success)
            return false;

        var start = match.Index + match.Length;
        var expression = ReadPowerShellManifestExpression(text, start);
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        value = expression;
        return true;
    }

    private static string ReadPowerShellManifestExpression(string text, int start)
    {
        if (string.IsNullOrWhiteSpace(text) || start < 0 || start >= text.Length)
            return string.Empty;

        var sb = new StringBuilder();
        var parenDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var inComment = false;

        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];

            if (inComment)
            {
                if (ch == '\r' || ch == '\n')
                {
                    inComment = false;
                    if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                        break;
                    sb.Append(ch);
                }
                continue;
            }

            if (inSingleQuote)
            {
                sb.Append(ch);
                if (ch == '\'')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\'')
                    {
                        sb.Append('\'');
                        i++;
                        continue;
                    }
                    inSingleQuote = false;
                }
                continue;
            }

            if (inDoubleQuote)
            {
                sb.Append(ch);
                if (ch == '`' && i + 1 < text.Length)
                {
                    sb.Append(text[i + 1]);
                    i++;
                    continue;
                }
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                        continue;
                    }
                    inDoubleQuote = false;
                }
                continue;
            }

            if (ch == '#')
            {
                inComment = true;
                continue;
            }

            if ((ch == '\r' || ch == '\n') && parenDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                break;

            if (sb.Length == 0 && char.IsWhiteSpace(ch))
                continue;

            if (ch == '\'')
            {
                inSingleQuote = true;
                sb.Append(ch);
                continue;
            }

            if (ch == '"')
            {
                inDoubleQuote = true;
                sb.Append(ch);
                continue;
            }

            if (ch == '(')
                parenDepth++;
            else if (ch == ')' && parenDepth > 0)
                parenDepth--;
            else if (ch == '[')
                bracketDepth++;
            else if (ch == ']' && bracketDepth > 0)
                bracketDepth--;
            else if (ch == '{')
                braceDepth++;
            else if (ch == '}' && braceDepth > 0)
                braceDepth--;

            sb.Append(ch);
        }

        return sb.ToString().Trim();
    }

    private static List<string> ParsePowerShellStringLiterals(string expression)
    {
        var values = new List<string>();
        if (string.IsNullOrWhiteSpace(expression))
            return values;

        for (var i = 0; i < expression.Length; i++)
        {
            var quote = expression[i];
            if (quote != '\'' && quote != '"')
                continue;

            var value = new StringBuilder();
            for (i = i + 1; i < expression.Length; i++)
            {
                var ch = expression[i];
                if (quote == '\'' && ch == '\'')
                {
                    if (i + 1 < expression.Length && expression[i + 1] == '\'')
                    {
                        value.Append('\'');
                        i++;
                        continue;
                    }
                    break;
                }

                if (quote == '"' && ch == '"')
                {
                    if (i + 1 < expression.Length && expression[i + 1] == '"')
                    {
                        value.Append('"');
                        i++;
                        continue;
                    }
                    break;
                }

                if (quote == '"' && ch == '`' && i + 1 < expression.Length)
                {
                    value.Append(expression[i + 1]);
                    i++;
                    continue;
                }

                value.Append(ch);
            }

            var parsed = value.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(parsed))
                values.Add(parsed);
        }

        return values;
    }

    private static IEnumerable<string> ParsePowerShellBareTokens(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            yield break;

        var bare = Regex.Replace(expression, "(['\"]).*?\\1", " ", RegexOptions.Singleline | RegexOptions.CultureInvariant, RegexTimeout);
        bare = bare.Replace("@(", " ", StringComparison.Ordinal)
            .Replace(")", " ", StringComparison.Ordinal)
            .Replace("@{", " ", StringComparison.Ordinal)
            .Replace("{", " ", StringComparison.Ordinal)
            .Replace("}", " ", StringComparison.Ordinal)
            .Replace(",", " ", StringComparison.Ordinal);

        foreach (var token in bare.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith("$", StringComparison.Ordinal) || token.StartsWith("-", StringComparison.Ordinal))
                continue;
            if (string.Equals(token, "@", StringComparison.Ordinal))
                continue;
            yield return token.Trim();
        }
    }

    private sealed class PowerShellCommandKindHints
    {
        public HashSet<string> Cmdlets { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Functions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Aliases { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool CmdletsWildcard { get; set; }
        public bool FunctionsWildcard { get; set; }
        public bool AliasesWildcard { get; set; }
        public bool HasSignals => Cmdlets.Count > 0 || Functions.Count > 0 || Aliases.Count > 0 || CmdletsWildcard || FunctionsWildcard || AliasesWildcard;
    }
}
