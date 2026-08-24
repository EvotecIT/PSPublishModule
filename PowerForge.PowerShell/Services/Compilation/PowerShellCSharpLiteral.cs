using System.Globalization;
using System.Text;

namespace PowerForge;

internal static class PowerShellCSharpLiteral
{
    internal static string QuoteString(string value)
        => "\"" + EscapeStringContent(value) + "\"";

    internal static string QuoteChar(char value)
    {
        var escaped = new StringBuilder(8).Append('\'');
        AppendEscapedCharacter(escaped, value, escapeDoubleQuote: false);
        return escaped.Append('\'').ToString();
    }

    private static string EscapeStringContent(string value)
    {
        var escaped = new StringBuilder(value.Length);
        foreach (var character in value)
            AppendEscapedCharacter(escaped, character, escapeDoubleQuote: true);
        return escaped.ToString();
    }

    private static void AppendEscapedCharacter(StringBuilder builder, char value, bool escapeDoubleQuote)
    {
        var escape = value switch
        {
            '\\' => "\\\\",
            '"' when escapeDoubleQuote => "\\\"",
            '\'' when !escapeDoubleQuote => "\\'",
            '\0' => "\\0",
            '\a' => "\\a",
            '\b' => "\\b",
            '\f' => "\\f",
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            '\v' => "\\v",
            _ => null
        };
        if (escape is not null)
        {
            builder.Append(escape);
            return;
        }
        if (char.IsControl(value) || char.IsSurrogate(value) || value is '\u0085' or '\u2028' or '\u2029')
            builder.Append("\\u").Append(((int)value).ToString("X4", CultureInfo.InvariantCulture));
        else
            builder.Append(value);
    }
}
