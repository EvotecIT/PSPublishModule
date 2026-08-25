using System.Globalization;
using System.Management.Automation;
using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Resolves only parser-safe constants and records their target-typed value.</summary>
internal static class PowerShellCompilationLiteralPolicy
{
    internal static bool TryResolve(ExpressionAst expression, Type targetType, out PowerShellCompilationLiteral? literal)
    {
        literal = null;
        object? raw;
        try
        {
            raw = expression.SafeGetValue();
        }
        catch (Exception exception) when (exception is InvalidOperationException or RuntimeException)
        {
            return false;
        }

        var compiledType = PowerShellCompilationParameterTypePolicy.GetCompiledType(targetType);
        object? converted;
        try
        {
            converted = LanguagePrimitives.ConvertTo(raw, compiledType, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is PSInvalidCastException or InvalidCastException or ArgumentException or FormatException or OverflowException)
        {
            return false;
        }
        return TryEncode(converted, compiledType, out literal);
    }

    private static bool TryEncode(object? value, Type targetType, out PowerShellCompilationLiteral? literal)
    {
        if (value is null)
        {
            if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) is null)
            {
                literal = null;
                return false;
            }
            literal = new PowerShellCompilationLiteral(PowerShellCompilationLiteralKind.Null, GetTypeName(targetType));
            return true;
        }

        if (targetType.IsArray && targetType.GetArrayRank() == 1 && value is Array array)
        {
            var elementType = targetType.GetElementType()!;
            var elements = new List<PowerShellCompilationLiteral>(array.Length);
            foreach (var item in array)
            {
                if (!TryEncode(item, elementType, out var element) || element is null)
                {
                    literal = null;
                    return false;
                }
                elements.Add(element);
            }
            literal = new PowerShellCompilationLiteral(
                PowerShellCompilationLiteralKind.Array,
                GetTypeName(targetType),
                elements: elements.ToArray());
            return true;
        }

        var scalar = Nullable.GetUnderlyingType(targetType) ?? targetType;
        var typeName = GetTypeName(targetType);
        var kind = GetKind(scalar);
        if (!kind.HasValue)
        {
            literal = null;
            return false;
        }
        var invariant = GetInvariantValue(value, scalar, kind.Value);
        if (invariant is null)
        {
            literal = null;
            return false;
        }
        literal = new PowerShellCompilationLiteral(kind.Value, typeName, invariant);
        return true;
    }

    private static PowerShellCompilationLiteralKind? GetKind(Type type)
    {
        if (type.IsEnum) return PowerShellCompilationLiteralKind.Enum;
        if (type == typeof(bool)) return PowerShellCompilationLiteralKind.Boolean;
        if (type == typeof(sbyte) || type == typeof(short) || type == typeof(int) || type == typeof(long)) return PowerShellCompilationLiteralKind.SignedInteger;
        if (type == typeof(byte) || type == typeof(ushort) || type == typeof(uint) || type == typeof(ulong)) return PowerShellCompilationLiteralKind.UnsignedInteger;
        if (type == typeof(float) || type == typeof(double)) return PowerShellCompilationLiteralKind.FloatingPoint;
        if (type == typeof(decimal)) return PowerShellCompilationLiteralKind.Decimal;
        if (type == typeof(char)) return PowerShellCompilationLiteralKind.Character;
        if (type == typeof(string)) return PowerShellCompilationLiteralKind.String;
        if (type == typeof(Guid)) return PowerShellCompilationLiteralKind.Guid;
        if (type == typeof(DateTime)) return PowerShellCompilationLiteralKind.DateTime;
        if (type == typeof(DateTimeOffset)) return PowerShellCompilationLiteralKind.DateTimeOffset;
        if (type == typeof(TimeSpan)) return PowerShellCompilationLiteralKind.TimeSpan;
        if (type == typeof(Uri)) return PowerShellCompilationLiteralKind.Uri;
        if (type == typeof(Version)) return PowerShellCompilationLiteralKind.Version;
        return null;
    }

    private static string? GetInvariantValue(object value, Type type, PowerShellCompilationLiteralKind kind)
        => kind switch
        {
            PowerShellCompilationLiteralKind.Boolean => (bool)value ? "true" : "false",
            PowerShellCompilationLiteralKind.Enum => GetEnumInvariant(value, type),
            PowerShellCompilationLiteralKind.DateTime => ((DateTime)value).ToString("O", CultureInfo.InvariantCulture),
            PowerShellCompilationLiteralKind.DateTimeOffset => ((DateTimeOffset)value).ToString("O", CultureInfo.InvariantCulture),
            PowerShellCompilationLiteralKind.TimeSpan => ((TimeSpan)value).ToString("c", CultureInfo.InvariantCulture),
            PowerShellCompilationLiteralKind.Guid => ((Guid)value).ToString("D"),
            PowerShellCompilationLiteralKind.Uri => ((Uri)value).OriginalString,
            PowerShellCompilationLiteralKind.Version => ((Version)value).ToString(),
            PowerShellCompilationLiteralKind.Character => value.ToString(),
            PowerShellCompilationLiteralKind.String => (string)value,
            _ => value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : null
        };

    private static string GetEnumInvariant(object value, Type enumType)
    {
        var underlying = Enum.GetUnderlyingType(enumType);
        return underlying == typeof(sbyte) || underlying == typeof(short) || underlying == typeof(int) || underlying == typeof(long)
            ? Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
            : Convert.ToUInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
    }

    private static string GetTypeName(Type type) => type.FullName ?? type.Name;
}
