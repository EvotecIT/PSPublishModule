using System.Text.RegularExpressions;

namespace PowerForge;

internal static class ModuleManifestTextParser
{
    private const RegexOptions ManifestRegexOptions =
        RegexOptions.IgnoreCase |
        RegexOptions.Multiline |
        RegexOptions.CultureInvariant |
        RegexOptions.Compiled;

    internal static bool TryGetQuotedStringValue(string manifestText, string key, out string? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(manifestText) || string.IsNullOrWhiteSpace(key))
            return false;

        var match = Regex.Match(
            manifestText,
            $@"(?:^|[\r\n{{;])\s*{Regex.Escape(key)}\s*=\s*(?<value>'(?:[^']|'')*'|""(?:[^""]|"""")*"")",
            ManifestRegexOptions);
        if (!match.Success)
            return false;

        value = Unquote(match.Groups["value"].Value);
        return !string.IsNullOrWhiteSpace(value);
    }

    internal static bool TryGetPsDataStringValue(string manifestText, string key, out string? value)
    {
        value = null;
        if (!TryReadAssignedExpressionByKey(manifestText, "PrivateData", out var privateData) ||
            string.IsNullOrWhiteSpace(privateData))
            return false;

        var privateDataText = privateData!;

        if (!TryReadAssignedExpressionByKey(privateDataText, "PSData", out var psData) ||
            string.IsNullOrWhiteSpace(psData))
            return false;

        return TryGetHashtableStringValue(psData!, key, out value);
    }

    internal static bool TryGetRequiredModules(string manifestText, out RequiredModuleReference[]? modules)
    {
        modules = null;
        if (!TryReadAssignedExpressionByKey(manifestText, "RequiredModules", out var expression) ||
            string.IsNullOrWhiteSpace(expression))
            return false;

        var parsed = ParseRequiredModules(expression!)
            .Where(static module => module is not null)
            .Cast<RequiredModuleReference>()
            .ToArray();

        modules = parsed;
        return true;
    }

    internal static bool TryGetStringArrayValue(string manifestText, string key, out string[]? values)
    {
        values = null;
        if (!TryReadAssignedExpressionByKey(manifestText, key, out var expression) ||
            string.IsNullOrWhiteSpace(expression))
            return false;

        var parsed = ParseStringArray(expression!)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        values = parsed;
        return true;
    }

    private static IEnumerable<RequiredModuleReference?> ParseRequiredModules(string expression)
    {
        var trimmed = expression.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            yield break;

        if (IsArrayExpression(trimmed))
        {
            var body = TrimCompositeWrapper(trimmed);
            var index = 0;
            while (TryReadValueExpression(body, ref index, out var itemExpression))
            {
                var module = ParseRequiredModuleItem(itemExpression);
                if (module is not null)
                    yield return module;
            }

            yield break;
        }

        yield return ParseRequiredModuleItem(trimmed);
    }

    private static RequiredModuleReference? ParseRequiredModuleItem(string expression)
    {
        var trimmed = expression.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        if (TryUnquote(trimmed, out var moduleName) && !string.IsNullOrWhiteSpace(moduleName))
            return new RequiredModuleReference(moduleName);

        if (!IsHashtableExpression(trimmed))
            return null;

        if (!TryGetHashtableStringValue(trimmed, "ModuleName", out var name) || string.IsNullOrWhiteSpace(name))
            return null;

        TryGetHashtableStringValue(trimmed, "ModuleVersion", out var moduleVersion);
        TryGetHashtableStringValue(trimmed, "RequiredVersion", out var requiredVersion);
        TryGetHashtableStringValue(trimmed, "MaximumVersion", out var maximumVersion);
        TryGetHashtableStringValue(trimmed, "Guid", out var guid);

        return new RequiredModuleReference(name!, moduleVersion, requiredVersion, maximumVersion, guid);
    }

    private static IEnumerable<string> ParseStringArray(string expression)
    {
        var trimmed = expression.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            yield break;

        if (TryUnquote(trimmed, out var singleValue) && !string.IsNullOrWhiteSpace(singleValue))
        {
            yield return singleValue;
            yield break;
        }

        if (!IsArrayExpression(trimmed))
            yield break;

        var body = TrimCompositeWrapper(trimmed);
        var index = 0;
        while (TryReadValueExpression(body, ref index, out var itemExpression))
        {
            if (TryUnquote(itemExpression, out var value) && !string.IsNullOrWhiteSpace(value))
                yield return value;
        }
    }

    private static bool TryGetHashtableStringValue(string hashtableExpression, string key, out string? value)
    {
        var body = TrimCompositeWrapper(hashtableExpression);
        return TryGetQuotedStringValue(body, key, out value);
    }

    private static bool TryReadAssignedExpressionByKey(string text, string key, out string? expression)
    {
        expression = null;
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(key))
            return false;

        var match = Regex.Match(
            text,
            $@"(?:^|[\r\n{{;])\s*{Regex.Escape(key)}\s*=",
            ManifestRegexOptions);
        if (!match.Success)
            return false;

        var index = match.Index + match.Length;
        expression = ReadValueExpression(text, ref index);
        return !string.IsNullOrWhiteSpace(expression);
    }

    private static bool TryReadValueExpression(string text, ref int index, out string expression)
    {
        expression = ReadValueExpression(text, ref index);
        return !string.IsNullOrWhiteSpace(expression);
    }

    private static string ReadValueExpression(string text, ref int index)
    {
        index = SkipTrivia(text, index, treatCommasAsTrivia: true);
        if (index >= text.Length)
            return string.Empty;

        if (TryReadQuotedString(text, index, out var quotedEnd))
        {
            var expression = text.Substring(index, quotedEnd - index);
            index = quotedEnd;
            return expression.Trim();
        }

        if (TryReadComposite(text, index, out var compositeEnd))
        {
            var expression = text.Substring(index, compositeEnd - index);
            index = compositeEnd;
            return expression.Trim();
        }

        var end = index;
        while (end < text.Length)
        {
            var ch = text[end];
            if (ch == ',' || ch == ';' || ch == '\r' || ch == '\n')
                break;
            end++;
        }

        var result = text.Substring(index, end - index).Trim();
        index = end;
        return result;
    }

    private static bool TryReadQuotedString(string text, int start, out int endExclusive)
    {
        endExclusive = start;
        if (start >= text.Length)
            return false;

        var quote = text[start];
        if (quote != '\'' && quote != '"')
            return false;

        for (var i = start + 1; i < text.Length; i++)
        {
            if (text[i] != quote)
                continue;

            if (i + 1 < text.Length && text[i + 1] == quote)
            {
                i++;
                continue;
            }

            endExclusive = i + 1;
            return true;
        }

        return false;
    }

    private static bool TryReadComposite(string text, int start, out int endExclusive)
    {
        endExclusive = start;
        if (!TryGetCompositeStart(text, start, out var currentIndex, out var firstCloser))
            return false;

        var stack = new Stack<char>();
        stack.Push(firstCloser);

        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var i = currentIndex; i < text.Length; i++)
        {
            var ch = text[i];

            if (inSingleQuote)
            {
                if (ch == '\'' && !(i + 1 < text.Length && text[i + 1] == '\''))
                    inSingleQuote = false;
                else if (ch == '\'' && i + 1 < text.Length && text[i + 1] == '\'')
                    i++;

                continue;
            }

            if (inDoubleQuote)
            {
                if (ch == '"' && !(i + 1 < text.Length && text[i + 1] == '"'))
                    inDoubleQuote = false;
                else if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"')
                    i++;

                continue;
            }

            if (ch == '#')
            {
                while (i < text.Length && text[i] != '\r' && text[i] != '\n')
                    i++;
                i--;
                continue;
            }

            if (ch == '\'')
            {
                inSingleQuote = true;
                continue;
            }

            if (ch == '"')
            {
                inDoubleQuote = true;
                continue;
            }

            if (TryGetCompositeStart(text, i, out var nestedIndex, out var nestedCloser))
            {
                stack.Push(nestedCloser);
                i = nestedIndex;
                continue;
            }

            if (stack.Count > 0 && ch == stack.Peek())
            {
                stack.Pop();
                if (stack.Count == 0)
                {
                    endExclusive = i + 1;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryGetCompositeStart(string text, int index, out int currentIndex, out char closer)
    {
        currentIndex = index;
        closer = '\0';

        if (index >= text.Length)
            return false;

        if (text[index] == '@' && index + 1 < text.Length)
        {
            if (text[index + 1] == '(')
            {
                currentIndex = index + 2;
                closer = ')';
                return true;
            }

            if (text[index + 1] == '{')
            {
                currentIndex = index + 2;
                closer = '}';
                return true;
            }
        }

        if (text[index] == '(')
        {
            currentIndex = index + 1;
            closer = ')';
            return true;
        }

        if (text[index] == '{')
        {
            currentIndex = index + 1;
            closer = '}';
            return true;
        }

        return false;
    }

    private static int SkipTrivia(string text, int index, bool treatCommasAsTrivia)
    {
        while (index < text.Length)
        {
            var ch = text[index];
            if (char.IsWhiteSpace(ch) || ch == ';' || (treatCommasAsTrivia && ch == ','))
            {
                index++;
                continue;
            }

            if (ch == '#')
            {
                while (index < text.Length && text[index] != '\r' && text[index] != '\n')
                    index++;
                continue;
            }

            break;
        }

        return index;
    }

    private static bool IsArrayExpression(string expression)
        => expression.StartsWith("@(", StringComparison.Ordinal) || expression.StartsWith("(", StringComparison.Ordinal);

    private static bool IsHashtableExpression(string expression)
        => expression.StartsWith("@{", StringComparison.Ordinal) || expression.StartsWith("{", StringComparison.Ordinal);

    private static string TrimCompositeWrapper(string expression)
    {
        var trimmed = expression.Trim();
        if (trimmed.StartsWith("@(", StringComparison.Ordinal) && trimmed.EndsWith(")", StringComparison.Ordinal))
            return trimmed.Substring(2, trimmed.Length - 3);
        if (trimmed.StartsWith("(", StringComparison.Ordinal) && trimmed.EndsWith(")", StringComparison.Ordinal))
            return trimmed.Substring(1, trimmed.Length - 2);
        if (trimmed.StartsWith("@{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal))
            return trimmed.Substring(2, trimmed.Length - 3);
        if (trimmed.StartsWith("{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal))
            return trimmed.Substring(1, trimmed.Length - 2);
        return trimmed;
    }

    private static string Unquote(string value)
        => TryUnquote(value, out var unquoted) ? unquoted : value.Trim();

    private static bool TryUnquote(string value, out string unquoted)
    {
        unquoted = value.Trim();
        if (unquoted.Length < 2)
            return false;

        var quote = unquoted[0];
        if ((quote != '\'' && quote != '"') || unquoted[unquoted.Length - 1] != quote)
            return false;

        unquoted = unquoted.Substring(1, unquoted.Length - 2)
            .Replace(new string(quote, 2), quote.ToString());
        return true;
    }
}
