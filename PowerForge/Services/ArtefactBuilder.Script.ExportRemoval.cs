using System;
using System.Collections.Generic;

namespace PowerForge;

public sealed partial class ArtefactBuilder
{
    private static void RemoveHandWrittenExportInvocations(List<string> lines)
    {
        var state = ScriptLexicalState.Normal;
        var blockCommentDepth = 0;
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex] ?? string.Empty;
            if (state == ScriptLexicalState.Normal &&
                blockCommentDepth == 0 &&
                TryGetExportModuleMemberInvocationStart(line, out var commandStart, out var preserveExpression))
            {
                preserveExpression = preserveExpression ||
                                     IsPrecededByPipelineChainOperator(lines, lineIndex);
                var endLine = FindPowerShellCommandEnd(lines, lineIndex, commandStart, out var suffixStart);
                var removeCount = endLine - lineIndex + 1;
                var prefix = line.Substring(0, commandStart);
                var suffix = suffixStart >= 0
                    ? lines[endLine].Substring(suffixStart).TrimStart()
                    : string.Empty;
                var statementSeparator = string.Empty;
                if (preserveExpression && suffixStart >= 0)
                {
                    if (suffixStart > 0 && lines[endLine][suffixStart - 1] == ';')
                        statementSeparator = "; ";
                    else if (suffix.StartsWith("&&", StringComparison.Ordinal) ||
                             suffix.StartsWith("||", StringComparison.Ordinal))
                        statementSeparator = " ";
                }
                lines.RemoveRange(lineIndex, removeCount);
                var replacement = prefix +
                                  (preserveExpression ? "$null" : string.Empty) +
                                  statementSeparator +
                                  suffix;
                if (!string.IsNullOrWhiteSpace(replacement))
                    lines.Insert(lineIndex, replacement);
                lineIndex--;
                continue;
            }

            UpdateScriptLexicalState(line, ref state, ref blockCommentDepth);
        }
    }

    private static bool IsPrecededByPipelineChainOperator(
        IReadOnlyList<string> lines,
        int lineIndex)
    {
        for (int previousIndex = lineIndex - 1; previousIndex >= 0; previousIndex--)
        {
            string previousLine = (lines[previousIndex] ?? string.Empty).TrimEnd();
            if (string.IsNullOrWhiteSpace(previousLine) ||
                previousLine.TrimStart().StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            int operatorIndex = Math.Max(
                previousLine.LastIndexOf("&&", StringComparison.Ordinal),
                previousLine.LastIndexOf("||", StringComparison.Ordinal));
            if (operatorIndex < 0)
                return false;

            string trailingText = previousLine.Substring(operatorIndex + 2).TrimStart();
            return trailingText.Length == 0 ||
                   trailingText.StartsWith("#", StringComparison.Ordinal);
        }

        return false;
    }

    private static int FindPowerShellCommandEnd(
        IReadOnlyList<string> lines,
        int startLine,
        int commandStart,
        out int suffixStart)
    {
        var state = ScriptLexicalState.Normal;
        var blockCommentDepth = 0;
        var parenthesisDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;
        suffixStart = -1;

        for (var lineIndex = startLine; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex] ?? string.Empty;
            var characterIndex = lineIndex == startLine ? commandStart : 0;
            var lineContinues = false;
            var lastSignificant = '\0';

            if (state is ScriptLexicalState.SingleQuotedHereString or ScriptLexicalState.DoubleQuotedHereString)
            {
                var terminator = state == ScriptLexicalState.SingleQuotedHereString ? "'@" : "\"@";
                var trimmed = line.TrimStart();
                if (!trimmed.StartsWith(terminator, StringComparison.Ordinal))
                    continue;

                characterIndex = line.Length - trimmed.Length + terminator.Length;
                state = ScriptLexicalState.Normal;
            }

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

                if (state == ScriptLexicalState.SingleQuotedString)
                {
                    if (current != '\'')
                        continue;
                    if (next == '\'')
                    {
                        characterIndex++;
                        continue;
                    }
                    state = ScriptLexicalState.Normal;
                    lastSignificant = current;
                    continue;
                }

                if (state == ScriptLexicalState.DoubleQuotedString)
                {
                    if (current == '`')
                    {
                        characterIndex++;
                        continue;
                    }
                    if (current == '"')
                    {
                        state = ScriptLexicalState.Normal;
                        lastSignificant = current;
                    }
                    continue;
                }

                if (current == '<' && next == '#')
                {
                    blockCommentDepth++;
                    characterIndex++;
                    continue;
                }
                if (current == '#')
                    break;
                if (current == '@' && next is '\'' or '"')
                {
                    state = next == '\''
                        ? ScriptLexicalState.SingleQuotedHereString
                        : ScriptLexicalState.DoubleQuotedHereString;
                    break;
                }
                if (current == '\'')
                {
                    state = ScriptLexicalState.SingleQuotedString;
                    lastSignificant = current;
                    continue;
                }
                if (current == '"')
                {
                    state = ScriptLexicalState.DoubleQuotedString;
                    lastSignificant = current;
                    continue;
                }
                if (current == '`')
                {
                    if (string.IsNullOrWhiteSpace(line.Substring(characterIndex + 1)))
                    {
                        lineContinues = true;
                        break;
                    }
                    characterIndex++;
                    continue;
                }

                if (char.IsWhiteSpace(current))
                    continue;

                if (current == ';' && parenthesisDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                {
                    suffixStart = characterIndex + 1;
                    return lineIndex;
                }

                if (((current == '&' && next == '&') || (current == '|' && next == '|')) &&
                    parenthesisDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                {
                    suffixStart = characterIndex;
                    return lineIndex;
                }

                if (current == '}' && parenthesisDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                {
                    suffixStart = characterIndex;
                    return lineIndex;
                }

                if (current is ')' or ']' && parenthesisDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                {
                    suffixStart = characterIndex;
                    return lineIndex;
                }

                switch (current)
                {
                    case '(':
                        parenthesisDepth++;
                        break;
                    case ')':
                        if (parenthesisDepth > 0) parenthesisDepth--;
                        break;
                    case '[':
                        bracketDepth++;
                        break;
                    case ']':
                        if (bracketDepth > 0) bracketDepth--;
                        break;
                    case '{':
                        braceDepth++;
                        break;
                    case '}':
                        if (braceDepth > 0) braceDepth--;
                        break;
                }

                lastSignificant = current;
            }

            if (state == ScriptLexicalState.Normal &&
                blockCommentDepth == 0 &&
                parenthesisDepth == 0 &&
                bracketDepth == 0 &&
                braceDepth == 0 &&
                !lineContinues &&
                lastSignificant is not ('|' or ','))
            {
                return lineIndex;
            }
        }

        return lines.Count - 1;
    }
}
