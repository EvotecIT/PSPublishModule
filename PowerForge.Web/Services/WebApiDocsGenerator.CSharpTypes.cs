using System.Text;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    // Match identities, never short CLR names: a library may declare its own String or Int32.
    private static string? GetCSharpKeyword(string? fullName) => fullName switch
    {
        "System.Void" => "void",
        "System.Boolean" => "bool",
        "System.Byte" => "byte",
        "System.SByte" => "sbyte",
        "System.Char" => "char",
        "System.Decimal" => "decimal",
        "System.Double" => "double",
        "System.Single" => "float",
        "System.Int32" => "int",
        "System.UInt32" => "uint",
        "System.Int64" => "long",
        "System.UInt64" => "ulong",
        "System.Int16" => "short",
        "System.UInt16" => "ushort",
        "System.IntPtr" => "nint",
        "System.UIntPtr" => "nuint",
        "System.String" => "string",
        "System.Object" => "object",
        _ => null
    };

    /// <summary>Formats a type expression for display without rewriting documentation identities,
    /// member names or literal text. Accepts XML documentation and C# generic delimiters.</summary>
    private static string FormatCSharpTypeName(string name, bool shortNames = false,
        Func<string, string, string>? renderName = null, Func<string, string?>? resolveDefinition = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;
        var text = name.Replace('+', '.').Replace('<', '{').Replace('>', '}').Trim();
        var result = new StringBuilder();
        for (var position = 0; position < text.Length;)
        {
            if (!char.IsLetterOrDigit(text[position]) && text[position] is not '_' and not '`')
            {
                var punctuation = text[position++].ToString();
                result.Append(renderName is null ? punctuation : System.Web.HttpUtility.HtmlEncode(punctuation));
                continue;
            }

            var start = position;
            while (position < text.Length &&
                   (char.IsLetterOrDigit(text[position]) || text[position] is '_' or '`' or '.'))
                position++;
            var identity = text[start..position];
            var keyword = GetCSharpKeyword(identity);
            // Standalone `0 / ``0 are XML type/method parameter references, not named-type arity.
            var display = identity[0] == '`' ? identity :
                keyword ?? StripGenericArity(shortNames ? identity[(identity.LastIndexOf('.') + 1)..] : identity);
            string[]? rawArguments = null;
            if (position < text.Length && text[position] == '{')
            {
                var open = position++;
                var depth = 1;
                while (position < text.Length && depth > 0)
                {
                    if (text[position] == '{') depth++;
                    if (text[position] == '}') depth--;
                    position++;
                }
                if (depth != 0) return renderName is null ? name : System.Web.HttpUtility.HtmlEncode(name);
                rawArguments = SplitTypeArguments(text[(open + 1)..(position - 1)]).ToArray();
                var arguments = rawArguments.Select(argument => FormatCSharpTypeName(argument, shortNames, resolveDefinition: resolveDefinition)).ToArray();
                if (identity is "System.Nullable" or "System.Nullable`1" && arguments.Length == 1)
                {
                    result.Append(FormatCSharpTypeName(rawArguments[0], shortNames, renderName, resolveDefinition)).Append('?');
                    continue;
                }
                display += "<" + string.Join(", ", arguments) + ">";
            }
            else if (identity[0] != '`' && identity.Contains('`'))
            {
                display = resolveDefinition?.Invoke(identity) ?? FormatOpenGenericName(identity, shortNames);
            }
            var linkIdentity = rawArguments is not null && !identity.Contains('`')
                ? identity + "`" + rawArguments.Length : identity;
            var rendered = renderName?.Invoke(linkIdentity, display) ?? display;
            // A linked generic owns its complete label. For unlinked containers, retain links
            // to documented argument types without ever nesting anchors.
            if (renderName is not null && rawArguments is not null && rendered == System.Web.HttpUtility.HtmlEncode(display))
                rendered = System.Web.HttpUtility.HtmlEncode(display[..display.IndexOf('<')]) + "&lt;" +
                    string.Join(", ", rawArguments.Select(argument => FormatCSharpTypeName(argument, shortNames, renderName, resolveDefinition))) + "&gt;";
            result.Append(rendered);
        }
        return result.ToString();
    }

    private static string FormatOpenGenericName(string identity, bool shortNames)
    {
        var name = shortNames ? identity[(identity.LastIndexOf('.') + 1)..] : identity;
        return System.Text.RegularExpressions.Regex.Replace(name, @"`(\d+)", match =>
        {
            var count = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            return "<" + string.Join(", ", Enumerable.Range(1, count).Select(index => count == 1 ? "T" : "T" + index)) + ">";
        });
    }
}
