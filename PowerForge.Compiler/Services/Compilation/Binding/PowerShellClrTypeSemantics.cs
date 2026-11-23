namespace PowerForge;

/// <summary>Canonical CLR representation rules used while binding PowerShell semantics.</summary>
internal static class PowerShellClrTypeSemantics
{
    internal static bool IsNumeric(Type type)
        => type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
           type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) ||
           type == typeof(float) || type == typeof(double) || type == typeof(decimal);

    internal static bool IsIntegral(Type type)
        => type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
           type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong);

    internal static bool IsNonNullableValueType(Type type)
        => type.IsValueType && Nullable.GetUnderlyingType(type) is null;

    internal static bool CanAssign(Type target, Type source)
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

    internal static bool TryUnify(Type left, Type right, out Type result)
    {
        if (left == right) { result = left; return true; }
        if (CanAssign(left, right)) { result = left; return true; }
        if (CanAssign(right, left)) { result = right; return true; }
        if (IsNumeric(left) && IsNumeric(right))
        {
            foreach (var candidate in new[] { typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(decimal), typeof(float), typeof(double) })
            {
                if (CanAssign(candidate, left) && CanAssign(candidate, right)) { result = candidate; return true; }
            }
        }
        result = typeof(object);
        return false;
    }

    internal static Type PromoteIntegral(Type type)
        => type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
            ? typeof(int)
            : type;
}
