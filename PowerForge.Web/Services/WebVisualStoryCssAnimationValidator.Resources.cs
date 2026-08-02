using System.Text;

namespace PowerForge.Web;

internal static partial class WebVisualStoryCssAnimationValidator
{
    internal static bool ContainsExternalResourceReference(string css)
    {
        var normalizedCss = DecodeCssEscapes(RemoveComments(css));
        var quote = '\0';
        var escaped = false;
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
            if ((IsCssFunctionAt(normalizedCss, index, "image-set") ||
                 IsCssFunctionAt(normalizedCss, index, "-webkit-image-set")) &&
                ContainsExternalStringFunctionArgument(normalizedCss, index))
            {
                return true;
            }
            if (character == '@' &&
                IsAtRuleBoundary(normalizedCss, index) &&
                StartsWithCssKeyword(normalizedCss, index, "@import"))
            {
                var importCursor = index + 7;
                while (importCursor < normalizedCss.Length && char.IsWhiteSpace(normalizedCss[importCursor]))
                    importCursor++;
                if (importCursor < normalizedCss.Length && normalizedCss[importCursor] is '\'' or '"')
                    return true;
            }
            if (!StartsWithCssKeyword(normalizedCss, index, "url"))
                continue;
            var cursor = index + 3;
            while (cursor < normalizedCss.Length && char.IsWhiteSpace(normalizedCss[cursor]))
                cursor++;
            if (cursor >= normalizedCss.Length || normalizedCss[cursor] != '(')
                continue;
            cursor++;
            while (cursor < normalizedCss.Length && char.IsWhiteSpace(normalizedCss[cursor]))
                cursor++;
            var valueQuote = cursor < normalizedCss.Length && normalizedCss[cursor] is '\'' or '"'
                ? normalizedCss[cursor++]
                : '\0';
            var value = new StringBuilder();
            var valueEscaped = false;
            while (cursor < normalizedCss.Length)
            {
                character = normalizedCss[cursor++];
                if (valueEscaped)
                {
                    value.Append(character);
                    valueEscaped = false;
                    continue;
                }
                if (character == '\\')
                {
                    valueEscaped = true;
                    value.Append(character);
                    continue;
                }
                if (valueQuote != '\0')
                {
                    if (character == valueQuote)
                        break;
                    value.Append(character);
                    continue;
                }
                if (character == ')')
                    break;
                value.Append(character);
            }
            var reference = DecodeCssEscapes(value.ToString()).Trim();
            if (reference.Length > 0 && reference[0] != '#')
                return true;
        }
        return false;
    }

    private static bool IsCssFunctionAt(string css, int index, string name)
    {
        if (index > 0 && IsCssIdentifierCharacter(css[index - 1]))
            return false;
        if (!StartsWithCssKeyword(css, index, name))
            return false;
        var cursor = index + name.Length;
        while (cursor < css.Length && char.IsWhiteSpace(css[cursor]))
            cursor++;
        return cursor < css.Length && css[cursor] == '(';
    }

    private static bool ContainsExternalStringFunctionArgument(string css, int functionStart)
    {
        var cursor = css.IndexOf('(', functionStart);
        if (cursor < 0)
            return false;
        var depth = 1;
        for (cursor++; cursor < css.Length && depth > 0; cursor++)
        {
            var character = css[cursor];
            if (character == '(')
            {
                depth++;
                continue;
            }
            if (character == ')')
            {
                depth--;
                continue;
            }
            if (character is not ('\'' or '"'))
                continue;

            var quote = character;
            var value = new StringBuilder();
            var escaped = false;
            for (cursor++; cursor < css.Length; cursor++)
            {
                character = css[cursor];
                if (escaped)
                {
                    value.Append(character);
                    escaped = false;
                    continue;
                }
                if (character == '\\')
                {
                    value.Append(character);
                    escaped = true;
                    continue;
                }
                if (character == quote)
                    break;
                value.Append(character);
            }
            var reference = DecodeCssEscapes(value.ToString()).Trim();
            if (reference.Length > 0 &&
                reference[0] != '#' &&
                !reference.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
