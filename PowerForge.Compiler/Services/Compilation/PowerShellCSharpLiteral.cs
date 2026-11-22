using System.Globalization;
using System.Text;

namespace PowerForge;

internal static class PowerShellCSharpLiteral
{
    internal static string Emit(PowerShellCompilationLiteral literal, Type targetType, Func<Type, string> getTypeName)
    {
        if (literal is null) throw new ArgumentNullException(nameof(literal));
        if (targetType is null) throw new ArgumentNullException(nameof(targetType));
        if (getTypeName is null) throw new ArgumentNullException(nameof(getTypeName));

        if (literal.Kind == PowerShellCompilationLiteralKind.Null)
            return "default!";
        if (literal.Kind == PowerShellCompilationLiteralKind.Array)
        {
            if (targetType == typeof(Array))
            {
                var systemArrayElements = literal.Elements.Select(element =>
                {
                    if (element.Kind == PowerShellCompilationLiteralKind.Null) return "null";
                    var elementType = Type.GetType(element.TypeName, throwOnError: false)
                        ?? throw new InvalidOperationException($"System.Array literal element type '{element.TypeName}' could not be resolved.");
                    return $"(object?)({Emit(element, elementType, getTypeName)})";
                });
                return $"new object?[] {{ {string.Join(", ", systemArrayElements)} }}";
            }
            var elementType = targetType.GetElementType()
                ?? throw new InvalidOperationException($"Literal target '{targetType.FullName}' is not an array.");
            var elements = literal.Elements.Select(element => Emit(element, elementType, getTypeName));
            return $"new {getTypeName(elementType)}[] {{ {string.Join(", ", elements)} }}";
        }

        var scalarType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        var scalar = EmitScalar(literal, scalarType, getTypeName);
        return Nullable.GetUnderlyingType(targetType) is null
            ? scalar
            : $"new {getTypeName(targetType)}({scalar})";
    }

    private static string EmitScalar(PowerShellCompilationLiteral literal, Type type, Func<Type, string> getTypeName)
    {
        var value = literal.Value ?? throw new InvalidOperationException($"Literal '{literal.Kind}' has no invariant value.");
        var quoted = QuoteString(value);
        var invariant = "global::System.Globalization.CultureInfo.InvariantCulture";
        return literal.Kind switch
        {
            PowerShellCompilationLiteralKind.Boolean => value,
            PowerShellCompilationLiteralKind.SignedInteger or PowerShellCompilationLiteralKind.UnsignedInteger =>
                $"{getTypeName(type)}.Parse({quoted}, global::System.Globalization.NumberStyles.Integer, {invariant})",
            PowerShellCompilationLiteralKind.FloatingPoint =>
                $"{getTypeName(type)}.Parse({quoted}, global::System.Globalization.NumberStyles.Float, {invariant})",
            PowerShellCompilationLiteralKind.Decimal =>
                $"global::System.Decimal.Parse({quoted}, global::System.Globalization.NumberStyles.Float, {invariant})",
            PowerShellCompilationLiteralKind.Character => QuoteChar(value.Single()),
            PowerShellCompilationLiteralKind.String => quoted,
            PowerShellCompilationLiteralKind.Enum => EmitEnum(value, type, getTypeName, invariant),
            PowerShellCompilationLiteralKind.Guid => $"new global::System.Guid({quoted})",
            PowerShellCompilationLiteralKind.DateTime =>
                $"global::System.DateTime.ParseExact({quoted}, \"O\", {invariant}, global::System.Globalization.DateTimeStyles.RoundtripKind)",
            PowerShellCompilationLiteralKind.DateTimeOffset =>
                $"global::System.DateTimeOffset.ParseExact({quoted}, \"O\", {invariant}, global::System.Globalization.DateTimeStyles.None)",
            PowerShellCompilationLiteralKind.TimeSpan =>
                $"global::System.TimeSpan.ParseExact({quoted}, \"c\", {invariant})",
            PowerShellCompilationLiteralKind.Uri =>
                $"new global::System.Uri({quoted}, global::System.UriKind.RelativeOrAbsolute)",
            PowerShellCompilationLiteralKind.Version => $"new global::System.Version({quoted})",
            _ => throw new InvalidOperationException($"Literal kind '{literal.Kind}' cannot be emitted as a scalar.")
        };
    }

    private static string EmitEnum(string value, Type enumType, Func<Type, string> getTypeName, string invariant)
    {
        var underlyingType = Enum.GetUnderlyingType(enumType);
        var parsed = $"{getTypeName(underlyingType)}.Parse({QuoteString(value)}, global::System.Globalization.NumberStyles.Integer, {invariant})";
        return $"({getTypeName(enumType)}){parsed}";
    }

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
