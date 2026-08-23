using System.Reflection;

namespace PowerForge;

/// <summary>
/// Restricts generated operators to CLR shapes whose static behavior is understood.
/// </summary>
internal static class PowerShellCSharpOperatorPolicy
{
    internal static bool SupportsCompoundAssignment(string operation, Type left, Type right)
    {
        if (left.IsArray || right.IsArray)
            return false;
        if (operation == "PlusEquals" && left == typeof(string) && right == typeof(string))
            return true;
        if (!IsNumeric(left) || !IsNumeric(right))
            return false;
        if (IsIntegral(left) && (operation == "DivideEquals" || !IsIntegral(right)))
            return false;
        if (left == typeof(ulong) && IsSignedIntegral(right) || right == typeof(ulong) && IsSignedIntegral(left))
            return false;
        if ((left == typeof(decimal)) != (right == typeof(decimal)))
            return false;
        return operation is "PlusEquals" or "MinusEquals" or "MultiplyEquals" or "DivideEquals" or "RemEquals";
    }

    internal static bool SupportsEquality(Type type)
    {
        if (type.IsEnum || type == typeof(bool) || type == typeof(char) || IsNumeric(type))
            return true;
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        return HasEqualityOperator(methods, type, "op_Equality") &&
               HasEqualityOperator(methods, type, "op_Inequality");
    }

    internal static bool SupportsIncrement(Type type) => IsNumeric(type);

    private static bool HasEqualityOperator(IEnumerable<MethodInfo> methods, Type type, string name)
        => methods.Any(method =>
        {
            if (method.Name != name || method.ReturnType != typeof(bool))
                return false;
            var parameters = method.GetParameters();
            return parameters.Length == 2 && parameters[0].ParameterType == type && parameters[1].ParameterType == type;
        });

    private static bool IsNumeric(Type type)
        => type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
           type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) ||
           type == typeof(float) || type == typeof(double) || type == typeof(decimal);

    private static bool IsSignedIntegral(Type type)
        => type == typeof(sbyte) || type == typeof(short) || type == typeof(int) || type == typeof(long);

    private static bool IsIntegral(Type type)
        => type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
           type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong);
}
