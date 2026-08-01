using System;
using System.Globalization;
using System.Text;
using System.Xml;

namespace PowerForge;

/// <summary>
/// Renders identity-bearing documentation text as XML-safe, collision-free text.
/// </summary>
internal static class DocumentationIdentityTextFormatter
{
    /// <summary>
    /// Preserves a binding identity exactly when XML can represent it and rejects
    /// identities that external help cannot associate with the runtime command.
    /// </summary>
    internal static string PreserveBindable(string? text, string identityKind)
    {
        var value = text ?? string.Empty;
        if (IsXmlSafe(value)) return value;
        throw new InvalidOperationException(identityKind + " contains XML-invalid characters: " + value);
    }

    internal static bool IsXmlSafe(string? text)
    {
        try
        {
            XmlConvert.VerifyXmlChars(text ?? string.Empty);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    /// <summary>
    /// Escapes percent markers and invalid UTF-16 code units so distinct raw
    /// identities cannot collapse to the same rendered value.
    /// </summary>
    internal static string Format(string text)
        => Format(text, encodeLineBreaks: false);

    /// <summary>
    /// Escapes identity-significant CR/LF code units in addition to XML-invalid
    /// characters so output types that differ only by line breaks remain distinct.
    /// </summary>
    internal static string FormatOutputType(string text)
        => Format(text, encodeLineBreaks: true);

    private static string Format(string text, bool encodeLineBreaks)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var builder = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '%')
            {
                builder.Append("%25");
            }
            else if (encodeLineBreaks && (character == '\r' || character == '\n'))
            {
                builder.Append("%u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
            }
            else if (char.IsHighSurrogate(character) &&
                     index + 1 < text.Length &&
                     char.IsLowSurrogate(text[index + 1]))
            {
                builder.Append(character);
                builder.Append(text[++index]);
            }
            else if (XmlConvert.IsXmlChar(character))
            {
                builder.Append(character);
            }
            else
            {
                builder.Append("%u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
            }
        }
        return builder.ToString();
    }
}
