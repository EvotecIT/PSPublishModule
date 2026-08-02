using System.Globalization;
using System.Text;

namespace PowerForge.Web;

/// <summary>Recognizes effective CSS animations without executing or rendering SVG content.</summary>
internal static class WebVisualStoryCssAnimationValidator
{
    private readonly record struct AnimationDefinition(string? Name, double DurationMilliseconds, bool Paused);

    private static readonly HashSet<string> AnimationKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "none", "infinite", "normal", "reverse", "alternate", "alternate-reverse",
        "forwards", "backwards", "both", "running", "paused", "ease", "ease-in",
        "ease-out", "ease-in-out", "linear", "step-start", "step-end", "initial",
        "inherit", "unset", "revert", "revert-layer"
    };

    internal static IReadOnlySet<string> GetEffectiveAnimationNames(string css)
    {
        var normalizedCss = RemoveComments(css);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declarationBlock in GetDeclarationBlocks(normalizedCss))
        {
            foreach (var name in GetEffectiveAnimationNamesFromDeclaration(declarationBlock))
                names.Add(name);
        }
        return names;
    }

    internal static IReadOnlySet<string> GetKeyframeNames(string css)
    {
        var normalizedCss = RemoveComments(css);
        var names = new HashSet<string>(StringComparer.Ordinal);
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
            if (cursor >= normalizedCss.Length)
                continue;
            string name;
            if (normalizedCss[cursor] is '\'' or '"')
            {
                var nameQuote = normalizedCss[cursor++];
                var nameStart = cursor;
                while (cursor < normalizedCss.Length && normalizedCss[cursor] != nameQuote)
                    cursor++;
                if (cursor == nameStart || cursor >= normalizedCss.Length)
                    continue;
                name = normalizedCss.Substring(nameStart, cursor - nameStart);
                cursor++;
            }
            else
            {
                var nameStart = cursor;
                while (cursor < normalizedCss.Length && IsCssIdentifierCharacter(normalizedCss[cursor]))
                    cursor++;
                if (cursor == nameStart)
                    continue;
                name = normalizedCss.Substring(nameStart, cursor - nameStart);
            }
            while (cursor < normalizedCss.Length && char.IsWhiteSpace(normalizedCss[cursor]))
                cursor++;
            if (cursor < normalizedCss.Length && normalizedCss[cursor] == '{')
                names.Add(name);
        }
        return names;
    }

    internal static bool ContainsExternalResourceReference(string css)
    {
        var normalizedCss = RemoveComments(css);
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
            var reference = value.ToString().Trim();
            if (reference.Length > 0 && reference[0] != '#')
                return true;
        }
        return false;
    }

    private static IReadOnlyList<string> GetEffectiveAnimationNamesFromDeclaration(string declarations)
    {
        AnimationDefinition[] shorthand = [new(null, 0, false)];
        string?[]? names = null;
        double[]? durations = null;
        bool[]? playStates = null;
        foreach (var declaration in ParseDeclarations(declarations))
        {
            switch (declaration.Property.ToLowerInvariant())
            {
                case "animation":
                    shorthand = SplitTopLevel(declaration.Value, ',')
                        .Select(ParseShorthand)
                        .ToArray();
                    names = null;
                    durations = null;
                    playStates = null;
                    break;
                case "animation-name":
                    names = SplitTopLevel(declaration.Value, ',')
                        .Select(NormalizeAnimationName)
                        .ToArray();
                    break;
                case "animation-duration":
                    durations = SplitTopLevel(declaration.Value, ',')
                        .Select(static value =>
                            TryParseCssTime(value.Trim(), out var milliseconds) ? milliseconds : 0)
                        .ToArray();
                    break;
                case "animation-play-state":
                    playStates = SplitTopLevel(declaration.Value, ',')
                        .Select(static value =>
                            string.Equals(value.Trim(), "paused", StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    break;
            }
        }

        var count = Math.Max(
            shorthand.Length,
            Math.Max(names?.Length ?? 0, Math.Max(durations?.Length ?? 0, playStates?.Length ?? 0)));
        var effectiveNames = new List<string>();
        for (var index = 0; index < count; index++)
        {
            var basis = shorthand[index % shorthand.Length];
            var name = names is { Length: > 0 } ? names[index % names.Length] : basis.Name;
            var duration = durations is { Length: > 0 }
                ? durations[index % durations.Length]
                : basis.DurationMilliseconds;
            var paused = playStates is { Length: > 0 }
                ? playStates[index % playStates.Length]
                : basis.Paused;
            if (name is not null && duration > 0 && !paused)
                effectiveNames.Add(name);
        }
        return effectiveNames;
    }

    private static AnimationDefinition ParseShorthand(string shorthand)
    {
        var tokens = SplitTopLevelWhitespace(shorthand);
        var hasPositiveDuration = false;
        var sawDuration = false;
        string? name = null;
        var paused = false;
        foreach (var token in tokens)
        {
            if (TryParseCssTime(token, out var milliseconds))
            {
                if (!sawDuration)
                {
                    sawDuration = true;
                    hasPositiveDuration = milliseconds > 0;
                }
                continue;
            }

            if (string.Equals(token, "paused", StringComparison.OrdinalIgnoreCase))
                paused = true;
            else if (NormalizeAnimationName(token) is { } animationName)
                name = animationName;
        }
        return new AnimationDefinition(name, hasPositiveDuration ? 1 : 0, paused);
    }

    private static string? NormalizeAnimationName(string value)
    {
        var token = value.Trim();
        if (token.Length == 0 || AnimationKeywords.Contains(token))
            return null;
        if ((token[0] is '\'' or '"') && token.Length > 1 && token[^1] == token[0])
        {
            var quotedName = token.Substring(1, token.Length - 2);
            return string.Equals(quotedName, "none", StringComparison.OrdinalIgnoreCase) ? null : quotedName;
        }
        if (token.IndexOf('(') >= 0 || double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            return null;
        return TryParseCssTime(token, out _) ? null : token;
    }

    private static bool TryParseCssTime(string value, out double milliseconds)
    {
        milliseconds = 0;
        var token = value.Trim();
        var multiplier = 0d;
        var numberLength = 0;
        if (token.EndsWith("ms", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1;
            numberLength = token.Length - 2;
        }
        else if (token.EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1000;
            numberLength = token.Length - 1;
        }
        if (numberLength <= 0 ||
            !double.TryParse(token.Substring(0, numberLength), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
            !double.IsFinite(number))
            return false;
        milliseconds = number * multiplier;
        return true;
    }

    private static IReadOnlyList<(string Property, string Value)> ParseDeclarations(string declarations)
    {
        var properties = new List<(string Property, string Value)>();
        foreach (var declaration in SplitTopLevel(declarations, ';'))
        {
            var separator = FindTopLevelSeparator(declaration, ':');
            if (separator <= 0)
                continue;
            var property = declaration.Substring(0, separator).Trim();
            var value = TrimImportant(declaration.Substring(separator + 1).Trim());
            if (property.Length > 0 && value.Length > 0)
                properties.Add((property, value));
        }
        return properties;
    }

    private static string TrimImportant(string value)
    {
        const string important = "!important";
        return value.EndsWith(important, StringComparison.OrdinalIgnoreCase)
            ? value.Substring(0, value.Length - important.Length).TrimEnd()
            : value;
    }

    private static string RemoveComments(string css)
    {
        var normalized = new StringBuilder(css.Length);
        var quote = '\0';
        var escaped = false;
        for (var index = 0; index < css.Length; index++)
        {
            var character = css[index];
            var next = index + 1 < css.Length ? css[index + 1] : '\0';
            if (quote != '\0')
            {
                normalized.Append(character);
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
                normalized.Append(character);
                continue;
            }
            if (character == '/' && next == '*')
            {
                normalized.Append(' ');
                index += 2;
                while (index < css.Length && !(css[index - 1] == '*' && css[index] == '/'))
                    index++;
                continue;
            }
            normalized.Append(character);
        }
        return normalized.ToString();
    }

    private static bool StartsWithCssKeyword(string css, int index, string keyword)
    {
        if (index + keyword.Length > css.Length ||
            !string.Equals(css.Substring(index, keyword.Length), keyword, StringComparison.OrdinalIgnoreCase))
            return false;
        var after = index + keyword.Length;
        return after == css.Length || !IsCssIdentifierCharacter(css[after]);
    }

    private static bool IsAtRuleBoundary(string css, int index)
    {
        for (var cursor = index - 1; cursor >= 0; cursor--)
        {
            if (char.IsWhiteSpace(css[cursor]))
                continue;
            return css[cursor] is '{' or '}' or ';';
        }
        return true;
    }

    private static bool IsCssIdentifierCharacter(char value)
        => char.IsLetterOrDigit(value) || value is '-' or '_' or '\\';

    private static IReadOnlyList<string> GetDeclarationBlocks(string css)
    {
        var blocks = new List<string>();
        var stack = new List<(int Start, bool HasNestedBlock)>();
        var sawBlock = false;
        var quote = '\0';
        var escaped = false;
        var inComment = false;
        var parentheses = 0;
        for (var index = 0; index < css.Length; index++)
        {
            var value = css[index];
            var next = index + 1 < css.Length ? css[index + 1] : '\0';
            if (inComment)
            {
                if (value == '*' && next == '/')
                {
                    inComment = false;
                    index++;
                }
                continue;
            }
            if (quote != '\0')
            {
                if (escaped)
                    escaped = false;
                else if (value == '\\')
                    escaped = true;
                else if (value == quote)
                    quote = '\0';
                continue;
            }
            if (value == '/' && next == '*')
            {
                inComment = true;
                index++;
                continue;
            }
            if (value is '\'' or '"')
            {
                quote = value;
                continue;
            }
            if (value == '(')
            {
                parentheses++;
                continue;
            }
            if (value == ')' && parentheses > 0)
            {
                parentheses--;
                continue;
            }
            if (parentheses > 0)
                continue;
            if (value == '{')
            {
                sawBlock = true;
                if (stack.Count > 0)
                {
                    var parent = stack[^1];
                    stack[^1] = (parent.Start, true);
                }
                stack.Add((index + 1, false));
            }
            else if (value == '}' && stack.Count > 0)
            {
                var block = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                if (!block.HasNestedBlock)
                    blocks.Add(css.Substring(block.Start, index - block.Start));
            }
        }
        if (!sawBlock)
            blocks.Add(css);
        return blocks;
    }

    private static IReadOnlyList<string> SplitTopLevelWhitespace(string value)
    {
        var tokens = new List<string>();
        var start = -1;
        var quote = '\0';
        var escaped = false;
        var parentheses = 0;
        for (var index = 0; index <= value.Length; index++)
        {
            var character = index < value.Length ? value[index] : ' ';
            if (quote != '\0')
            {
                if (escaped)
                    escaped = false;
                else if (character == '\\')
                    escaped = true;
                else if (character == quote)
                    quote = '\0';
            }
            else if (character is '\'' or '"')
                quote = character;
            else if (character == '(')
                parentheses++;
            else if (character == ')' && parentheses > 0)
                parentheses--;

            if (char.IsWhiteSpace(character) && quote == '\0' && parentheses == 0)
            {
                if (start >= 0)
                {
                    tokens.Add(value.Substring(start, index - start));
                    start = -1;
                }
            }
            else if (start < 0)
                start = index;
        }
        return tokens;
    }

    private static IReadOnlyList<string> SplitTopLevel(string value, char separator)
    {
        var parts = new List<string>();
        var start = 0;
        var quote = '\0';
        var escaped = false;
        var inComment = false;
        var parentheses = 0;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var next = index + 1 < value.Length ? value[index + 1] : '\0';
            if (inComment)
            {
                if (character == '*' && next == '/')
                {
                    inComment = false;
                    index++;
                }
                continue;
            }
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
            if (character == '/' && next == '*')
            {
                inComment = true;
                index++;
                continue;
            }
            if (character is '\'' or '"')
                quote = character;
            else if (character == '(')
                parentheses++;
            else if (character == ')' && parentheses > 0)
                parentheses--;
            else if (character == separator && parentheses == 0)
            {
                parts.Add(value.Substring(start, index - start).Trim());
                start = index + 1;
            }
        }
        parts.Add(value.Substring(start).Trim());
        return parts;
    }

    private static int FindTopLevelSeparator(string value, char separator)
    {
        var quote = '\0';
        var escaped = false;
        var inComment = false;
        var parentheses = 0;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var next = index + 1 < value.Length ? value[index + 1] : '\0';
            if (inComment)
            {
                if (character == '*' && next == '/')
                {
                    inComment = false;
                    index++;
                }
                continue;
            }
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
            if (character == '/' && next == '*')
            {
                inComment = true;
                index++;
                continue;
            }
            if (character is '\'' or '"')
                quote = character;
            else if (character == '(')
                parentheses++;
            else if (character == ')' && parentheses > 0)
                parentheses--;
            else if (character == separator && parentheses == 0)
                return index;
        }
        return -1;
    }
}
