using System.Globalization;
using System.Management.Automation.Language;
using System.Text;

namespace PowerForge;

internal sealed partial class PowerShellCSharpMethodEmitter
{
    private static bool CanAssign(Type target, Type source)
    {
        if (target == source || target.IsAssignableFrom(source)) return true;
        if (!IsNumeric(target) || !IsNumeric(source)) return false;
        return source == typeof(sbyte) && (target == typeof(short) || target == typeof(int) || target == typeof(long) || target == typeof(float) || target == typeof(double) || target == typeof(decimal)) ||
               source == typeof(byte) && (target == typeof(short) || target == typeof(ushort) || target == typeof(int) || target == typeof(uint) || target == typeof(long) || target == typeof(ulong) || target == typeof(float) || target == typeof(double) || target == typeof(decimal)) ||
               source == typeof(short) && (target == typeof(int) || target == typeof(long) || target == typeof(float) || target == typeof(double) || target == typeof(decimal)) ||
               source == typeof(ushort) && (target == typeof(int) || target == typeof(uint) || target == typeof(long) || target == typeof(ulong) || target == typeof(float) || target == typeof(double) || target == typeof(decimal)) ||
               source == typeof(int) && (target == typeof(long) || target == typeof(float) || target == typeof(double) || target == typeof(decimal)) ||
               source == typeof(uint) && (target == typeof(long) || target == typeof(ulong) || target == typeof(float) || target == typeof(double) || target == typeof(decimal)) ||
               source == typeof(long) && (target == typeof(float) || target == typeof(double) || target == typeof(decimal)) ||
               source == typeof(ulong) && (target == typeof(float) || target == typeof(double) || target == typeof(decimal)) ||
               source == typeof(float) && target == typeof(double);
    }

    internal static string SanitizeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Generated";
        var builder = new StringBuilder(value.Length + 1);
        if (!char.IsLetter(value[0]) && value[0] != '_') builder.Append('_');
        foreach (var character in value)
            builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        var identifier = builder.ToString();
        return CSharpKeywords.Contains(identifier) ? "@" + identifier : identifier;
    }

    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue",
        "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed",
        "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace",
        "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return",
        "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try",
        "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
    };

    internal static string GetTypeName(Type type)
    {
        if (type.IsArray) return GetTypeName(type.GetElementType()!) + "[]";
        if (type.IsGenericType)
        {
            var definitionName = (type.GetGenericTypeDefinition().FullName ?? type.Name).Split('`')[0].Replace('+', '.');
            return $"global::{definitionName}<{string.Join(", ", type.GetGenericArguments().Select(GetTypeName))}>";
        }
        if (type == typeof(void)) return "void";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(byte)) return "byte";
        if (type == typeof(sbyte)) return "sbyte";
        if (type == typeof(short)) return "short";
        if (type == typeof(ushort)) return "ushort";
        if (type == typeof(int)) return "int";
        if (type == typeof(uint)) return "uint";
        if (type == typeof(long)) return "long";
        if (type == typeof(ulong)) return "ulong";
        if (type == typeof(float)) return "float";
        if (type == typeof(double)) return "double";
        if (type == typeof(decimal)) return "decimal";
        if (type == typeof(char)) return "char";
        if (type == typeof(string)) return "string";
        if (type == typeof(object)) return "object";
        return "global::" + (type.FullName ?? type.Name).Replace('+', '.');
    }

    private static string EmitConstant(ConstantExpressionAst constant)
        => constant.Value switch
        {
            null => "null",
            bool value => value ? "true" : "false",
            string value => EmitString(value),
            char value => EmitChar(value),
            float value => value.ToString("R", CultureInfo.InvariantCulture) + "F",
            double value => value.ToString("R", CultureInfo.InvariantCulture) + "D",
            decimal value => value.ToString(CultureInfo.InvariantCulture) + "M",
            long value => value.ToString(CultureInfo.InvariantCulture) + "L",
            ulong value => value.ToString(CultureInfo.InvariantCulture) + "UL",
            uint value => value.ToString(CultureInfo.InvariantCulture) + "U",
            System.Numerics.BigInteger value =>
                $"global::System.Numerics.BigInteger.Parse({EmitString(value.ToString(CultureInfo.InvariantCulture))}, global::System.Globalization.CultureInfo.InvariantCulture)",
            IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
            _ => throw new PowerShellCSharpEmissionException(constant, $"Constant type '{constant.Value.GetType().FullName}' is not supported.")
        };

    private static string EmitString(string value) => PowerShellCSharpLiteral.QuoteString(value);
    private static string EmitChar(char value) => PowerShellCSharpLiteral.QuoteChar(value);
}
