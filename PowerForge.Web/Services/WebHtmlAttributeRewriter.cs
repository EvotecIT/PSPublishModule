using System.Text;
using System.Net;
using HtmlAgilityPack;
using HtmlTinkerX;

namespace PowerForge.Web;

/// <summary>Rewrites parsed attribute values without reserializing page content.</summary>
internal static class WebHtmlAttributeRewriter
{
    /// <summary>Updates actual element attributes and inserts missing hints while preserving surrounding source.</summary>
    internal static string RewriteElements(string html, Func<HtmlNode, IReadOnlyDictionary<string, string>?> rewrite)
    {
        var document = HtmlParser.ParseWithHtmlAgilityPack(html);
        var replacements = new List<(int Start, int Length, string Value)>();
        foreach (var node in document.DocumentNode.Descendants())
        {
            if (node.NodeType != HtmlNodeType.Element) continue;
            var attributes = rewrite(node);
            if (attributes is null || attributes.Count == 0) continue;
            var tagEnd = FindOpeningTagEnd(html, node.StreamPosition);
            if (tagEnd < 0) continue;
            var additions = new StringBuilder();
            foreach (var (name, value) in attributes)
            {
                var encoded = WebUtility.HtmlEncode(value);
                var attribute = node.Attributes[name];
                if (attribute is null)
                {
                    additions.Append(' ').Append(name).Append("=\"").Append(encoded).Append('"');
                    continue;
                }
                var start = attribute.ValueStartIndex;
                var length = attribute.ValueLength;
                if (start <= node.StreamPosition || start + length > tagEnd) continue;
                var quote = html[start - 1];
                if (quote is not ('\'' or '"')) encoded = "\"" + encoded + "\"";
                if (!string.Equals(html.Substring(start, length), encoded, StringComparison.Ordinal))
                    replacements.Add((start, length, encoded));
            }
            if (additions.Length > 0)
            {
                var insertAt = tagEnd;
                var attributeStart = node.StreamPosition + node.Name.Length + 1;
                var selfClosing = HasHtmlSelfClosingMarker(html.Substring(attributeStart, tagEnd - attributeStart));
                if (selfClosing) insertAt--;
                replacements.Add((insertAt, 0, additions.ToString() + (selfClosing ? " " : "")));
            }
        }
        return ApplyReplacements(html, replacements);
    }

    private static bool HasHtmlSelfClosingMarker(string attributes)
    {
        if (!attributes.EndsWith("/", StringComparison.Ordinal))
            return false;
        if (attributes.Length == 1)
            return true;

        var state = HtmlAttributeParseState.BeforeName;
        for (var index = 0; index < attributes.Length - 1; index++)
        {
            var character = attributes[index];
            state = state switch
            {
                HtmlAttributeParseState.BeforeName when IsHtmlSpace(character) => state,
                HtmlAttributeParseState.BeforeName => HtmlAttributeParseState.Name,
                HtmlAttributeParseState.Name when IsHtmlSpace(character) => HtmlAttributeParseState.AfterName,
                HtmlAttributeParseState.Name when character == '=' => HtmlAttributeParseState.BeforeValue,
                HtmlAttributeParseState.Name => state,
                HtmlAttributeParseState.AfterName when IsHtmlSpace(character) => state,
                HtmlAttributeParseState.AfterName when character == '=' => HtmlAttributeParseState.BeforeValue,
                HtmlAttributeParseState.AfterName => HtmlAttributeParseState.Name,
                HtmlAttributeParseState.BeforeValue when IsHtmlSpace(character) => state,
                HtmlAttributeParseState.BeforeValue when character == '\'' => HtmlAttributeParseState.SingleQuotedValue,
                HtmlAttributeParseState.BeforeValue when character == '"' => HtmlAttributeParseState.DoubleQuotedValue,
                HtmlAttributeParseState.BeforeValue => HtmlAttributeParseState.UnquotedValue,
                HtmlAttributeParseState.UnquotedValue when IsHtmlSpace(character) => HtmlAttributeParseState.BeforeName,
                HtmlAttributeParseState.UnquotedValue => state,
                HtmlAttributeParseState.SingleQuotedValue when character == '\'' => HtmlAttributeParseState.AfterName,
                HtmlAttributeParseState.SingleQuotedValue => state,
                HtmlAttributeParseState.DoubleQuotedValue when character == '"' => HtmlAttributeParseState.AfterName,
                HtmlAttributeParseState.DoubleQuotedValue => state,
                _ => state
            };
        }

        return state is HtmlAttributeParseState.BeforeName
            or HtmlAttributeParseState.Name
            or HtmlAttributeParseState.AfterName;
    }

    private static bool IsHtmlSpace(char value)
        => value is '\t' or '\n' or '\f' or '\r' or ' ';

    private enum HtmlAttributeParseState
    {
        BeforeName,
        Name,
        AfterName,
        BeforeValue,
        UnquotedValue,
        SingleQuotedValue,
        DoubleQuotedValue
    }

    private static int FindOpeningTagEnd(string html, int start)
    {
        if (start < 0 || start >= html.Length || html[start] != '<') return -1;
        char quote = '\0';
        for (var index = start + 1; index < html.Length; index++)
        {
            var current = html[index];
            if (quote != '\0') { if (current == quote) quote = '\0'; }
            else if (current is '\'' or '"') quote = current;
            else if (current == '>') return index;
        }
        return -1;
    }

    internal static string Rewrite(string html, Func<string, string, string> rewrite)
    {
        if (string.IsNullOrEmpty(html)) return html;
        var document = HtmlParser.ParseWithHtmlAgilityPack(html);
        var replacements = new List<(int Start, int Length, string Value)>();
        foreach (var node in document.DocumentNode.Descendants())
        {
            foreach (var attribute in node.Attributes)
            {
                var start = attribute.ValueStartIndex;
                var length = attribute.ValueLength;
                if (start < 1 || length == 0 || start + length > html.Length) continue;
                var quote = html[start - 1];
                var quoted = quote is '\'' or '"';
                if (quoted && (start + length == html.Length || html[start + length] != quote)) continue;
                var value = html.Substring(start, length);
                var changed = rewrite(attribute.Name, value);
                if (!string.Equals(value, changed, StringComparison.Ordinal))
                {
                    // Minification can remove quotes. Quote the replacement so a rewritten
                    // value containing spaces or delimiters remains a single attribute.
                    if (!quoted)
                        changed = "\"" + changed.Replace("\"", "&quot;", StringComparison.Ordinal) + "\"";
                    replacements.Add((start, length, changed));
                }
            }
        }
        return ApplyReplacements(html, replacements);
    }

    private static string ApplyReplacements(string html, List<(int Start, int Length, string Value)> replacements)
    {
        if (replacements.Count == 0) return html;
        var result = new StringBuilder(html);
        foreach (var replacement in replacements.OrderByDescending(item => item.Start))
            result.Remove(replacement.Start, replacement.Length).Insert(replacement.Start, replacement.Value);
        return result.ToString();
    }
}
