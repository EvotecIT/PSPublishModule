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
    /// Escapes percent markers and invalid UTF-16 code units so distinct raw
    /// identities cannot collapse to the same rendered value.
    /// </summary>
    internal static string Format(string text)
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
