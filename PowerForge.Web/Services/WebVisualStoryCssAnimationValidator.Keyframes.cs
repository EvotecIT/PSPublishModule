namespace PowerForge.Web;

internal static partial class WebVisualStoryCssAnimationValidator
{
    internal static IReadOnlySet<string> GetKeyframeNames(string css)
    {
        var normalizedCss = RemoveUnsupportedConditionalRuleBlocks(RemoveComments(css));
        var definitions = new Dictionary<string, bool>(StringComparer.Ordinal);
        var quote = '\0';
        var escaped = false;
        var parentheses = 0;
        for (var index = 0; index < normalizedCss.Length; index++)
        {
            var character = normalizedCss[index];
            if (quote != '\0')
            {
                if (escaped)
                    escaped = false;
                else if (character == '\\')
                    escaped = true;
                else if (character == quote)
                    quote = '\0';
                continue;
            }
            if (character is '\'' or '"')
            {
                quote = character;
                continue;
            }
            if (character == '(')
            {
                parentheses++;
                continue;
            }
            if (character == ')' && parentheses > 0)
            {
                parentheses--;
                continue;
            }
            if (character != '@' || parentheses > 0 || !IsAtRuleBoundary(normalizedCss, index))
                continue;

            var keywordLength = StartsWithCssKeyword(normalizedCss, index, "@keyframes")
                ? 10
                : StartsWithCssKeyword(normalizedCss, index, "@-webkit-keyframes")
                    ? 18
                    : 0;
            if (keywordLength == 0)
                continue;

            var cursor = index + keywordLength;
            while (cursor < normalizedCss.Length && char.IsWhiteSpace(normalizedCss[cursor]))
                cursor++;
            if (!TryReadKeyframeName(normalizedCss, ref cursor, out var name))
                continue;
            while (cursor < normalizedCss.Length && char.IsWhiteSpace(normalizedCss[cursor]))
                cursor++;
            if (cursor >= normalizedCss.Length || normalizedCss[cursor] != '{')
                continue;

            var blockEnd = FindConditionalBlockEnd(normalizedCss, cursor);
            if (blockEnd < 0)
                continue;
            var body = normalizedCss.Substring(cursor + 1, blockEnd - cursor - 1);
            definitions[name] = KeyframesCanProduceMotion(body);
            index = blockEnd;
        }

        return definitions
            .Where(static definition => definition.Value)
            .Select(static definition => definition.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool TryReadKeyframeName(string css, ref int cursor, out string name)
    {
        name = string.Empty;
        if (cursor >= css.Length)
            return false;
        if (css[cursor] is '\'' or '"')
        {
            var nameQuote = css[cursor++];
            var nameStart = cursor;
            var escaped = false;
            while (cursor < css.Length)
            {
                var character = css[cursor];
                if (!escaped && character == nameQuote)
                    break;
                escaped = !escaped && character == '\\';
                if (character != '\\')
                    escaped = false;
                cursor++;
            }
            if (cursor == nameStart || cursor >= css.Length)
                return false;
            name = css.Substring(nameStart, cursor - nameStart);
            cursor++;
            return true;
        }

        var start = cursor;
        while (cursor < css.Length && IsCssIdentifierCharacter(css[cursor]))
            cursor++;
        if (cursor == start)
            return false;
        name = css.Substring(start, cursor - start);
        return true;
    }

    private static bool KeyframesCanProduceMotion(string body)
    {
        var rules = GetStyleRules(body);
        var signatures = rules
            .Select(static rule => CreateDeclarationSignature(rule.Declarations))
            .ToArray();
        if (signatures.Length == 0 || signatures.All(static signature => signature.Length == 0))
            return false;
        if (signatures.Length == 1)
            return !PinsBothAnimationEndpoints(rules[0].Selector);
        return signatures.Skip(1).Any(signature =>
            !string.Equals(signature, signatures[0], StringComparison.Ordinal));
    }

    private static bool PinsBothAnimationEndpoints(string selector)
    {
        var endpoints = selector.Split(',')
            .Select(static value => value.Trim())
            .Where(static value => value.Length > 0)
            .ToArray();
        var hasStart = endpoints.Any(static value =>
            string.Equals(value, "from", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "0%", StringComparison.Ordinal));
        var hasEnd = endpoints.Any(static value =>
            string.Equals(value, "to", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "100%", StringComparison.Ordinal));
        return hasStart && hasEnd;
    }

    private static string CreateDeclarationSignature(string declarations)
    {
        var effective = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in ParseDeclarations(declarations))
            effective[declaration.Property] = declaration.Value.Trim();
        return string.Join(
            ";",
            effective.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static pair => pair.Key.ToLowerInvariant() + ":" + pair.Value));
    }
}
