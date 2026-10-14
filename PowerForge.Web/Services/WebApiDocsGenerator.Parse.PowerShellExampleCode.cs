using System.Globalization;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

/// <summary>Generates API documentation artifacts from XML docs.</summary>
public static partial class WebApiDocsGenerator
{
    // "PS>", "PS C:\>", "PS /home/user>" with nothing after the prompt.
    private static readonly Regex PowerShellPromptOnlyRegex = new(
        @"^PS(?:[ \t]+[^\r\n>]*)?>$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // A prompt that prefixes the first command line of an example.
    private static readonly Regex PowerShellLeadingPromptRegex = new(
        @"^(?<prompt>PS(?:[ \t]+[^\r\n>]*)?>)[ \t]*",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static bool IsPowerShellPromptOnly(string? text)
        => !string.IsNullOrWhiteSpace(text) && PowerShellPromptOnlyRegex.IsMatch(text.Trim());

    private static bool IsPowerShellPositionalParameter(string? position)
        => !string.IsNullOrWhiteSpace(position) &&
           int.TryParse(position.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) &&
           value >= 0;

    private static string ResolvePowerShellModuleDisplayName(string helpModuleName, string? manifestPath, WebApiDocsOptions options)
    {
        var configured = options?.PowerShellModuleName?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        // Help files are often lower-cased on disk ("imageplayground-help.xml"); prefer a source with the module's own casing.
        var manifestName = string.IsNullOrWhiteSpace(manifestPath) ? null : Path.GetFileNameWithoutExtension(manifestPath);
        if (!string.IsNullOrWhiteSpace(manifestName) && string.Equals(manifestName, helpModuleName, StringComparison.OrdinalIgnoreCase))
            return manifestName;

        var packageId = options?.PackageId?.Trim();
        if (!string.IsNullOrWhiteSpace(packageId) && string.Equals(packageId, helpModuleName, StringComparison.OrdinalIgnoreCase))
            return packageId;

        return helpModuleName;
    }

    /// <summary>
    /// Normalizes MAML example code: drops surrounding blank lines and removes indentation that comes from the XML
    /// layout rather than from the script. Help generators often trim the first line but keep the XML indentation on
    /// the following lines, so a statement that returns to the top level reveals how much of that indentation to drop.
    /// </summary>
    private static string NormalizePowerShellExampleCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return string.Empty;

        var lines = code
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(static line => line.TrimEnd())
            .ToList();
        while (lines.Count > 0 && lines[0].Length == 0)
            lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        if (lines.Count == 0)
            return string.Empty;

        RemoveExampleIndent(lines, 0, GetExampleCommonIndent(lines, 0));
        if (lines.Count > 1 && CountExampleLeadingWhitespace(lines[0]) == 0)
        {
            var continuationIndent = GetExampleCommonIndent(lines, 1);
            if (continuationIndent > 0 && HasTopLevelStatementAfterFirstLine(lines))
                RemoveExampleIndent(lines, 1, continuationIndent);
        }

        return string.Join("\n", lines);
    }

    private static bool TrySplitPowerShellPrompt(string codeText, string? languageToken, out string prompt, out string body)
    {
        prompt = string.Empty;
        body = codeText;

        if (!string.Equals(languageToken, "powershell", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.IsNullOrWhiteSpace(codeText))
            return false;

        var trimmedStart = codeText.TrimStart();
        var match = PowerShellLeadingPromptRegex.Match(trimmedStart);
        if (!match.Success)
            return false;

        prompt = match.Groups["prompt"].Value;
        body = trimmedStart.Substring(match.Length).TrimStart();
        return !string.IsNullOrWhiteSpace(body);
    }

    private static int GetExampleCommonIndent(IReadOnlyList<string> lines, int startIndex)
    {
        var common = int.MaxValue;
        for (var i = startIndex; i < lines.Count; i++)
        {
            if (lines[i].Length == 0)
                continue;
            common = Math.Min(common, CountExampleLeadingWhitespace(lines[i]));
        }

        return common == int.MaxValue ? 0 : common;
    }

    private static void RemoveExampleIndent(List<string> lines, int startIndex, int indent)
    {
        if (indent <= 0)
            return;

        for (var i = startIndex; i < lines.Count; i++)
        {
            var line = lines[i];
            lines[i] = line.Length == 0 ? line : line.Substring(Math.Min(indent, CountExampleLeadingWhitespace(line)));
        }
    }

    private static int CountExampleLeadingWhitespace(string line)
    {
        var count = 0;
        while (count < line.Length && (line[count] == ' ' || line[count] == '\t'))
            count++;
        return count;
    }

    /// <summary>
    /// Returns true when a line after the first starts a new top-level statement: it is outside any open bracket,
    /// string, comment or here-string, and the previous line does not continue onto it (pipe, backtick, comma, '=').
    /// </summary>
    private static bool HasTopLevelStatementAfterFirstLine(IReadOnlyList<string> lines)
    {
        var state = new ExampleScanState();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (i > 0 && state.IsAtStatementBoundary(line))
                return true;
            state.Scan(line);
        }

        return false;
    }

    private sealed class ExampleScanState
    {
        private int _depth;
        private bool _inBlockComment;
        private char _stringQuote;
        private string? _hereStringTerminator;
        private bool _previousLineContinues;

        public bool IsAtStatementBoundary(string line)
        {
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || _inBlockComment || _stringQuote != '\0' || _hereStringTerminator is not null || _previousLineContinues)
                return false;
            // A leading pipe or ".Method()" continues the previous statement; ".\script.ps1" does not.
            if (trimmed[0] == '|' || (trimmed[0] == '.' && trimmed.Length > 1 && char.IsLetter(trimmed[1])))
                return false;

            var depth = _depth;
            foreach (var ch in trimmed)
            {
                if (ch is '}' or ')' or ']')
                    depth--;
                else if (!char.IsWhiteSpace(ch))
                    break;
            }

            return depth <= 0;
        }

        public void Scan(string line)
        {
            var lastSignificant = '\0';
            var index = 0;
            if (_hereStringTerminator is not null)
            {
                var trimmed = line.TrimStart();
                if (!trimmed.StartsWith(_hereStringTerminator, StringComparison.Ordinal))
                    return;
                index = line.Length - trimmed.Length + _hereStringTerminator.Length;
                _hereStringTerminator = null;
                lastSignificant = '@';
            }

            for (; index < line.Length; index++)
            {
                var ch = line[index];
                var next = index + 1 < line.Length ? line[index + 1] : '\0';
                if (_inBlockComment)
                {
                    if (ch == '#' && next == '>')
                    {
                        _inBlockComment = false;
                        index++;
                    }
                    continue;
                }

                if (_stringQuote != '\0')
                {
                    if (_stringQuote == '"' && ch == '`')
                        index++;
                    else if (ch == _stringQuote && next == _stringQuote)
                        index++;
                    else if (ch == _stringQuote)
                    {
                        _stringQuote = '\0';
                        lastSignificant = ch;
                    }
                    continue;
                }

                if (ch == '<' && next == '#')
                {
                    _inBlockComment = true;
                    index++;
                    continue;
                }

                if (ch == '#')
                    break;
                if (ch == '@' && (next == '\'' || next == '"') && line.Substring(index + 2).Trim().Length == 0)
                {
                    _hereStringTerminator = next + "@";
                    break;
                }
                if (ch == '\'' || ch == '"')
                {
                    _stringQuote = ch;
                    continue;
                }
                if (ch == '`')
                {
                    lastSignificant = next == '\0' ? '`' : next;
                    index++;
                    continue;
                }

                if (ch is '{' or '(' or '[')
                    _depth++;
                else if (ch is '}' or ')' or ']')
                    _depth = Math.Max(0, _depth - 1);

                if (!char.IsWhiteSpace(ch))
                    lastSignificant = ch;
            }

            if (line.Trim().Length > 0 || _hereStringTerminator is not null)
                _previousLineContinues = _hereStringTerminator is null && lastSignificant is '|' or '`' or ',' or '=';
        }
    }
}
