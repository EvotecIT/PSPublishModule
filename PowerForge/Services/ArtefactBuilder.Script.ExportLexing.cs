using System;
using System.Linq;

namespace PowerForge;

public sealed partial class ArtefactBuilder
{
    private static bool TryGetExportModuleMemberInvocationStart(
        string line,
        out int commandStart,
        out bool preserveExpression)
    {
        var source = line ?? string.Empty;
        var state = ScriptLexicalState.Normal;
        var blockCommentDepth = 0;
        var previousSignificant = '\0';
        var previousSignificantIndex = -1;
        for (var index = 0; index < source.Length; index++)
        {
            var current = source[index];
            var next = index + 1 < source.Length ? source[index + 1] : '\0';
            if (blockCommentDepth > 0)
            {
                if (current == '<' && next == '#')
                {
                    blockCommentDepth++;
                    index++;
                }
                else if (current == '#' && next == '>')
                {
                    blockCommentDepth--;
                    index++;
                }
                continue;
            }

            if (state == ScriptLexicalState.SingleQuotedString)
            {
                if (current != '\'')
                    continue;
                if (next == '\'')
                {
                    index++;
                    continue;
                }
                state = ScriptLexicalState.Normal;
                continue;
            }

            if (state == ScriptLexicalState.DoubleQuotedString)
            {
                if (current == '`')
                {
                    index++;
                    continue;
                }
                if (current == '"')
                    state = ScriptLexicalState.Normal;
                continue;
            }

            if (current == '<' && next == '#')
            {
                blockCommentDepth++;
                index++;
                continue;
            }
            if (current == '#')
                break;
            if (current == '@' && next is '\'' or '"')
                break;
            if (current is '\'' or '"' &&
                TryGetQuotedExportCallOperator(source, index, out int quotedCallOperatorIndex))
            {
                commandStart = quotedCallOperatorIndex;
                preserveExpression = RequiresExpressionPlaceholder(source, commandStart);
                return true;
            }
            if (current == '\'')
            {
                state = ScriptLexicalState.SingleQuotedString;
                continue;
            }
            if (current == '"')
            {
                state = ScriptLexicalState.DoubleQuotedString;
                continue;
            }
            if (current == '`')
            {
                index++;
                continue;
            }
            if (char.IsWhiteSpace(current))
                continue;

            var callOperator = previousSignificant is '&' or '.' &&
                               previousSignificantIndex >= 0 &&
                               source.Substring(previousSignificantIndex + 1, index - previousSignificantIndex - 1)
                                   .All(char.IsWhiteSpace);
            var expressionBoundary = previousSignificant is '=' or '(';
            var expressionPosition = expressionBoundary || RequiresExpressionPlaceholder(source, index);
            if ((previousSignificant == '\0' || previousSignificant is ';' or '{' || expressionPosition || callOperator) &&
                (StartsWithCommandName(source.Substring(index), "Export-ModuleMember") ||
                 StartsWithCommandName(source.Substring(index), "Microsoft.PowerShell.Core\\Export-ModuleMember")))
            {
                commandStart = callOperator ? previousSignificantIndex : index;
                preserveExpression = expressionPosition || RequiresExpressionPlaceholder(source, commandStart);
                return true;
            }

            previousSignificant = current;
            previousSignificantIndex = index;
        }

        commandStart = -1;
        preserveExpression = false;
        return false;
    }

    private static bool IsQuotedExportCommandName(string source, int quoteStart)
    {
        char quote = source[quoteStart];
        int quoteEnd = quoteStart + 1;
        while (quoteEnd < source.Length)
        {
            if (source[quoteEnd] == quote)
            {
                if (quote == '\'' &&
                    quoteEnd + 1 < source.Length && source[quoteEnd + 1] == '\'')
                {
                    return false;
                }
                break;
            }
            if (quote == '"' && (source[quoteEnd] is '`' or '$'))
                return false;
            quoteEnd++;
        }
        if (quoteEnd >= source.Length)
            return false;

        string commandName = source.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
        return string.Equals(commandName, "Export-ModuleMember", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   commandName,
                   "Microsoft.PowerShell.Core\\Export-ModuleMember",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetQuotedExportCallOperator(
        string source,
        int quoteStart,
        out int callOperatorIndex)
    {
        callOperatorIndex = -1;
        if (!IsQuotedExportCommandName(source, quoteStart))
            return false;

        int index = quoteStart - 1;
        while (index >= 0 && char.IsWhiteSpace(source[index]))
            index--;
        while (index >= 0 && source[index] == '(')
        {
            index--;
            while (index >= 0 && char.IsWhiteSpace(source[index]))
                index--;
        }

        if (index < 0 || source[index] is not ('&' or '.'))
            return false;

        callOperatorIndex = index;
        return true;
    }

    private static bool RequiresExpressionPlaceholder(string source, int commandStart)
    {
        var prefix = source.Substring(0, commandStart).TrimEnd();
        if (prefix.Length == 0)
            return false;

        if (prefix[prefix.Length - 1] is '=' or '(')
            return true;

        const string returnKeyword = "return";
        if (!prefix.EndsWith(returnKeyword, StringComparison.OrdinalIgnoreCase))
            return false;

        var keywordStart = prefix.Length - returnKeyword.Length;
        return keywordStart == 0 || !IsPowerShellWordCharacter(prefix[keywordStart - 1]);
    }

    private static bool IsPowerShellWordCharacter(char value)
        => char.IsLetterOrDigit(value) || value is '_' or '-';

    private static bool StartsWithCommandName(string line, string commandName)
    {
        if (!line.StartsWith(commandName, StringComparison.OrdinalIgnoreCase))
            return false;

        return line.Length == commandName.Length ||
               char.IsWhiteSpace(line[commandName.Length]) ||
               line[commandName.Length] is '`' or ';' or '|';
    }
}
