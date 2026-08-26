namespace PowerForge;

/// <summary>
/// Restricts runtime-independent executable output to values whose console rendering matches PowerShell.
/// </summary>
internal static class PowerShellTypedExecutableOutputPolicy
{
    internal static void EnsureSupported(Type type)
    {
        if (type == typeof(void) || IsScalar(type) || (type.IsArray && type.GetArrayRank() == 1 && IsScalar(type.GetElementType()!)))
            return;

        throw new InvalidOperationException(
            $"Strict typed executable output type '{type.FullName ?? type.Name}' requires PowerShell formatting semantics. " +
            "Use a supported scalar or one-dimensional scalar array result, or keep this script on the PowerShell runtime path.");
    }

    private static bool IsScalar(Type type)
        => type == typeof(bool) || type == typeof(byte) || type == typeof(sbyte) ||
           type == typeof(short) || type == typeof(ushort) || type == typeof(int) || type == typeof(uint) ||
           type == typeof(long) || type == typeof(ulong) || type == typeof(float) || type == typeof(double) ||
           type == typeof(decimal) || type == typeof(char) || type == typeof(string);
}
