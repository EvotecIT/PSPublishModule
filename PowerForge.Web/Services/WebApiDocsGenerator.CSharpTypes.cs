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
        Func<string, string, string>? renderName = null)
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
            var rendered = renderName?.Invoke(identity, display) ?? display;

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
                var arguments = SplitTypeArguments(text[(open + 1)..(position - 1)])
                    .Select(argument => FormatCSharpTypeName(argument, shortNames, renderName)).ToArray();
                rendered = identity is "System.Nullable" or "System.Nullable`1" && arguments.Length == 1
                    ? arguments[0] + "?"
                    : rendered + (renderName is null ? "<" : "&lt;") + string.Join(", ", arguments) +
                        (renderName is null ? ">" : "&gt;");
            }
            result.Append(rendered);
        }
        return result.ToString();
    }
}
