using System.Globalization;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Numerics;

namespace PowerForge;

/// <summary>Resolves only parser-safe constants and records their target-typed value.</summary>
internal static class PowerShellCompilationLiteralPolicy
{
    internal static bool TryResolve(ExpressionAst expression, Type targetType, out PowerShellCompilationLiteral? literal)
        => TryResolve(expression, targetType, targetFramework: null, out literal);

    internal static bool TryResolve(
        ExpressionAst expression,
        Type targetType,
        string? targetFramework,
        out PowerShellCompilationLiteral? literal)
        => TryResolve(
            expression,
            targetType,
            targetFramework,
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            out literal);

    internal static bool TryResolve(
        ExpressionAst expression,
        Type targetType,
        string? targetFramework,
        string semanticProfileId,
        out PowerShellCompilationLiteral? literal)
    {
        literal = null;
        if (targetType == typeof(Array) &&
            expression is ConvertExpressionAst conversion &&
            conversion.StaticType != typeof(Array))
            return false;
        return TryResolveValue(expression, targetType, targetFramework, semanticProfileId, out var converted) &&
               TryEncodeValue(converted, targetType, targetFramework, out literal);
    }

    internal static bool TryResolveValue(ExpressionAst expression, Type targetType, out object? converted)
        => TryResolveValue(expression, targetType, targetFramework: null, out converted);

    internal static bool TryResolveValue(
        ExpressionAst expression,
        Type targetType,
        string? targetFramework,
        out object? converted)
        => TryResolveValue(
            expression,
            targetType,
            targetFramework,
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            out converted);

    internal static bool TryResolveValue(
        ExpressionAst expression,
        Type targetType,
        string? targetFramework,
        string semanticProfileId,
        out object? converted)
    {
        converted = null;
        if (!IsLiteralSyntaxSupportedByProfile(expression, semanticProfileId))
            return false;
        if (TryResolveExactEnumMember(expression, targetType, targetFramework, out converted))
            return true;

        object? raw;
        try
        {
            raw = (expression is ConvertExpressionAst conversion ? conversion.Child : expression).SafeGetValue();
        }
        catch (Exception exception) when (exception is InvalidOperationException or RuntimeException)
        {
            return false;
        }

        // This bounded System.Array default contract promotes one stable scalar.
        // Authored collection expressions need their own shape and enumeration proof.
        if (targetType == typeof(Array) && raw is Array)
            return false;

        try
        {
            converted = LanguagePrimitives.ConvertTo(raw, targetType, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception exception) when (exception is PSInvalidCastException or InvalidCastException or ArgumentException or FormatException or OverflowException)
        {
            return false;
        }
    }

    private static bool TryResolveExactEnumMember(
        ExpressionAst expression,
        Type targetType,
        string? targetFramework,
        out object? value)
    {
        value = null;
        var enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (!enumType.IsEnum)
            return false;

        while (expression is ParenExpressionAst
               {
                   Pipeline: PipelineAst { PipelineElements.Count: 1 } pipeline
               } &&
               pipeline.PipelineElements[0] is CommandExpressionAst
               {
                   Redirections.Count: 0,
                   Expression: var nested
               })
        {
            expression = nested;
        }

        if (expression is not MemberExpressionAst
            {
                Static: true,
                Expression: TypeExpressionAst typeExpression,
                Member: StringConstantExpressionAst member
            } ||
            typeExpression.TypeName.GetReflectionType() != enumType)
            return false;

        return PowerShellGeneratedTypePolicy.TryResolveEnumMember(
            enumType,
            member.Value,
            targetFramework,
            out value);
    }

    internal static bool TryEncodeValue(object? value, Type targetType, out PowerShellCompilationLiteral? literal)
        => TryEncodeValue(value, targetType, targetFramework: null, out literal);

    private static bool TryEncodeValue(
        object? value,
        Type targetType,
        string? targetFramework,
        out PowerShellCompilationLiteral? literal)
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

        if (targetType == typeof(Array) && value.GetType() == typeof(object[]))
        {
            var systemArrayValues = (object[])value;
            var elements = new List<PowerShellCompilationLiteral>(systemArrayValues.Length);
            foreach (var item in systemArrayValues)
            {
                var elementType = item?.GetType() ?? typeof(object);
                if (elementType.IsArray ||
                    !PowerShellGeneratedTypePolicy.IsSupported(elementType, targetFramework) ||
                    (item is not null && Type.GetType(elementType.FullName ?? elementType.Name, throwOnError: false) != elementType) ||
                    !TryEncodeValue(item, elementType, targetFramework, out var element) ||
                    element is null)
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

        if (targetType.IsArray && targetType.GetArrayRank() == 1 && value is Array array)
        {
            var elementType = targetType.GetElementType()!;
            var elements = new List<PowerShellCompilationLiteral>(array.Length);
            foreach (var item in array)
            {
                if (!TryEncodeValue(item, elementType, targetFramework, out var element) || element is null)
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

    internal static bool CanEmitBoundValue(object? value, Type targetType)
    {
        // System.Array literals are currently a parameter-default contract only. General
        // conversions need their own heterogeneous bound-array representation first.
        if (targetType == typeof(Array)) return false;
        if (TryEncodeValue(value, targetType, out _)) return true;
        if (value is null) return false;
        if (targetType.IsArray && targetType.GetArrayRank() == 1 && value is Array array)
        {
            var elementType = targetType.GetElementType()!;
            return array.Cast<object?>().All(item => CanEmitBoundValue(item, elementType));
        }
        var scalar = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return scalar == typeof(BigInteger) && value is BigInteger ||
               scalar == typeof(SwitchParameter) && value is SwitchParameter;
    }

    private static bool IsLiteralSyntaxSupportedByProfile(ExpressionAst expression, string semanticProfileId)
    {
        var constants = expression is ConstantExpressionAst constant
            ? new[] { constant }
            : expression.FindAll(static node => node is ConstantExpressionAst, searchNestedScriptBlocks: true)
                .OfType<ConstantExpressionAst>();
        return constants
            .Where(static item => item.Value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal or BigInteger)
            .All(item => PowerShellSourceProfileSyntaxPolicy.IsNumericLiteralSupported(item.Extent.Text, semanticProfileId));
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
