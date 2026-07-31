using System.Globalization;
using System.Text;

namespace PowerForge.Web;

/// <summary>Encodes URL characters that are delimiters in an HTML srcset value.</summary>
internal static class SrcSetUrlEncoder
{
    internal static string Encode(string href)
    {
        var encoded = new StringBuilder(href.Length);
        for (var index = 0; index < href.Length; index++)
        {
            var character = href[index];
            if (character != ',' && !char.IsWhiteSpace(character))
            {
                encoded.Append(character);
                continue;
            }

            var scalarLength = char.IsHighSurrogate(character) &&
                               index + 1 < href.Length &&
                               char.IsLowSurrogate(href[index + 1])
                ? 2
                : 1;
            var bytes = Encoding.UTF8.GetBytes(href.Substring(index, scalarLength));
            foreach (var value in bytes)
                encoded.Append('%').Append(value.ToString("X2", CultureInfo.InvariantCulture));
            index += scalarLength - 1;
        }
        return encoded.ToString();
    }
}
