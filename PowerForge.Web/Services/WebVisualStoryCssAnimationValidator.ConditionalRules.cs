using System.Text;

namespace PowerForge.Web;

internal static partial class WebVisualStoryCssAnimationValidator
{
    private static readonly string[] UnsupportedConditionalAtRules =
    [
        "@media",
        "@supports",
        "@container",
        "@document",
        "@-moz-document",
        "@scope",
        "@starting-style"
    ];

    private static string RemoveUnsupportedConditionalRuleBlocks(string css)
    {
        var sanitized = new StringBuilder(css);
        var quote = '\0';
        var escaped = false;
        for (var index = 0; index < css.Length; index++)
        {
            var character = css[index];
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
            if (character != '@' || !IsAtRuleBoundary(css, index))
                continue;

            var keyword = UnsupportedConditionalAtRules.FirstOrDefault(
                rule => StartsWithCssKeyword(css, index, rule));
            if (keyword is null)
                continue;
            var blockStart = FindConditionalBlockStart(css, index + keyword.Length);
            if (blockStart < 0)
                continue;
            var blockEnd = FindConditionalBlockEnd(css, blockStart);
            if (blockEnd < 0)
                blockEnd = css.Length - 1;
            for (var cursor = index; cursor <= blockEnd; cursor++)
                sanitized[cursor] = ' ';
            index = blockEnd;
        }
        return sanitized.ToString();
    }

    private static int FindConditionalBlockStart(string css, int start)
    {
        var quote = '\0';
        var escaped = false;
        var parentheses = 0;
        for (var index = start; index < css.Length; index++)
        {
            var character = css[index];
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
                quote = character;
            else if (character == '(')
                parentheses++;
            else if (character == ')' && parentheses > 0)
                parentheses--;
            else if (character == '{' && parentheses == 0)
                return index;
            else if (character == ';' && parentheses == 0)
                return -1;
        }
        return -1;
    }

    private static int FindConditionalBlockEnd(string css, int blockStart)
    {
        var depth = 1;
        var quote = '\0';
        var escaped = false;
        for (var index = blockStart + 1; index < css.Length; index++)
        {
            var character = css[index];
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
                quote = character;
            else if (character == '{')
                depth++;
            else if (character == '}' && --depth == 0)
                return index;
        }
        return -1;
    }
}
