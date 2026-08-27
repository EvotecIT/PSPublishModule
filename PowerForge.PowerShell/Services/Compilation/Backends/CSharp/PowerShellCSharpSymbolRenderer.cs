namespace PowerForge;

/// <summary>
/// Renders semantic CLR symbols in generated C# without depending on an AST emitter.
/// </summary>
internal static class PowerShellCSharpSymbolRenderer
{
    internal static string Identifier(string value)
        => PowerShellClrSymbolMapper.MapIdentifier(value);

    internal static string TypeName(Type type)
    {
        if (type.IsArray) return TypeName(type.GetElementType()!) + "[]";
        if (type.IsGenericType)
        {
            var definitionName = (type.GetGenericTypeDefinition().FullName ?? type.Name).Split('`')[0].Replace('+', '.');
            return $"global::{definitionName}<{string.Join(", ", type.GetGenericArguments().Select(TypeName))}>";
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
}
