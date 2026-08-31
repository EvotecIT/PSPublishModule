using System.Globalization;

namespace PowerForge;

/// <summary>Renders canonical literal values for the lowered C# backend.</summary>
internal sealed partial class PowerShellBoundCSharpBackend
{
    internal static string EmitLiteral(PowerShellLoweredLiteralExpression literal)
    {
        if (literal.Value is null) return "null";
        var nullableType = Nullable.GetUnderlyingType(literal.ClrType);
        if (nullableType is not null)
        {
            var scalar = new PowerShellLoweredLiteralExpression(literal.Span, nullableType, literal.Value);
            return $"new {PowerShellCSharpSymbolRenderer.TypeName(literal.ClrType)}({EmitLiteral(scalar)})";
        }
        if (literal.ClrType.IsEnum)
        {
            var underlying = Enum.GetUnderlyingType(literal.ClrType);
            var value = Type.GetTypeCode(underlying) is TypeCode.Byte or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64
                ? Convert.ToUInt64(literal.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "UL"
                : Convert.ToInt64(literal.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "L";
            return $"({PowerShellCSharpSymbolRenderer.TypeName(literal.ClrType)}){value}";
        }
        if (literal.Value is string text) return PowerShellCSharpLiteral.QuoteString(text);
        if (literal.Value is bool boolean) return boolean ? "true" : "false";
        if (literal.Value is System.Management.Automation.SwitchParameter switchParameter)
            return $"new global::System.Management.Automation.SwitchParameter({(switchParameter.IsPresent ? "true" : "false")})";
        if (literal.Value is char character) return PowerShellCSharpLiteral.QuoteChar(character);
        if (literal.Value is float single)
            return float.IsNaN(single) || float.IsInfinity(single)
                ? EmitCanonicalLiteral(literal)
                : single.ToString("R", CultureInfo.InvariantCulture) + "f";
        if (literal.Value is double doubleValue)
            return double.IsNaN(doubleValue) || double.IsInfinity(doubleValue)
                ? EmitCanonicalLiteral(literal)
                : doubleValue.ToString("R", CultureInfo.InvariantCulture) + "d";
        if (literal.Value is decimal decimalValue) return decimalValue.ToString(CultureInfo.InvariantCulture) + "m";
        if (literal.Value is sbyte signedByte) return $"(sbyte){signedByte.ToString(CultureInfo.InvariantCulture)}";
        if (literal.Value is byte unsignedByte) return $"(byte){unsignedByte.ToString(CultureInfo.InvariantCulture)}";
        if (literal.Value is short signedShort) return $"(short){signedShort.ToString(CultureInfo.InvariantCulture)}";
        if (literal.Value is ushort unsignedShort) return $"(ushort){unsignedShort.ToString(CultureInfo.InvariantCulture)}";
        if (literal.Value is int integer) return integer.ToString(CultureInfo.InvariantCulture);
        if (literal.Value is long longValue) return longValue.ToString(CultureInfo.InvariantCulture) + "L";
        if (literal.Value is ulong unsignedLong) return unsignedLong.ToString(CultureInfo.InvariantCulture) + "UL";
        if (literal.Value is uint unsignedInteger) return unsignedInteger.ToString(CultureInfo.InvariantCulture) + "U";
        if (literal.Value is Guid guid) return $"new global::System.Guid({PowerShellCSharpLiteral.QuoteString(guid.ToString("D"))})";
        if (literal.Value is DateTime dateTime)
            return $"new global::System.DateTime({dateTime.Ticks.ToString(CultureInfo.InvariantCulture)}L, (global::System.DateTimeKind){((int)dateTime.Kind).ToString(CultureInfo.InvariantCulture)})";
        if (literal.Value is DateTimeOffset dateTimeOffset)
            return $"new global::System.DateTimeOffset({dateTimeOffset.Ticks.ToString(CultureInfo.InvariantCulture)}L, new global::System.TimeSpan({dateTimeOffset.Offset.Ticks.ToString(CultureInfo.InvariantCulture)}L))";
        if (literal.Value is TimeSpan timeSpan) return $"new global::System.TimeSpan({timeSpan.Ticks.ToString(CultureInfo.InvariantCulture)}L)";
        if (literal.Value is Uri uri) return $"new global::System.Uri({PowerShellCSharpLiteral.QuoteString(uri.OriginalString)}, global::System.UriKind.RelativeOrAbsolute)";
        if (literal.Value is Version version) return $"new global::System.Version({PowerShellCSharpLiteral.QuoteString(version.ToString())})";
        if (literal.Value is System.Numerics.BigInteger bigInteger)
        {
            return $"global::System.Numerics.BigInteger.Parse({PowerShellCSharpLiteral.QuoteString(bigInteger.ToString(CultureInfo.InvariantCulture))}, global::System.Globalization.CultureInfo.InvariantCulture)";
        }
        throw new InvalidOperationException($"Literal value for '{literal.ClrType.FullName}' has no canonical C# encoding.");
    }

    private static string EmitCanonicalLiteral(PowerShellLoweredLiteralExpression literal)
    {
        if (!PowerShellCompilationLiteralPolicy.TryEncodeValue(literal.Value, literal.ClrType, out var encoded) || encoded is null)
            throw new InvalidOperationException($"Literal value for '{literal.ClrType.FullName}' has no canonical C# encoding.");
        return PowerShellCSharpLiteral.Emit(encoded, literal.ClrType, PowerShellCSharpSymbolRenderer.TypeName);
    }
}
