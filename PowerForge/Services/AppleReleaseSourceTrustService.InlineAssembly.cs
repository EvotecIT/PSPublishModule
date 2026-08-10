using System.Text.RegularExpressions;

namespace PowerForge;

internal sealed partial class AppleReleaseSourceTrustService
{
    private void ValidateInlineAssemblerInputs(string repositoryRoot, string sourcePath, string source)
    {
        var syntax = MaskCStringAndCharacterLiterals(source);
        foreach (Match invocation in Regex.Matches(
                     syntax,
                     "(?<![A-Za-z0-9_])(?:asm|__asm|__asm__)(?![A-Za-z0-9_])(?:[ \\t\\r\\n]+(?:volatile|__volatile|__volatile__|goto))*[ \\t\\r\\n]*\\(",
                     RegexOptions.CultureInvariant))
        {
            var opening = invocation.Index + invocation.Length - 1;
            var closing = FindMatchingCDelimiter(source, opening, '(', ')');
            var body = source.Substring(opening + 1, closing - opening - 1);
            var template = ReadCInlineAssemblyTemplate(body);
            if (!TryDecodeConcatenatedCStringLiterals(template, out var assemblerSource))
            {
                throw new InvalidOperationException(
                    $"Source input '{sourcePath}' uses computed inline assembler text, whose file directives cannot be bound to the exact source commit. " +
                    "Use literal inline assembler text or a tracked standalone assembler source.");
            }
            ValidateAssemblerDirectives(repositoryRoot, sourcePath, assemblerSource);
        }
    }

    private static bool IsCInlineAssemblySource(string extension)
        => extension.Equals(".c", StringComparison.OrdinalIgnoreCase) ||
           extension.Equals(".cc", StringComparison.OrdinalIgnoreCase) ||
           extension.Equals(".cpp", StringComparison.OrdinalIgnoreCase) ||
           extension.Equals(".cxx", StringComparison.OrdinalIgnoreCase) ||
           extension.Equals(".m", StringComparison.OrdinalIgnoreCase) ||
           extension.Equals(".mm", StringComparison.OrdinalIgnoreCase) ||
           extension.Equals(".h", StringComparison.OrdinalIgnoreCase) ||
           extension.Equals(".hh", StringComparison.OrdinalIgnoreCase) ||
           extension.Equals(".hpp", StringComparison.OrdinalIgnoreCase) ||
           extension.Equals(".hxx", StringComparison.OrdinalIgnoreCase) ||
           extension.Equals(".pch", StringComparison.OrdinalIgnoreCase);

    private static int FindMatchingCDelimiter(string source, int openingIndex, char opening, char closing)
    {
        var depth = 0;
        var quote = '\0';
        var escaped = false;
        for (var index = openingIndex; index < source.Length; index++)
        {
            var current = source[index];
            if (quote != '\0')
            {
                if (escaped)
                    escaped = false;
                else if (current == '\\')
                    escaped = true;
                else if (current == quote)
                    quote = '\0';
                continue;
            }
            if (current is '\"' or '\'')
            {
                quote = current;
                continue;
            }
            if (current == opening)
                depth++;
            else if (current == closing && --depth == 0)
                return index;
        }
        throw new InvalidOperationException("Inline assembler declaration contains an unterminated parenthesized expression.");
    }

    private static string ReadCInlineAssemblyTemplate(string body)
    {
        var quote = '\0';
        var escaped = false;
        var parentheses = 0;
        var brackets = 0;
        var braces = 0;
        for (var index = 0; index < body.Length; index++)
        {
            var current = body[index];
            if (quote != '\0')
            {
                if (escaped)
                    escaped = false;
                else if (current == '\\')
                    escaped = true;
                else if (current == quote)
                    quote = '\0';
                continue;
            }
            if (current is '\"' or '\'')
            {
                quote = current;
                continue;
            }
            switch (current)
            {
                case '(':
                    parentheses++;
                    break;
                case ')':
                    parentheses--;
                    break;
                case '[':
                    brackets++;
                    break;
                case ']':
                    brackets--;
                    break;
                case '{':
                    braces++;
                    break;
                case '}':
                    braces--;
                    break;
                case ':' when parentheses == 0 && brackets == 0 && braces == 0:
                    return body.Substring(0, index);
            }
        }
        return body;
    }

    private static bool TryDecodeConcatenatedCStringLiterals(string expression, out string value)
    {
        var result = new System.Text.StringBuilder();
        var index = 0;
        var literals = 0;
        while (index < expression.Length)
        {
            while (index < expression.Length && char.IsWhiteSpace(expression[index]))
                index++;
            if (index >= expression.Length)
                break;

            if (index + 1 < expression.Length && expression[index] == 'u' && expression[index + 1] == '8')
                index += 2;
            else if (expression[index] is 'u' or 'U' or 'L')
                index++;
            if (index >= expression.Length || expression[index] != '\"')
            {
                value = string.Empty;
                return false;
            }
            index++;
            literals++;
            var closed = false;
            while (index < expression.Length)
            {
                var current = expression[index++];
                if (current == '\"')
                {
                    closed = true;
                    break;
                }
                if (current != '\\')
                {
                    result.Append(current);
                    continue;
                }
                if (index >= expression.Length)
                {
                    value = string.Empty;
                    return false;
                }
                var escaped = expression[index++];
                result.Append(escaped switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    'a' => '\a',
                    'b' => '\b',
                    'f' => '\f',
                    'v' => '\v',
                    '\\' => '\\',
                    '\"' => '\"',
                    '\'' => '\'',
                    '?' => '?',
                    _ => '\0'
                });
                if (result[result.Length - 1] == '\0')
                {
                    value = string.Empty;
                    return false;
                }
            }
            if (!closed)
            {
                value = string.Empty;
                return false;
            }
        }
        value = result.ToString();
        return literals > 0;
    }
}
