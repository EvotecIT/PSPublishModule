using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

internal static partial class ModuleMergeComposer
{
    private static void RebaseLateRequiresAssemblyDirectives(
        IReadOnlyList<string> lines,
        IDictionary<int, string> lineReplacements,
        string sourcePath,
        string moduleRoot)
    {
        var blockCommentDepth = 0;
        var state = PowerShellLexicalState.Normal;

        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex] ?? string.Empty;
            var hasCode = state is PowerShellLexicalState.SingleQuotedString or PowerShellLexicalState.DoubleQuotedString;

            if (state is PowerShellLexicalState.SingleQuotedHereString or PowerShellLexicalState.DoubleQuotedHereString)
            {
                var terminator = state == PowerShellLexicalState.SingleQuotedHereString ? "'@" : "\"@";
                var trimmed = line.TrimStart();
                if (!trimmed.StartsWith(terminator, StringComparison.Ordinal))
                    continue;

                var suffix = trimmed.Substring(terminator.Length);
                if (suffix.Length > 0 && !string.IsNullOrWhiteSpace(suffix) && !suffix.TrimStart().StartsWith("#", StringComparison.Ordinal))
                    continue;

                state = PowerShellLexicalState.Normal;
                continue;
            }

            for (var characterIndex = 0; characterIndex < line.Length; characterIndex++)
            {
                var current = line[characterIndex];
                var next = characterIndex + 1 < line.Length ? line[characterIndex + 1] : '\0';

                if (blockCommentDepth > 0)
                {
                    if (current == '<' && next == '#')
                    {
                        blockCommentDepth++;
                        characterIndex++;
                    }
                    else if (current == '#' && next == '>')
                    {
                        blockCommentDepth--;
                        characterIndex++;
                    }
                    continue;
                }

                if (state == PowerShellLexicalState.SingleQuotedString)
                {
                    if (current != '\'')
                        continue;
                    if (next == '\'')
                    {
                        characterIndex++;
                        continue;
                    }

                    state = PowerShellLexicalState.Normal;
                    continue;
                }

                if (state == PowerShellLexicalState.DoubleQuotedString)
                {
                    if (current == '`')
                    {
                        characterIndex++;
                        continue;
                    }
                    if (current == '"')
                        state = PowerShellLexicalState.Normal;
                    continue;
                }

                if (char.IsWhiteSpace(current))
                    continue;
                if (current == '<' && next == '#')
                {
                    blockCommentDepth++;
                    characterIndex++;
                    continue;
                }
                if (current == '#')
                {
                    if (!hasCode &&
                        !lineReplacements.ContainsKey(lineIndex) &&
                        StartsWithDirective(line, characterIndex, "#requires"))
                    {
                        var directive = line.Substring(characterIndex);
                        var rebased = RebaseRequiresAssemblyDirective(directive, sourcePath, moduleRoot);
                        if (!string.Equals(directive, rebased, StringComparison.Ordinal))
                            lineReplacements[lineIndex] = line.Substring(0, characterIndex) + rebased;
                    }

                    break;
                }
                if (current == '@' && next is '\'' or '"')
                {
                    state = next == '\''
                        ? PowerShellLexicalState.SingleQuotedHereString
                        : PowerShellLexicalState.DoubleQuotedHereString;
                    break;
                }
                if (current == '\'')
                {
                    state = PowerShellLexicalState.SingleQuotedString;
                    hasCode = true;
                    continue;
                }
                if (current == '"')
                {
                    state = PowerShellLexicalState.DoubleQuotedString;
                    hasCode = true;
                    continue;
                }
                if (current == '`')
                {
                    hasCode = true;
                    characterIndex++;
                    continue;
                }

                hasCode = true;
            }
        }
    }

    private enum PowerShellLexicalState
    {
        Normal,
        SingleQuotedString,
        DoubleQuotedString,
        SingleQuotedHereString,
        DoubleQuotedHereString
    }

    private static string RebaseRequiresAssemblyDirective(string directive, string sourcePath, string moduleRoot)
        => Regex.Replace(
            directive ?? string.Empty,
            @"(?i)(?<prefix>(?:^|\s)-Assembly\s+)(?:(?<single>')(?<singlePath>(?:''|[^'\r\n])*)'|(?<double>"")(?<doublePath>(?:`[^\r\n]|[^`""\r\n])*)""|(?<bare>[^\s;]+))",
            match =>
            {
                var quote = match.Groups["single"].Success
                    ? '\''
                    : match.Groups["double"].Success ? '"' : '\0';
                var encodedPath = match.Groups["single"].Success
                    ? match.Groups["singlePath"].Value
                    : match.Groups["double"].Success
                        ? match.Groups["doublePath"].Value
                        : match.Groups["bare"].Value;
                var path = quote == '\0' ? encodedPath : DecodeUsingPathLiteral(encodedPath, quote);
                var rebased = RebaseUsingPath(
                    path,
                    sourcePath,
                    moduleRoot,
                    treatBareNameAsPath: IsBareAssemblyRequirementPath(path, sourcePath));
                if (string.Equals(path, rebased, StringComparison.Ordinal))
                    return match.Value;

                var encodedRebased = quote == '\0'
                    ? FormatUnquotedUsingPath(rebased)
                    : quote + EncodeUsingPathLiteral(rebased, quote) + quote;
                return match.Groups["prefix"].Value + encodedRebased;
            });

    private static bool IsBareAssemblyRequirementPath(string value, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.StartsWith(".", StringComparison.Ordinal) ||
            value.IndexOf('/') >= 0 ||
            value.IndexOf('\\') >= 0)
        {
            return false;
        }

        var extension = Path.GetExtension(value);
        if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".winmd", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
        return !string.IsNullOrWhiteSpace(sourceDirectory) &&
               File.Exists(Path.Combine(sourceDirectory, value));
    }

    private static string RebaseUsingDirective(string directive, string sourcePath, string moduleRoot)
    {
        if (string.IsNullOrWhiteSpace(directive) || string.IsNullOrWhiteSpace(moduleRoot))
            return directive;

        if (!TryParseUsingDirectiveHeader(directive, out var kind, out var index))
            return directive;
        if (!string.Equals(kind, "assembly", System.StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(kind, "module", System.StringComparison.OrdinalIgnoreCase))
        {
            return directive;
        }

        if (index >= directive.Length)
            return directive;
        if (directive[index] == '@')
            return string.Equals(kind, "module", System.StringComparison.OrdinalIgnoreCase)
                ? RebaseUsingModuleSpecification(directive, sourcePath, moduleRoot)
                : directive;

        var quote = directive[index] is '\'' or '"' ? directive[index++] : '\0';
        var pathStart = index;
        var pathEnd = quote == '\0'
            ? FindUnquotedPathEnd(directive, pathStart)
            : FindQuotedPathEnd(directive, pathStart, quote);
        if (pathEnd < pathStart)
            return directive;

        var encodedUsingPath = directive.Substring(pathStart, pathEnd - pathStart);
        var usingPath = quote == '\0'
            ? encodedUsingPath
            : DecodeUsingPathLiteral(encodedUsingPath, quote);
        var rebased = RebaseUsingPath(
            usingPath,
            sourcePath,
            moduleRoot,
            treatBareNameAsPath: string.Equals(kind, "assembly", System.StringComparison.OrdinalIgnoreCase));
        if (string.Equals(usingPath, rebased, System.StringComparison.Ordinal))
            return directive;

        var encodedRebased = quote == '\0'
            ? FormatUnquotedUsingPath(rebased)
            : EncodeUsingPathLiteral(rebased, quote);
        return directive.Substring(0, pathStart) + encodedRebased + directive.Substring(pathEnd);
    }

    private static int FindUsingDirectiveEnd(
        IReadOnlyList<string> lines,
        int startLine,
        int directiveStart)
    {
        var firstLine = lines[startLine].Substring(directiveStart);
        if (!TryParseUsingDirectiveHeader(firstLine, out var kind, out var argumentStart) ||
            !string.Equals(kind, "module", System.StringComparison.OrdinalIgnoreCase) ||
            argumentStart + 1 >= firstLine.Length ||
            firstLine[argumentStart] != '@' ||
            firstLine[argumentStart + 1] != '{')
        {
            return startLine;
        }

        var braceDepth = 0;
        var blockCommentDepth = 0;
        var inSingleQuotedString = false;
        var inDoubleQuotedString = false;
        var started = false;
        for (var lineIndex = startLine; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var characterIndex = lineIndex == startLine ? directiveStart + argumentStart : 0;
            for (; characterIndex < line.Length; characterIndex++)
            {
                var current = line[characterIndex];
                var next = characterIndex + 1 < line.Length ? line[characterIndex + 1] : '\0';

                if (blockCommentDepth > 0)
                {
                    if (current == '<' && next == '#')
                    {
                        blockCommentDepth++;
                        characterIndex++;
                    }
                    else if (current == '#' && next == '>')
                    {
                        blockCommentDepth--;
                        characterIndex++;
                    }
                    continue;
                }

                if (inSingleQuotedString)
                {
                    if (current != '\'')
                        continue;
                    if (next == '\'')
                    {
                        characterIndex++;
                        continue;
                    }
                    inSingleQuotedString = false;
                    continue;
                }

                if (inDoubleQuotedString)
                {
                    if (current == '`')
                    {
                        characterIndex++;
                        continue;
                    }
                    if (current == '"')
                        inDoubleQuotedString = false;
                    continue;
                }

                if (current == '#')
                    break;
                if (current == '<' && next == '#')
                {
                    blockCommentDepth++;
                    characterIndex++;
                    continue;
                }
                if (current == '\'')
                {
                    inSingleQuotedString = true;
                    continue;
                }
                if (current == '"')
                {
                    inDoubleQuotedString = true;
                    continue;
                }
                if (current == '{')
                {
                    braceDepth++;
                    started = true;
                    continue;
                }
                if (current != '}' || !started)
                    continue;

                braceDepth--;
                if (braceDepth == 0)
                    return lineIndex;
            }
        }

        return startLine;
    }

    private static bool TryParseUsingDirectiveHeader(
        string directive,
        out string kind,
        out int argumentStart)
    {
        kind = string.Empty;
        argumentStart = 0;
        if (string.IsNullOrWhiteSpace(directive) ||
            !StartsWithDirective(directive, 0, "using"))
        {
            return false;
        }

        var index = "using".Length;
        while (index < directive.Length && char.IsWhiteSpace(directive[index]))
            index++;
        var kindStart = index;
        while (index < directive.Length && !char.IsWhiteSpace(directive[index]))
            index++;
        if (index == kindStart)
            return false;

        kind = directive.Substring(kindStart, index - kindStart);
        while (index < directive.Length && char.IsWhiteSpace(directive[index]))
            index++;
        argumentStart = index;
        return true;
    }

    private static string RebaseUsingModuleSpecification(
        string directive,
        string sourcePath,
        string moduleRoot)
        => Regex.Replace(
            directive,
            @"(?im)(\bModuleName\s*=\s*)(?:(?<single>')(?<singlePath>(?:''|[^'\r\n])*)'|(?<double>"")(?<doublePath>(?:`[^\r\n]|[^`""\r\n])*)"")",
            match =>
            {
                var quote = match.Groups["single"].Success ? '\'' : '"';
                var encodedPath = match.Groups["single"].Success
                    ? match.Groups["singlePath"].Value
                    : match.Groups["doublePath"].Value;
                var path = DecodeUsingPathLiteral(encodedPath, quote);
                var rebased = RebaseUsingPath(path, sourcePath, moduleRoot, treatBareNameAsPath: false);
                if (string.Equals(path, rebased, System.StringComparison.Ordinal))
                    return match.Value;

                return match.Groups[1].Value + quote + EncodeUsingPathLiteral(rebased, quote) + quote;
            });

    private static int FindQuotedPathEnd(string directive, int start, char quote)
    {
        for (var index = start; index < directive.Length; index++)
        {
            if (quote == '\'' && directive[index] == '\'')
            {
                if (index + 1 < directive.Length && directive[index + 1] == '\'')
                {
                    index++;
                    continue;
                }

                return index;
            }

            if (quote == '"' && directive[index] == '`')
            {
                index++;
                continue;
            }

            if (directive[index] == quote)
                return index;
        }

        return -1;
    }

    private static string DecodeUsingPathLiteral(string value, char quote)
    {
        if (quote == '\'')
            return value.Replace("''", "'");
        if (quote != '"' || value.IndexOf('`') < 0)
            return value;

        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (current != '`' || index + 1 >= value.Length)
            {
                builder.Append(current);
                continue;
            }

            var escaped = value[++index];
            if (escaped == 'u' && index + 2 < value.Length && value[index + 1] == '{')
            {
                var unicodeEnd = value.IndexOf('}', index + 2);
                var unicodeLength = unicodeEnd - index - 2;
                if (unicodeEnd > index + 2 &&
                    unicodeLength <= 6 &&
                    int.TryParse(
                        value.Substring(index + 2, unicodeLength),
                        NumberStyles.AllowHexSpecifier,
                        CultureInfo.InvariantCulture,
                        out var codePoint) &&
                    codePoint <= 0x10ffff &&
                    (codePoint < 0xd800 || codePoint > 0xdfff))
                {
                    builder.Append(char.ConvertFromUtf32(codePoint));
                    index = unicodeEnd;
                    continue;
                }
            }

            builder.Append(escaped switch
            {
                '0' => '\0',
                'a' => '\a',
                'b' => '\b',
                'e' => '\u001b',
                'f' => '\f',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                'v' => '\v',
                _ => escaped
            });
        }

        return builder.ToString();
    }

    private static string EncodeUsingPathLiteral(string value, char quote)
    {
        if (quote == '\'')
            return value.Replace("'", "''");
        if (quote != '"')
            return value;

        var builder = new StringBuilder(value.Length);
        foreach (var current in value)
        {
            builder.Append(current switch
            {
                '`' => "``",
                '$' => "`$",
                '"' => "`\"",
                '\0' => "`0",
                '\a' => "`a",
                '\b' => "`b",
                '\u001b' => "`e",
                '\f' => "`f",
                '\n' => "`n",
                '\r' => "`r",
                '\t' => "`t",
                '\v' => "`v",
                _ => current.ToString()
            });
        }

        return builder.ToString();
    }

    private static string FormatUnquotedUsingPath(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (char.IsLetterOrDigit(current) || current is '_' or '-' or '.' or '/' or '\\' or ':')
                continue;

            return "'" + EncodeUsingPathLiteral(value, '\'') + "'";
        }

        return value;
    }

    private static string RebaseUsingPath(
        string usingPath,
        string sourcePath,
        string moduleRoot,
        bool treatBareNameAsPath)
    {
        if (!IsRelativeUsingPath(usingPath, treatBareNameAsPath))
            return usingPath;

        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
        if (string.IsNullOrWhiteSpace(sourceDirectory))
            return usingPath;

        var normalizedPath = usingPath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var absolutePath = Path.GetFullPath(Path.Combine(sourceDirectory, normalizedPath));
        var rebased = FrameworkCompatibility.GetRelativePath(moduleRoot, absolutePath).Replace('\\', '/');
        return rebased.StartsWith(".", System.StringComparison.Ordinal) ? rebased : "./" + rebased;
    }

    private static int FindUnquotedPathEnd(string directive, int start)
    {
        var index = start;
        while (index < directive.Length && !char.IsWhiteSpace(directive[index]) && directive[index] != ';')
            index++;
        return index;
    }

    private static bool IsRelativeUsingPath(string path, bool treatBareNameAsPath)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            Path.IsPathRooted(path) ||
            path.StartsWith("\\\\", System.StringComparison.Ordinal) ||
            path.StartsWith("//", System.StringComparison.Ordinal) ||
            (path.Length > 2 && path[1] == ':' && (path[2] == '\\' || path[2] == '/')))
        {
            return false;
        }

        return treatBareNameAsPath ||
               path.StartsWith(".", System.StringComparison.Ordinal) ||
               path.IndexOf('\\') >= 0 ||
               path.IndexOf('/') >= 0;
    }

}
