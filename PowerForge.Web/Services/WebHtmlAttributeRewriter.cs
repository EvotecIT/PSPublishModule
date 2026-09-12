using System.Text;
using HtmlTinkerX;

namespace PowerForge.Web;

/// <summary>Rewrites parsed, quoted attribute values without reserializing page content.</summary>
internal static class WebHtmlAttributeRewriter
{
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
                if (start < 1 || start + length >= html.Length) continue;
                var quote = html[start - 1];
                if ((quote != '\'' && quote != '"') || html[start + length] != quote) continue;
                var value = html.Substring(start, length);
                var changed = rewrite(attribute.Name, value);
                if (!string.Equals(value, changed, StringComparison.Ordinal))
                    replacements.Add((start, length, changed));
            }
        }
        if (replacements.Count == 0) return html;
        var result = new StringBuilder(html);
        foreach (var replacement in replacements.OrderByDescending(item => item.Start))
            result.Remove(replacement.Start, replacement.Length).Insert(replacement.Start, replacement.Value);
        return result.ToString();
    }
}
