namespace PowerForge;

internal static class ModuleManifestTextParser
{
    internal static bool TryGetQuotedStringValue(string manifestText, string key, out string? value)
        => TryGetTopLevelQuotedStringValue(manifestText, key, out value);

    internal static bool TryGetTopLevelQuotedStringValue(string manifestText, string key, out string? value)
    {
        value = null;
        if (!TryReadTopLevelAssignedExpressionByKey(manifestText, key, out var expression) ||
            string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        return TryParseQuotedStringExpression(expression!, out value);
    }

    internal static bool TryGetPsDataStringValue(string manifestText, string key, out string? value)
    {
        value = null;
        if (!TryReadPsDataAssignedExpression(manifestText, key, out var expression) ||
            string.IsNullOrWhiteSpace(expression))
            return false;

        return TryParseQuotedStringExpression(expression!, out value);
    }

    internal static bool TryGetPsDataStringArrayValue(string manifestText, string key, out string[]? values)
    {
        values = null;
        if (!TryReadPsDataAssignedExpression(manifestText, key, out var expression) ||
            string.IsNullOrWhiteSpace(expression))
            return false;

        return TryParseStringArrayExpression(expression!, out values);
    }

    internal static bool TryGetRequiredModules(string manifestText, out RequiredModuleReference[]? modules)
    {
        modules = null;
        if (!TryReadTopLevelAssignedExpressionByKey(manifestText, "RequiredModules", out var expression) ||
            string.IsNullOrWhiteSpace(expression))
            return false;

        var parsed = ParseRequiredModules(expression!)
            .Where(static module => module is not null)
            .Cast<RequiredModuleReference>()
            .ToArray();

        modules = parsed;
        return true;
    }

    internal static bool TryReadPsDataAssignedExpression(string manifestText, string key, out string? expression)
    {
        expression = null;
        if (!TryReadTopLevelAssignedExpressionByKey(manifestText, "PrivateData", out var privateData) ||
            string.IsNullOrWhiteSpace(privateData))
            return false;

        var privateDataText = TrimCompositeWrapper(privateData!);
        if (!TryReadTopLevelAssignedExpressionByKey(privateDataText, "PSData", out var psData) ||
            string.IsNullOrWhiteSpace(psData))
            return false;

        var psDataText = TrimCompositeWrapper(psData!);
        return TryReadTopLevelAssignedExpressionByKey(psDataText, key, out expression);
    }

    internal static bool TryGetStringArrayValue(string manifestText, string key, out string[]? values)
    {
        values = null;
        if (!TryReadTopLevelAssignedExpressionByKey(manifestText, key, out var expression) ||
            string.IsNullOrWhiteSpace(expression))
            return false;

        var parsed = ParseStringArray(expression!)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        values = parsed;
        return true;
    }

    internal static bool TryGetStrictStringArrayValue(string manifestText, string key, out string[]? values)
    {
        values = null;
        if (!TryReadTopLevelAssignedExpressionByKey(manifestText, key, out var expression) ||
            string.IsNullOrWhiteSpace(expression))
            return false;

        return TryParseStrictStringArrayExpression(expression!, out values);
    }

    internal static bool TryParseStringArrayExpression(string expression, out string[]? values)
    {
        values = null;
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        var parsed = ParseStringArray(expression)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        values = parsed;
        return true;
    }

    internal static bool TryParseStrictStringArrayExpression(string expression, out string[]? values)
    {
        values = null;
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        var trimmed = expression.Trim();
        var probeIndex = 0;
        if (TryReadValueExpression(trimmed, ref probeIndex, out var singleExpression) &&
            SkipTrivia(trimmed, probeIndex, treatCommasAsTrivia: true) == trimmed.Length &&
            TryUnquote(singleExpression, out var singleValue))
        {
            values = string.IsNullOrWhiteSpace(singleValue) ? Array.Empty<string>() : new[] { singleValue };
            return true;
        }
        var parsed = new List<string>();
        var body = IsArrayExpression(trimmed) ? TrimCompositeWrapper(trimmed) : trimmed;
        var index = 0;
        while (TryReadValueExpression(body, ref index, out var itemExpression))
        {
            if (!TryUnquote(itemExpression, out var value))
                return false;

            if (!string.IsNullOrWhiteSpace(value))
                parsed.Add(value);
        }

        if (SkipTrivia(body, index, treatCommasAsTrivia: true) != body.Length ||
            parsed.Count == 0 && !IsArrayExpression(trimmed))
            return false;

        values = parsed.ToArray();
        return true;
    }

    internal static bool TryParseQuotedStringExpression(string expression, out string? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        if (!TryUnquote(expression, out var parsed) || string.IsNullOrWhiteSpace(parsed))
            return false;

        value = parsed;
        return true;
    }

    internal static bool TryParseBooleanExpression(string expression, out bool value)
    {
        value = false;
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        var trimmed = expression.Trim();
        if (trimmed.Equals("$true", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (trimmed.Equals("$false", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        if (TryUnquote(trimmed, out var unquoted) && bool.TryParse(unquoted, out value))
            return true;

        return false;
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

        var itemIndex = 0;
        while (TryReadValueExpression(trimmed, ref itemIndex, out var itemExpression))
        {
            var module = ParseRequiredModuleItem(itemExpression);
            if (module is not null)
                yield return module;
        }
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

    internal static bool TryParseModuleReferencePathExpression(string expression, out string[]? paths)
    {
        paths = null;
        var trimmed = expression?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return false;

        var body = IsArrayExpression(trimmed!)
            ? TrimCompositeWrapper(trimmed!)
            : trimmed!;
        var values = new List<string>();
        var index = 0;
        while (TryReadValueExpression(body, ref index, out var itemExpression))
        {
            RequiredModuleReference? module = ParseRequiredModuleItem(itemExpression);
            if (module is null || string.IsNullOrWhiteSpace(module.ModuleName))
                return false;
            values.Add(module.ModuleName.Trim());
        }

        paths = values.ToArray();
        return true;
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

        var body = IsArrayExpression(trimmed)
            ? TrimCompositeWrapper(trimmed)
            : trimmed;
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

    private static bool TryGetHashtableStringArrayValue(string hashtableExpression, string key, out string[]? values)
    {
        var body = TrimCompositeWrapper(hashtableExpression);
        return TryGetStringArrayValue(body, key, out values);
    }

    internal static bool TryReadTopLevelAssignedExpressionByKey(string text, string key, out string? expression)
    {
        expression = null;
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(key))
            return false;

        var body = TrimCompositeWrapper(text);
        var index = 0;
        while (index < body.Length)
        {
            index = SkipTrivia(body, index, treatCommasAsTrivia: false);
            if (index >= body.Length)
                break;

            string candidateKey;
            if (body[index] is '\'' or '"')
            {
                if (!TryReadQuotedString(body, index, out int quotedKeyEnd) ||
                    !TryUnquote(body.Substring(index, quotedKeyEnd - index), out candidateKey))
                {
                    index++;
                    continue;
                }
                index = quotedKeyEnd;
            }
            else
            {
                var keyStart = index;
                while (index < body.Length && IsManifestKeyCharacter(body[index]))
                    index++;
                if (keyStart == index)
                {
                    index++;
                    continue;
                }
                candidateKey = body.Substring(keyStart, index - keyStart);
            }

            index = SkipTrivia(body, index, treatCommasAsTrivia: false);
            if (index >= body.Length || body[index] != '=')
            {
                continue;
            }

            index++;
            var valueExpression = ReadAssignedValueExpression(body, ref index);
            if (candidateKey.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                expression = valueExpression;
                return !string.IsNullOrWhiteSpace(expression);
            }
        }

        return false;
    }

    private static bool IsManifestKeyCharacter(char value)
        => char.IsLetterOrDigit(value) || value == '_' || value == '-';

    private static string ReadAssignedValueExpression(string text, ref int index)
    {
        var parts = new List<string>();
        while (true)
        {
            var expression = ReadValueExpression(text, ref index);
            if (string.IsNullOrWhiteSpace(expression))
                break;

            parts.Add(expression.Trim());
            var next = SkipWhitespaceAndComments(text, index);
            if (next >= text.Length || text[next] != ',')
                break;

            index = next + 1;
        }

        return string.Join(", ", parts);
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

            if (ch == '<' && i + 1 < text.Length && text[i + 1] == '#')
            {
                i = SkipBlockComment(text, i) - 1;
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
                // The loop increment must land on the first character inside the nested
                // composite. Skipping that character can lose an opening quote and leave
                // the remaining manifest incorrectly treated as quoted text.
                i = nestedIndex - 1;
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

            if (ch == '<' && index + 1 < text.Length && text[index + 1] == '#')
            {
                index = SkipBlockComment(text, index);
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

    private static int SkipWhitespaceAndComments(string text, int index)
    {
        while (index < text.Length)
        {
            var ch = text[index];
            if (char.IsWhiteSpace(ch))
            {
                index++;
                continue;
            }

            if (ch == '<' && index + 1 < text.Length && text[index + 1] == '#')
            {
                index = SkipBlockComment(text, index);
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

    private static int SkipBlockComment(string text, int index)
    {
        int end = text.IndexOf("#>", index + 2, StringComparison.Ordinal);
        return end < 0 ? text.Length : end + 2;
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
