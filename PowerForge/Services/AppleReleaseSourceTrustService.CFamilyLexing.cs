namespace PowerForge;

internal sealed partial class AppleReleaseSourceTrustService
{
    private static string MaskCStringAndCharacterLiterals(string source)
    {
        var result = new System.Text.StringBuilder(source.Length);
        var quote = '\0';
        var escaped = false;
        for (var index = 0; index < source.Length; index++)
        {
            var current = source[index];
            if (quote == '\0')
            {
                if (TryReadCppRawStringLiteral(source, index, out var rawEnd))
                {
                    for (; index <= rawEnd; index++)
                        result.Append(source[index] is '\r' or '\n' ? source[index] : ' ');
                    index--;
                    continue;
                }
                if (current is '\"' or '\'')
                {
                    quote = current;
                    result.Append(' ');
                }
                else
                {
                    result.Append(current);
                }
                continue;
            }

            result.Append(current is '\r' or '\n' ? current : ' ');
            if (escaped)
                escaped = false;
            else if (current == '\\')
                escaped = true;
            else if (current == quote)
                quote = '\0';
        }
        return result.ToString();
    }

    private static string SpliceCPreprocessingLines(string source)
    {
        var result = new System.Text.StringBuilder(source.Length);
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] != '\\' || index + 1 >= source.Length)
            {
                result.Append(source[index]);
                continue;
            }

            if (source[index + 1] == '\n')
            {
                index++;
                continue;
            }
            if (source[index + 1] == '\r')
            {
                index++;
                if (index + 1 < source.Length && source[index + 1] == '\n')
                    index++;
                continue;
            }

            result.Append(source[index]);
        }
        return result.ToString();
    }

    private static string RemoveCComments(string source)
    {
        var result = new System.Text.StringBuilder(source.Length);
        var inBlockComment = false;
        var inLineComment = false;
        var quote = '\0';
        var escaped = false;
        for (var index = 0; index < source.Length; index++)
        {
            var current = source[index];
            var next = index + 1 < source.Length ? source[index + 1] : '\0';
            if (inLineComment)
            {
                if (current == '\r' || current == '\n')
                {
                    inLineComment = false;
                    result.Append(current);
                }
                else
                {
                    result.Append(' ');
                }
                continue;
            }
            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    index++;
                    inBlockComment = false;
                }
                continue;
            }
            if (quote != '\0')
            {
                result.Append(current);
                if (escaped)
                    escaped = false;
                else if (current == '\\')
                    escaped = true;
                else if (current == quote)
                    quote = '\0';
                continue;
            }
            if (TryReadCppRawStringLiteral(source, index, out var rawEnd))
            {
                for (; index <= rawEnd; index++)
                    result.Append(source[index] is '\r' or '\n' ? source[index] : ' ');
                index--;
                continue;
            }
            if (current == '/' && next == '/')
            {
                result.Append("  ");
                index++;
                inLineComment = true;
                continue;
            }
            if (current == '/' && next == '*')
            {
                // Translation phase 3 replaces one complete block comment with one space.
                // In particular, newlines inside the comment do not terminate a directive.
                result.Append(' ');
                index++;
                inBlockComment = true;
                continue;
            }
            if (current == '"' || current == '\'')
                quote = current;
            result.Append(current);
        }
        return result.ToString();
    }

    private static bool TryReadCppRawStringLiteral(string source, int start, out int end)
    {
        end = -1;
        if (start > 0 && (source[start - 1] == '_' || char.IsLetterOrDigit(source[start - 1])))
            return false;

        var prefixes = new[] { "u8R\"", "uR\"", "UR\"", "LR\"", "R\"" };
        var prefix = prefixes.FirstOrDefault(candidate =>
            start + candidate.Length <= source.Length &&
            source.Substring(start, candidate.Length).Equals(candidate, StringComparison.Ordinal));
        if (prefix is null)
            return false;

        var delimiterStart = start + prefix.Length;
        var opening = delimiterStart;
        while (opening < source.Length &&
               opening - delimiterStart <= 16 &&
               source[opening] != '(')
        {
            var value = source[opening];
            if (value <= ' ' || value is ')' or '\\')
                return false;
            opening++;
        }
        if (opening >= source.Length || source[opening] != '(' || opening - delimiterStart > 16)
            return false;

        var delimiter = source.Substring(delimiterStart, opening - delimiterStart);
        var terminator = ")" + delimiter + "\"";
        var closing = source.IndexOf(terminator, opening + 1, StringComparison.Ordinal);
        if (closing < 0)
            return false;
        end = closing + terminator.Length - 1;
        return true;
    }
}
