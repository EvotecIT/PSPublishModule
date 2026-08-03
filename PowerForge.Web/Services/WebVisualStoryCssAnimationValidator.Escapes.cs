using System.Text;

namespace PowerForge.Web;

internal static partial class WebVisualStoryCssAnimationValidator
{
    private static string DecodeCssEscapes(string css)
    {
        var decoded = new StringBuilder(css.Length);
        var quote = '\0';
        for (var index = 0; index < css.Length; index++)
        {
            var character = css[index];
            if (quote != '\0')
            {
                decoded.Append(character);
                if (character == '\\' && index + 1 < css.Length)
                    decoded.Append(css[++index]);
                else if (character == quote)
                    quote = '\0';
                continue;
            }
            if (character is '\'' or '"')
            {
                quote = character;
                decoded.Append(character);
                continue;
            }
            if (character != '\\' || index + 1 >= css.Length)
            {
                decoded.Append(character);
                continue;
            }

            var cursor = index + 1;
            var codePoint = 0;
            var digits = 0;
            while (cursor < css.Length && digits < 6 && IsHexDigit(css[cursor]))
            {
                codePoint = checked(codePoint * 16 + HexValue(css[cursor]));
                cursor++;
                digits++;
            }
            if (digits > 0)
            {
                if (cursor < css.Length && char.IsWhiteSpace(css[cursor]))
                {
                    if (css[cursor] == '\r' && cursor + 1 < css.Length && css[cursor + 1] == '\n')
                        cursor++;
                    cursor++;
                }
                if (codePoint == 0 || codePoint > 0x10FFFF || codePoint is >= 0xD800 and <= 0xDFFF)
                    decoded.Append('\uFFFD');
                else
                    decoded.Append(char.ConvertFromUtf32(codePoint));
                index = cursor - 1;
                continue;
            }

            if (css[cursor] is '\r' or '\n' or '\f')
            {
                if (css[cursor] == '\r' && cursor + 1 < css.Length && css[cursor + 1] == '\n')
                    cursor++;
                index = cursor;
                continue;
            }
            decoded.Append(css[cursor]);
            index = cursor;
        }
        return decoded.ToString();
    }

    private static bool IsHexDigit(char character)
        => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private static int HexValue(char character)
        => character <= '9'
            ? character - '0'
            : char.ToUpperInvariant(character) - 'A' + 10;
}
